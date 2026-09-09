using System.Text.Json;
using Kneset.Core.Abstractions;
using Kneset.Core.Models;
using Kneset.Core.Entities;
using Kneset.Core.Legislation;
using Kneset.Infrastructure.Data;
using Kneset.Infrastructure.Knesset;
using Microsoft.EntityFrameworkCore;

namespace Kneset.Web.Services;

/// <summary>
/// Фоновая синхронизация с API Кнессета: статусы → депутаты → фракции → законопроекты → инициаторы.
/// Запускается через 5 секунд после старта и далее повторяется каждые Sync:IntervalHours часов.
/// Инкрементальная: фильтрует по LastUpdatedDate с момента последней успешной синхронизации (SyncLogs).
/// </summary>
public class KnessetSyncService(
    IDbContextFactory<AppDbContext> dbFactory,
    KnessetODataClient client,
    KnessetWebsiteClient websiteClient,
    WikisourceClient wikisource,
    AnalysisClaims claims,
    IAnalysisTranslator translator,
    ILawAnalyzer? lawAnalyzer,
    NotificationDispatchService notifications,
    IConfiguration configuration,
    ILogger<KnessetSyncService> logger) : BackgroundService
{
    private Dictionary<int, string> _statusDescById = [];

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Sync:IntervalHours=0 — режим «один прогон на холодный старт». Нужен там, где
        // инстанс засыпает без трафика: во сне таймер не тикает, поэтому периодический цикл
        // всё равно не сработает, а однократный прогон обновляет данные при каждом пробуждении.
        var intervalHours = configuration.GetValue("Sync:IntervalHours", 6d);
        var interval = intervalHours > 0 ? TimeSpan.FromHours(intervalHours) : (TimeSpan?)null;

        await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await SyncAllAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Синхронизация с Кнессетом завершилась ошибкой");
            }

            if (interval is null)
            {
                logger.LogInformation(
                    "Синхронизация выполнена однократно (Sync:IntervalHours=0), " +
                    "следующая — при следующем запуске приложения");
                return;
            }

            await Task.Delay(interval.Value, stoppingToken);
        }
    }

    private async Task SyncAllAsync(CancellationToken ct)
    {
        await SyncStatusesAsync(ct);
        await RunStepAsync("KnessetTerms", SyncKnessetTermsAsync, ct);
        await RunStepAsync("Persons", SyncPersonsAsync, ct);
        await RunStepAsync("Factions", SyncFactionsAsync, ct);
        await RunStepAsync("Photos", SyncPhotosAsync, ct);
        await RunStepAsync("Committees", SyncCommitteesAsync, ct);
        await RunStepAsync("Bills", SyncBillsAsync, ct);
        await RunStepAsync("BillsCommitteeBackfill", BackfillBillCommitteesAsync, ct);
        await RunStepAsync("BillInitiators", SyncInitiatorsAsync, ct);
        await RunStepAsync("BillSessions", SyncBillSessionsAsync, ct);
        await RunStepAsync("BillDocuments", SyncBillDocumentsAsync, ct);
        await RunStepAsync("IsraelLaws", SyncIsraelLawsAsync, ct);
        await RunStepAsync("LawActs", SyncLawActsAsync, ct);
        await RunStepAsync("LawAmendments", SyncLawAmendmentsAsync, ct);
        await RunStepAsync("LawTopics", SyncLawTopicsAsync, ct);
        await RunStepAsync("BillLawLinks", LinkBillsToLawsAsync, ct);
        await RunStepAsync("LawSiteLinks", SyncLawSiteLinksAsync, ct);
        await RunStepAsync("LawTexts", SyncLawTextsAsync, ct);
        await RunStepAsync("LawAnalyses", SyncLawAnalysesAsync, ct);

        // Строго последним: подписка на депутата опирается на BillInitiators,
        // которые заполняются шагом выше. RunStepAsync передаёт сюда время
        // последней успешной рассылки — прерванный прогон повторится.
        await RunStepAsync("Notifications", notifications.DispatchAsync, ct);
    }

    private async Task RunStepAsync(string entityName,
        Func<DateTime?, CancellationToken, Task<int>> step, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var lastSync = await db.SyncLogs
            .Where(s => s.EntityName == entityName && s.Error == null && s.FinishedUtc != null)
            .MaxAsync(s => (DateTime?)s.StartedUtc, ct);

        var log = new SyncLog { EntityName = entityName, StartedUtc = DateTime.UtcNow };
        db.SyncLogs.Add(log);
        await db.SaveChangesAsync(ct);

        try
        {
            log.RecordsUpserted = await step(lastSync, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            log.Error = ex.Message;
            logger.LogError(ex, "Шаг синхронизации {Entity} завершился ошибкой", entityName);
        }

        log.FinishedUtc = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
    }

    private async Task SyncStatusesAsync(CancellationToken ct)
    {
        var statuses = await client.GetStatusesAsync(ct);
        _statusDescById = statuses
            .Select(s => new { s.StatusID, Desc = Clean(s.Desc) })
            .Where(s => s.Desc is not null)
            .ToDictionary(s => s.StatusID, s => s.Desc!);
    }

    /// <summary>
    /// Границы созывов. В API это сессии пленума — по строке на сессию,
    /// у 25-го созыва их восемь. Сворачиваем в один созыв: начало —
    /// самая ранняя сессия, конец — самая поздняя.
    /// </summary>
    private async Task<int> SyncKnessetTermsAsync(DateTime? since, CancellationToken ct)
    {
        var rows = await client.GetKnessetDatesAsync(ct);
        if (rows.Count == 0) return 0;

        var terms = rows
            .Where(r => r.PlenumStart is not null)
            .GroupBy(r => r.KnessetNum)
            .Select(g => new
            {
                Num = g.Key,
                Name = Clean(g.First().Name),
                Start = DateOnly.FromDateTime(g.Min(r => r.PlenumStart!.Value)),
                // Конец созыва известен, только если у всех сессий он проставлен:
                // у идущего созыва последняя сессия ещё не закрыта.
                Finish = g.All(r => r.PlenumFinish is not null)
                    ? DateOnly.FromDateTime(g.Max(r => r.PlenumFinish!.Value))
                    : (DateOnly?)null,
                IsCurrent = g.Any(r => r.IsCurrent == true),
            })
            .ToList();

        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var existing = await db.KnessetTerms.ToDictionaryAsync(t => t.Id, ct);

        foreach (var src in terms)
        {
            if (!existing.TryGetValue(src.Num, out var term))
            {
                term = new KnessetTerm { Id = src.Num };
                db.KnessetTerms.Add(term);
            }
            term.Name = src.Name;
            term.StartDate = src.Start;
            term.FinishDate = src.Finish;
            term.IsCurrent = src.IsCurrent;
        }

        await db.SaveChangesAsync(ct);
        return terms.Count;
    }

    private async Task<int> SyncPersonsAsync(DateTime? since, CancellationToken ct)
    {
        var persons = await client.GetPersonsAsync(since, ct);
        if (persons.Count == 0) return 0;

        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var ids = persons.Select(p => p.PersonID).ToList();
        var existing = await db.Persons
            .Where(p => ids.Contains(p.KnessetPersonId))
            .ToDictionaryAsync(p => p.KnessetPersonId, ct);

        foreach (var src in persons)
        {
            if (!existing.TryGetValue(src.PersonID, out var person))
            {
                person = new Person { KnessetPersonId = src.PersonID };
                db.Persons.Add(person);
            }
            person.FirstName = CleanRequired(src.FirstName);
            person.LastName = CleanRequired(src.LastName);
            person.GenderDesc = Clean(src.GenderDesc);
            person.Email = Clean(src.Email);
            // IsCurrent здесь нарочно не трогаем. У KNS_Person это поле
            // означает не «действующий депутат»: таких записей 139 при 120
            // мандатах. Признак проставляет шаг фракций — по должности
            // «член фракции», которая даёт ровно 120.
            person.LastUpdatedDate = AsUtc(src.LastUpdatedDate);
        }

        await db.SaveChangesAsync(ct);
        return persons.Count;
    }

    private async Task<int> SyncFactionsAsync(DateTime? since, CancellationToken ct)
    {
        // Фракции текущих депутатов; объём небольшой, синхронизируем целиком.
        var memberships = await client.GetCurrentFactionMembershipsAsync(ct);
        if (memberships.Count == 0) return 0;

        var factionByPerson = memberships
            .GroupBy(m => m.PersonID)
            .ToDictionary(g => g.Key, g => (g.First().FactionID, Clean(g.First().FactionName)));

        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var ids = factionByPerson.Keys.ToList();

        // Снять признак с тех, кого в списке больше нет. Без этого шага
        // ушедший депутат навсегда оставался «действующим» со своей старой
        // фракцией: мы записывали только тех, кого вернул запрос, и никогда
        // никого не стирали. В базе из-за этого числился 121 депутат
        // при 120 мандатах, а Ликуд был разбит надвое.
        //
        // Порог — защита от неполной выборки: мандатов 120, и если запрос
        // вернул заметно меньше, это сломанная загрузка, а не роспуск
        // фракций. Стирать состав по такой выборке нельзя.
        var cleared = 0;
        if (ids.Count >= 100)
        {
            cleared = await db.Persons
                .Where(p => !ids.Contains(p.KnessetPersonId)
                            && (p.IsCurrent || p.FactionId != null || p.FactionName != null))
                .ExecuteUpdateAsync(s => s
                    .SetProperty(p => p.IsCurrent, false)
                    .SetProperty(p => p.FactionId, (int?)null)
                    .SetProperty(p => p.FactionName, (string?)null), ct);
        }
        else
        {
            logger.LogWarning(
                "Фракции: получено {Count} членств при 120 мандатах — состав не стираю",
                ids.Count);
        }

        var persons = await db.Persons
            .Where(p => ids.Contains(p.KnessetPersonId))
            .ToListAsync(ct);

        foreach (var person in persons)
        {
            var (id, name) = factionByPerson[person.KnessetPersonId];
            person.FactionId = id;
            person.FactionName = name;
            person.IsCurrent = true;
        }

        await db.SaveChangesAsync(ct);
        logger.LogInformation(
            "Фракции: действующих депутатов {Current}, снят признак с {Cleared}",
            persons.Count, cleared);
        return persons.Count;
    }

    private async Task<int> SyncPhotosAsync(DateTime? since, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);

        // 1. Site-коды: маппинг PersonID (OData) → SiteId (сайт Кнессета).
        var needSiteId = await db.Persons.AnyAsync(p => p.IsCurrent && p.KnessetSiteId == null, ct);
        if (needSiteId)
        {
            var siteCodes = await client.GetMkSiteCodesAsync(ct);
            var siteIdByKnsId = siteCodes
                .GroupBy(s => s.KnsID)
                .ToDictionary(g => g.Key, g => g.First().SiteId);

            var withoutSiteId = await db.Persons
                .Where(p => p.KnessetSiteId == null)
                .ToListAsync(ct);
            foreach (var person in withoutSiteId)
            {
                if (siteIdByKnsId.TryGetValue(person.KnessetPersonId, out var siteId))
                    person.KnessetSiteId = siteId;
            }
            await db.SaveChangesAsync(ct);
        }

        // 2. Фото действующих депутатов, у которых его ещё нет.
        var pending = await db.Persons
            .Where(p => p.IsCurrent && p.PhotoUrl == null && p.KnessetSiteId != null)
            .ToListAsync(ct);

        var updated = 0;
        foreach (var person in pending)
        {
            var photoUrl = await websiteClient.GetMkPhotoUrlAsync(person.KnessetSiteId!.Value, ct);
            if (photoUrl is not null)
            {
                person.PhotoUrl = photoUrl;
                updated++;
            }
            await Task.Delay(150, ct); // щадящий темп к API сайта
        }

        await db.SaveChangesAsync(ct);
        return updated;
    }

    private async Task<int> SyncBillsAsync(DateTime? since, CancellationToken ct)
    {
        var latestKnesset = await client.GetLatestKnessetNumAsync(ct);
        var minKnesset = latestKnesset - 1; // последние два созыва

        var bills = await client.GetBillsAsync(minKnesset, since, ct);
        if (bills.Count == 0) return 0;

        var total = 0;
        // Сохраняем порциями, чтобы не держать тысячи отслеживаемых сущностей в одном контексте.
        foreach (var chunk in bills.Chunk(500))
        {
            await using var db = await dbFactory.CreateDbContextAsync(ct);
            var ids = chunk.Select(b => b.BillID).ToList();
            var existing = await db.Bills
                .Where(b => ids.Contains(b.KnessetBillId))
                .ToDictionaryAsync(b => b.KnessetBillId, ct);

            foreach (var src in chunk)
            {
                if (!existing.TryGetValue(src.BillID, out var bill))
                {
                    bill = new Bill { KnessetBillId = src.BillID, FirstSeenAt = DateTime.UtcNow };
                    db.Bills.Add(bill);
                }

                // Анализ устаревает только при содержательных изменениях (название, статус,
                // описание) — технические обновления LastUpdatedDate не тратят генерации.
                var contentChanged = bill.Id != 0 &&
                    (bill.Name != CleanRequired(src.Name) ||
                     bill.StatusId != src.StatusID ||
                     bill.SummaryLaw != Clean(src.SummaryLaw));

                // Смена стадии — отдельное событие для тех, кто следит за этим законом.
                if (bill.Id != 0 && bill.StatusId != src.StatusID)
                    bill.StatusChangedAt = DateTime.UtcNow;

                bill.Name = CleanRequired(src.Name);
                bill.KnessetNum = src.KnessetNum ?? 0;
                bill.SubTypeDesc = Clean(src.SubTypeDesc);
                bill.CommitteeId = src.CommitteeID;
                bill.StatusId = src.StatusID;
                bill.StatusDesc = src.StatusID is int sid && _statusDescById.TryGetValue(sid, out var desc)
                    ? desc : null;
                bill.Number = src.Number;
                bill.PublicationDate = AsUtcNullable(src.PublicationDate);
                bill.SummaryLaw = Clean(src.SummaryLaw);
                bill.LastUpdatedDate = AsUtc(src.LastUpdatedDate);

                // Законопроект изменился — помечаем существующие анализы устаревшими.
                if (contentChanged)
                {
                    await db.BillAnalyses
                        .Where(a => a.BillId == bill.Id && !a.IsStale)
                        .ExecuteUpdateAsync(s => s.SetProperty(a => a.IsStale, true), ct);
                }
            }

            await db.SaveChangesAsync(ct);
            total += chunk.Length;
        }

        return total;
    }

    private async Task<int> SyncInitiatorsAsync(DateTime? since, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);

        var minBillId = await db.Bills.MinAsync(b => (int?)b.KnessetBillId, ct);
        if (minBillId is null) return 0;

        var initiators = await client.GetBillInitiatorsAsync(minBillId.Value, since, ct);
        if (initiators.Count == 0) return 0;

        // Отображение внешних ID на локальные PK.
        var billIdMap = await db.Bills.ToDictionaryAsync(b => b.KnessetBillId, b => b.Id, ct);
        var personIdMap = await db.Persons.ToDictionaryAsync(p => p.KnessetPersonId, p => p.Id, ct);

        var total = 0;
        foreach (var chunk in initiators.Chunk(1000))
        {
            await using var chunkDb = await dbFactory.CreateDbContextAsync(ct);
            var billIds = chunk
                .Where(i => billIdMap.ContainsKey(i.BillID))
                .Select(i => billIdMap[i.BillID])
                .Distinct().ToList();

            var existing = await chunkDb.BillInitiators
                .Where(bi => billIds.Contains(bi.BillId))
                .ToDictionaryAsync(bi => (bi.BillId, bi.PersonId), ct);

            foreach (var src in chunk)
            {
                if (!billIdMap.TryGetValue(src.BillID, out var billId) ||
                    !personIdMap.TryGetValue(src.PersonID, out var personId))
                    continue; // законопроект другого созыва или неизвестный человек

                if (!existing.TryGetValue((billId, personId), out var link))
                {
                    link = new BillInitiator { BillId = billId, PersonId = personId };
                    chunkDb.BillInitiators.Add(link);
                    existing[(billId, personId)] = link;
                }
                link.IsInitiator = src.IsInitiator ?? false;
                link.Ordinal = src.Ordinal;
            }

            await chunkDb.SaveChangesAsync(ct);
            total += chunk.Length;
        }

        return total;
    }

    /// <summary>
    /// История стадий: где и когда законопроект стоял в повестке. В KNS_Bill такого
    /// нет — там только текущий статус, без даты его наступления. Собирается из двух
    /// пар сущностей источника: пункты повесток (KNS_CmtSessionItem, KNS_PlmSessionItem)
    /// дают связь с законом и стадию, сами заседания — дату.
    ///
    /// Пункты берём инкрементально, заседания целиком: дата живёт в заседании,
    /// и перенос его на другой день не трогает LastUpdatedDate у пунктов. Иначе
    /// перенесённое заседание осталось бы у нас со старой датой навсегда.
    /// </summary>
    /// <summary>
    /// Разовый добор комиссии у ранее сохранённых законопроектов.
    ///
    /// Поле CommitteeId появилось позже самих строк, а обычная синхронизация
    /// законопроектов инкрементальна: она приносит только те, что менялись
    /// в Кнессете, и у остальных комиссия осталась бы пустой навсегда.
    ///
    /// Шаг заведён отдельным именем, поэтому его собственный водяной знак
    /// пуст ровно один раз — при первом прогоне после этой правки. Дальше
    /// шаг видит непустой since и сразу выходит.
    /// </summary>
    private async Task<int> BackfillBillCommitteesAsync(DateTime? since, CancellationToken ct) =>
        since is null ? await SyncBillsAsync(null, ct) : 0;

    /// <summary>
    /// Справочник комиссий. Нужен ради названия и адреса секретариата:
    /// без них бейдж окна влияния зовёт написать в комиссию, не говоря
    /// куда именно.
    /// </summary>
    private async Task<int> SyncCommitteesAsync(DateTime? since, CancellationToken ct)
    {
        var source = await client.GetCommitteesAsync(ct);
        if (source.Count == 0) return 0;

        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var existing = await db.Committees.ToDictionaryAsync(c => c.Id, ct);

        foreach (var src in source)
        {
            if (!existing.TryGetValue(src.CommitteeID, out var row))
            {
                row = new Committee { Id = src.CommitteeID };
                db.Committees.Add(row);
                existing[src.CommitteeID] = row;
            }

            row.Name = CleanRequired(src.Name);
            row.Email = Clean(src.Email);
            // 71 — «ועדה ראשית», основная комиссия; остальное подкомиссии
            // и совместные, законопроекты через них не ведут.
            row.IsMain = src.CommitteeTypeID == 71;
            row.IsCurrent = src.IsCurrent ?? false;
            row.LastUpdatedDate = AsUtc(src.LastUpdatedDate);
        }

        await db.SaveChangesAsync(ct);
        return source.Count;
    }

    /// <summary>
    /// Файлы законопроектов: сам текст с пояснительной запиской.
    ///
    /// Единственный источник содержания — KNS_Bill отдаёт заголовок и почти
    /// всегда пустой SummaryLaw. Один документ приходит по строке на формат,
    /// DOC и PDF с общим DocumentBillID, поэтому ключ строки — пара
    /// «документ + формат».
    /// </summary>
    private async Task<int> SyncBillDocumentsAsync(DateTime? since, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);

        var minBillId = await db.Bills.MinAsync(b => (int?)b.KnessetBillId, ct);
        if (minBillId is null) return 0;

        var documents = await client.GetBillDocumentsAsync(minBillId.Value, since, ct);
        if (documents.Count == 0) return 0;

        var billIdMap = await db.Bills.ToDictionaryAsync(b => b.KnessetBillId, b => b.Id, ct);
        var total = 0;

        foreach (var chunk in documents.Chunk(500))
        {
            await using var chunkDb = await dbFactory.CreateDbContextAsync(ct);

            var billIds = chunk
                .Where(d => billIdMap.ContainsKey(d.BillID))
                .Select(d => billIdMap[d.BillID])
                .Distinct().ToList();
            if (billIds.Count == 0) continue;

            var existing = await chunkDb.BillDocuments
                .Where(x => billIds.Contains(x.BillId))
                .ToDictionaryAsync(x => (x.BillId, x.KnessetDocumentId, x.Format), ct);

            foreach (var src in chunk)
            {
                // Законопроект чужого созыва — его самого мы не забирали.
                if (!billIdMap.TryGetValue(src.BillID, out var billId)) continue;

                var format = CleanRequired(src.ApplicationDesc);
                var url = Clean(src.FilePath);
                // Строка без ссылки бесполезна: показывать нечего.
                if (url is null || format.Length == 0) continue;

                var key = (billId, src.DocumentBillID, format);
                if (!existing.TryGetValue(key, out var row))
                {
                    row = new BillDocument
                    {
                        BillId = billId,
                        KnessetDocumentId = src.DocumentBillID,
                        Format = format
                    };
                    chunkDb.BillDocuments.Add(row);
                    existing[key] = row;
                }

                row.GroupTypeDesc = Clean(src.GroupTypeDesc);
                row.Url = url;
                row.LastUpdatedDate = AsUtc(src.LastUpdatedDate);
            }

            await chunkDb.SaveChangesAsync(ct);
            total += chunk.Length;
        }

        return total;
    }

    private async Task<int> SyncBillSessionsAsync(DateTime? since, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);

        var minBillId = await db.Bills.MinAsync(b => (int?)b.KnessetBillId, ct);
        if (minBillId is null) return 0;

        var latestKnesset = await client.GetLatestKnessetNumAsync(ct);
        var minKnesset = latestKnesset - 1;

        // Дата заседания по его идентификатору. Ключ включает вид: нумерация
        // комиссий и пленума в источнике независимая и пересекается.
        var dates = new Dictionary<(BillSessionKind, int), DateTime>();

        // AsUtc обязателен: OData отдаёт время без часового пояса, и Npgsql
        // отказывается писать такой DateTime в timestamptz. Остальные шаги
        // синхронизации пропускают источниковые даты через тот же помощник.
        foreach (var cs in await client.GetCommitteeSessionsAsync(minKnesset, since, ct))
            if (cs.StartDate is { } d)
                dates[(BillSessionKind.Committee, cs.CommitteeSessionID)] = AsUtc(d);

        foreach (var ps in await client.GetPlenumSessionsAsync(minKnesset, since, ct))
            if (ps.StartDate is { } d)
                dates[(BillSessionKind.Plenum, ps.PlenumSessionID)] = AsUtc(d);

        var items = new List<(int KnessetBillId, BillSessionKind Kind, int SessionId, int? StatusId)>();

        foreach (var i in await client.GetCommitteeSessionItemsAsync(minBillId.Value, since, ct))
            items.Add((i.ItemID, BillSessionKind.Committee, i.CommitteeSessionID, i.StatusID));

        foreach (var i in await client.GetPlenumSessionItemsAsync(minBillId.Value, since, ct))
            items.Add((i.ItemID, BillSessionKind.Plenum, i.PlenumSessionID, i.StatusID));

        // Справочник содержит только заседания, изменившиеся с прошлого прогона.
        // Пункты повестки при этом приходят своим инкрементом, и заседание,
        // на которое ссылается новый пункт, вполне могло не меняться годами —
        // его даты в ответе источника нет. Дату такого заседания мы уже видели
        // и сохранили денормализованно в BillSessions, оттуда и берём. Без этого
        // строки отсекались бы ниже по «дата не найдена» и не создавались вовсе.
        var filledFromDb = await FillKnownSessionDatesAsync(db, items, dates, ct);
        if (filledFromDb > 0)
            logger.LogInformation(
                "Заседания: дат добрано из своей базы — {Count}", filledFromDb);

        // Ни изменившихся заседаний, ни новых пунктов — делать нечего.
        if (items.Count == 0 && dates.Count == 0) return 0;

        var billIdMap = await db.Bills.ToDictionaryAsync(b => b.KnessetBillId, b => b.Id, ct);
        var total = 0;
        var touchedBills = new HashSet<int>();

        foreach (var chunk in items.Chunk(1000))
        {
            await using var chunkDb = await dbFactory.CreateDbContextAsync(ct);

            var billIds = chunk
                .Where(i => billIdMap.ContainsKey(i.KnessetBillId))
                .Select(i => billIdMap[i.KnessetBillId])
                .Distinct().ToList();
            if (billIds.Count == 0) continue;

            var existing = await chunkDb.BillSessions
                .Where(s => billIds.Contains(s.BillId))
                .ToDictionaryAsync(s => (s.BillId, s.Kind, s.KnessetSessionId), ct);

            foreach (var src in chunk)
            {
                if (!billIdMap.TryGetValue(src.KnessetBillId, out var billId)) continue;
                // Заседание чужого созыва — его дату мы не забирали.
                if (!dates.TryGetValue((src.Kind, src.SessionId), out var startDate)) continue;

                var key = (billId, src.Kind, src.SessionId);
                if (!existing.TryGetValue(key, out var row))
                {
                    row = new BillSession
                    {
                        BillId = billId,
                        Kind = src.Kind,
                        KnessetSessionId = src.SessionId
                    };
                    chunkDb.BillSessions.Add(row);
                    existing[key] = row;
                }
                row.StartDate = startDate;
                row.StatusId = src.StatusId;
                // Справочник статусов загружен первым шагом SyncAllAsync,
                // поэтому к этому моменту он уже заполнен.
                row.StatusDesc = src.StatusId is { } sid
                    ? _statusDescById.GetValueOrDefault(sid)
                    : null;
                touchedBills.Add(billId);
            }

            await chunkDb.SaveChangesAsync(ct);
            total += chunk.Length;
        }

        await RefreshSessionDatesAsync(dates, ct);
        await RefreshFirstSessionAsync(touchedBills, ct);

        return total;
    }

    /// <summary>
    /// Доливает в справочник даты заседаний, которых нет в ответе источника.
    ///
    /// Заседания теперь забираются инкрементом, поэтому в ответе только
    /// изменившиеся. Пункт повестки же может ссылаться на заседание, не
    /// менявшееся с прошлых прогонов, — его дата у нас уже есть, сохранена
    /// в BillSessions при первой встрече. Берём оттуда.
    ///
    /// Объём запроса ограничен потребностью, а не размером таблицы: на полном
    /// прогоне since равен null, справочник приходит целиком и добирать нечего;
    /// на инкрементальном пунктов немного по определению.
    /// </summary>
    private static async Task<int> FillKnownSessionDatesAsync(
        AppDbContext db,
        List<(int KnessetBillId, BillSessionKind Kind, int SessionId, int? StatusId)> items,
        Dictionary<(BillSessionKind, int), DateTime> dates,
        CancellationToken ct)
    {
        var filled = 0;

        foreach (var kind in new[] { BillSessionKind.Committee, BillSessionKind.Plenum })
        {
            var needed = items
                .Where(i => i.Kind == kind && !dates.ContainsKey((kind, i.SessionId)))
                .Select(i => i.SessionId)
                .Distinct()
                .ToList();
            if (needed.Count == 0) continue;

            var known = await db.BillSessions.AsNoTracking()
                .Where(x => x.Kind == kind && needed.Contains(x.KnessetSessionId))
                .Select(x => new { x.KnessetSessionId, x.StartDate })
                .Distinct()
                .ToListAsync(ct);

            foreach (var row in known)
                if (dates.TryAdd((kind, row.KnessetSessionId), row.StartDate))
                    filled++;
        }

        return filled;
    }

    /// <summary>
    /// Переносы заседаний. Пункты повестки при переносе не меняются, поэтому
    /// инкрементальная выборка их не приносит, и без этого прохода дата у нас
    /// осталась бы старой. Обновляем только там, где она разошлась.
    /// </summary>
    private async Task RefreshSessionDatesAsync(
        Dictionary<(BillSessionKind, int), DateTime> dates, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);

        var rows = await db.BillSessions.ToListAsync(ct);
        var changed = 0;

        foreach (var row in rows)
        {
            if (dates.TryGetValue((row.Kind, row.KnessetSessionId), out var actual)
                && row.StartDate != actual)
            {
                row.StartDate = actual;
                changed++;
            }

            // Заодно дозаполняем название стадии. Колонка появилась позже самих
            // строк, а инкрементальная выборка пунктов их уже не принесёт —
            // без этого прохода старые заседания остались бы без подписи.
            if (row.StatusDesc is null && row.StatusId is { } sid
                && _statusDescById.TryGetValue(sid, out var desc))
            {
                row.StatusDesc = desc;
                changed++;
            }
        }

        if (changed > 0)
        {
            await db.SaveChangesAsync(ct);
            logger.LogInformation("Заседания: перенесено {Count}", changed);
        }
    }

    /// <summary>
    /// Дата первого появления в повестке — денормализуется в Bill, по ней
    /// сортируется список законопроектов. Пересчитываем только затронутые.
    /// </summary>
    private async Task RefreshFirstSessionAsync(HashSet<int> billIds, CancellationToken ct)
    {
        if (billIds.Count == 0) return;

        await using var db = await dbFactory.CreateDbContextAsync(ct);

        foreach (var chunk in billIds.Chunk(500))
        {
            var ids = chunk.ToList();
            var firsts = (await db.BillSessions
                    .Where(s => ids.Contains(s.BillId))
                    .GroupBy(s => s.BillId)
                    .Select(g => new { BillId = g.Key, First = g.Min(x => x.StartDate) })
                    .ToListAsync(ct))
                .ToDictionary(x => x.BillId, x => x.First);

            // Правим загруженные сущности и сохраняем пачкой. Отдельный
            // ExecuteUpdateAsync на каждый закон выглядит аккуратнее, но это
            // один сетевой обход на строку: на нескольких тысячах законов
            // и базе в другой стране шаг растягивается на минуты.
            var bills = await db.Bills.Where(b => ids.Contains(b.Id)).ToListAsync(ct);
            foreach (var bill in bills)
                if (firsts.TryGetValue(bill.Id, out var first))
                    bill.FirstSessionAt = first;

            await db.SaveChangesAsync(ct);
        }
    }

    /// <summary>
    /// Свод действующих законов. Это не законопроекты: законопроект — предложение,
    /// а здесь то, что уже принято и действует.
    /// </summary>
    private async Task<int> SyncIsraelLawsAsync(DateTime? since, CancellationToken ct)
    {
        var laws = await client.GetIsraelLawsAsync(since, ct);
        if (laws.Count == 0) return 0;

        var total = 0;
        foreach (var chunk in laws.Chunk(500))
        {
            await using var db = await dbFactory.CreateDbContextAsync(ct);
            var ids = chunk.Select(l => l.IsraelLawID).ToList();
            var existing = await db.IsraelLaws
                .Where(l => ids.Contains(l.KnessetIsraelLawId))
                .ToDictionaryAsync(l => l.KnessetIsraelLawId, ct);

            foreach (var src in chunk)
            {
                if (!existing.TryGetValue(src.IsraelLawID, out var law))
                {
                    law = new IsraelLaw { KnessetIsraelLawId = src.IsraelLawID };
                    db.IsraelLaws.Add(law);
                }

                law.Name = CleanRequired(src.Name);
                law.KnessetNum = src.KnessetNum;
                law.IsBasicLaw = src.IsBasicLaw ?? false;
                law.IsBudgetLaw = src.IsBudgetLaw ?? false;
                law.ValidityDesc = Clean(src.LawValidityDesc);
                law.PublicationDate = AsUtcNullable(src.PublicationDate);
                law.ValidityStartDate = AsUtcNullable(src.ValidityStartDate);
                law.ValidityFinishDate = AsUtcNullable(src.ValidityFinishDate);
                law.LastUpdatedDate = AsUtc(src.LastUpdatedDate);
            }

            await db.SaveChangesAsync(ct);
            total += chunk.Length;
        }

        return total;
    }

    /// <summary>
    /// Принятые акты. Их больше шестидесяти тысяч, поэтому шаг строго инкрементальный:
    /// первый прогон длинный, дальше подтягиваются только изменившиеся.
    /// </summary>
    private async Task<int> SyncLawActsAsync(DateTime? since, CancellationToken ct)
    {
        var total = 0;

        // Постранично, с сохранением каждой страницы: шесть сотен запросов могут
        // не уложиться в один сеанс, и прерванная загрузка не должна пропадать.
        await foreach (var chunk in client.StreamActsAsync(since, ct))
        {
            await using var db = await dbFactory.CreateDbContextAsync(ct);
            var ids = chunk.Select(a => a.LawID).ToList();
            var existing = await db.LawActs
                .Where(a => ids.Contains(a.KnessetLawId))
                .ToDictionaryAsync(a => a.KnessetLawId, ct);

            foreach (var src in chunk)
            {
                if (!existing.TryGetValue(src.LawID, out var act))
                {
                    act = new LawAct { KnessetLawId = src.LawID };
                    db.LawActs.Add(act);
                    existing[src.LawID] = act;
                }

                act.Name = CleanRequired(src.Name);
                act.PublicationDate = AsUtcNullable(src.PublicationDate);
                act.LastUpdatedDate = AsUtc(src.LastUpdatedDate);
            }

            await db.SaveChangesAsync(ct);
            total += chunk.Count;
        }

        return total;
    }

    /// <summary>
    /// Связки «акт → закон» с признаком прямой или косвенной поправки.
    /// Косвенная поправка — когда акт про одну тему меняет закон про другую;
    /// Кнессет помечает такие сам, и это самое ценное в этих данных.
    /// </summary>
    private async Task<int> SyncLawAmendmentsAsync(DateTime? since, CancellationToken ct)
    {
        var bindings = await client.GetLawBindingsAsync(since, ct);
        if (bindings.Count == 0) return 0;

        await using var mapDb = await dbFactory.CreateDbContextAsync(ct);
        var lawIdMap = await mapDb.IsraelLaws.ToDictionaryAsync(l => l.KnessetIsraelLawId, l => l.Id, ct);
        // Названия берём из своей таблицы, а не из API: она уже наполнена шагом выше.
        var acts = await mapDb.LawActs.AsNoTracking()
            .ToDictionaryAsync(a => a.KnessetLawId, a => new { a.Name, a.PublicationDate }, ct);

        var total = 0;
        foreach (var chunk in bindings.Chunk(1000))
        {
            await using var db = await dbFactory.CreateDbContextAsync(ct);
            var bindingIds = chunk.Select(b => b.LawBindingID).ToList();
            var existing = await db.LawAmendments
                .Where(a => bindingIds.Contains(a.KnessetBindingId))
                .ToDictionaryAsync(a => a.KnessetBindingId, ct);

            foreach (var src in chunk)
            {
                // Закон другого созыва или ещё не загруженный — пропускаем.
                if (!lawIdMap.TryGetValue(src.IsraelLawID, out var israelLawId)) continue;

                if (!existing.TryGetValue(src.LawBindingID, out var amendment))
                {
                    amendment = new LawAmendment { KnessetBindingId = src.LawBindingID };
                    db.LawAmendments.Add(amendment);
                    existing[src.LawBindingID] = amendment;
                }

                amendment.IsraelLawId = israelLawId;
                amendment.KnessetLawId = src.LawID;
                amendment.BindingTypeDesc = Clean(src.BindingTypeDesc);
                amendment.AmendmentTypeDesc = Clean(src.AmendmentTypeDesc);
                // Разметка Кнессета: עקיף — косвенная, החוק המקורי — сам факт создания закона.
                amendment.IsIndirect = src.AmendmentTypeDesc?.Contains("עקיף") ?? false;
                amendment.IsOriginal = src.BindingTypeDesc?.Contains("המקורי") ?? false;
                amendment.LastUpdatedDate = AsUtc(src.LastUpdatedDate);

                if (acts.TryGetValue(src.LawID, out var act))
                {
                    amendment.ActName = act.Name;
                    amendment.ActPublicationDate = act.PublicationDate;
                }
            }

            await db.SaveChangesAsync(ct);
            total += chunk.Length;
        }

        return total;
    }

    /// <summary>
    /// Разбор действующих законов и переводы разбора.
    ///
    /// Мастер делается на английском, остальные языки получаются переводом —
    /// тем же конвейером, что у законопроектов, и по той же причине:
    /// содержание на всех языках должно быть одним. Для иврита переводчику
    /// подаётся сам текст закона, чтобы термины восстанавливались
    /// по источнику, а не переводились обратно с английского.
    ///
    /// Каждый шаг берётся под захват. Дев и прод сидят на одной базе,
    /// и без захвата оба разобрали бы — и оплатили — один закон дважды;
    /// на переводах законопроектов это уже случалось.
    ///
    /// Порция за прогон ограничена: разбор на крупном тексте идёт минуты,
    /// и растягивать на них весь конвейер синхронизации незачем.
    /// </summary>
    private async Task<int> SyncLawAnalysesAsync(DateTime? since, CancellationToken ct)
    {
        if (lawAnalyzer is null) return 0;

        // Пятнадцать за прогон — чтобы девятнадцать основных законов
        // закрылись за один проход, а не за несколько суток по три.
        // Прогон при этом растягивается почти на час; когда очередь опустеет,
        // это перестанет иметь значение — шаг будет находить одиночные
        // законы с изменившимся текстом.
        const int PerRun = 15;
        string[] languages = ["ru", "he", "ar"];

        await using var readDb = await dbFactory.CreateDbContextAsync(ct);

        // Разбор нужен там, где его нет или где текст с тех пор изменился:
        // ревизия источника — единственный честный признак устаревания.
        var due = await readDb.IsraelLaws.AsNoTracking()
            .Where(l => l.IsBasicLaw && l.FullText != null)
            .Where(l => !l.Analyses.Any(x => x.LanguageCode == "en"
                                             && x.SourceRevision == l.FullText!.Revision))
            .OrderBy(l => l.Id)
            .Take(PerRun)
            .Select(l => l.Id)
            .ToListAsync(ct);

        logger.LogInformation("Разборы законов: в очереди {Due}", due.Count);

        var analysed = 0;
        foreach (var lawId in due)
        {
            if (await AnalyseLawAsync(lawId, ct)) analysed++;
        }

        // Переводы отдельным проходом: мастер мог появиться на прошлом прогоне,
        // и ждать нового разбора ради перевода незачем.
        var pending = await readDb.IsraelLawAnalyses.AsNoTracking()
            .Where(x => x.LanguageCode == "en")
            .Where(x => x.IsraelLaw.Analyses.Count(y => y.SourceRevision == x.SourceRevision)
                        < languages.Length + 1)
            .OrderBy(x => x.IsraelLawId)
            .Take(PerRun)
            .Select(x => x.IsraelLawId)
            .ToListAsync(ct);

        var translated = 0;
        foreach (var lawId in pending)
        {
            translated += await TranslateLawAnalysisAsync(lawId, languages, ct);
        }

        logger.LogInformation(
            "Разборы законов: разобрано {Analysed}, переводов {Translated}", analysed, translated);

        return analysed + translated;
    }

    private async Task<bool> AnalyseLawAsync(int lawId, CancellationToken ct)
    {
        if (!await claims.TryClaimAsync(AnalysisJob.SubjectLaw, lawId, AnalysisJob.MasterStep, ct))
            return false;

        try
        {
            await using var db = await dbFactory.CreateDbContextAsync(ct);
            var law = await db.IsraelLaws.AsNoTracking()
                .Include(l => l.FullText)
                .FirstAsync(l => l.Id == lawId, ct);

            var dates = await db.LawAmendments.AsNoTracking()
                .Where(x => x.IsraelLawId == lawId && !x.IsOriginal)
                .Select(x => x.ActPublicationDate)
                .ToListAsync(ct);
            var regulations = await db.LawRegulations.AsNoTracking()
                .CountAsync(x => x.IsraelLawId == lawId, ct);

            var result = await lawAnalyzer!.AnalyzeAsync(new LawAnalysisRequest
            {
                IsraelLawId = law.Id,
                NameHebrew = law.Name,
                IsBasicLaw = law.IsBasicLaw,
                ValidityDesc = law.ValidityDesc,
                PublicationDate = law.PublicationDate,
                AmendmentCount = dates.Count,
                LastAmendedAt = dates.Where(d => d != null).Max(),
                RegulationCount = regulations,
                FullText = law.FullText?.Text,
                TextSource = law.FullText?.SourceUrl,
                TextRevisionAt = law.FullText?.RevisionAt,
                LanguageCode = "en",
            }, ct);

            await SaveLawAnalysisAsync(lawId, "en", result, lawAnalyzer.ModelVersion,
                law.FullText?.Revision ?? 0, ct);

            await claims.ReleaseAsync(AnalysisJob.SubjectLaw, lawId, AnalysisJob.MasterStep, null, ct);
            return true;
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            logger.LogError(ex, "Разбор закона {LawId} не вышел", lawId);
            await claims.ReleaseAsync(
                AnalysisJob.SubjectLaw, lawId, AnalysisJob.MasterStep, ex.Message, ct);
            return false;
        }
    }

    private async Task<int> TranslateLawAnalysisAsync(
        int lawId, string[] languages, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var law = await db.IsraelLaws.AsNoTracking()
            .Include(l => l.FullText)
            .Include(l => l.Analyses)
            .FirstAsync(l => l.Id == lawId, ct);

        var master = law.Analyses.FirstOrDefault(x => x.LanguageCode == "en");
        if (master is null) return 0;

        var source = JsonSerializer.Deserialize<BillAnalysisResult>(master.AnalysisJson);
        if (source is null) return 0;

        var done = 0;
        foreach (var language in languages)
        {
            var existing = law.Analyses.FirstOrDefault(x => x.LanguageCode == language);
            if (existing is not null && existing.SourceRevision == master.SourceRevision) continue;

            if (!await claims.TryClaimAsync(AnalysisJob.SubjectLaw, lawId, language, ct)) continue;

            try
            {
                // Иврит — язык самого закона: подаём текст, чтобы термины
                // вернулись из источника, а не переводились обратно.
                var document = language == "he" ? law.FullText?.Text : null;
                var translation = await translator.TranslateAsync(source, language, document, ct);

                await SaveLawAnalysisAsync(lawId, language, translation.Analysis,
                    translation.ModelVersion, master.SourceRevision, ct);

                await claims.ReleaseAsync(AnalysisJob.SubjectLaw, lawId, language, null, ct);
                done++;
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
            {
                logger.LogError(ex, "Перевод разбора закона {LawId} на {Lang} не вышел", lawId, language);
                await claims.ReleaseAsync(AnalysisJob.SubjectLaw, lawId, language, ex.Message, ct);
            }
        }

        return done;
    }

    private async Task SaveLawAnalysisAsync(
        int lawId, string language, BillAnalysisResult analysis, string model,
        long revision, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var row = await db.IsraelLawAnalyses
            .FirstOrDefaultAsync(x => x.IsraelLawId == lawId && x.LanguageCode == language, ct);

        if (row is null)
        {
            row = new IsraelLawAnalysis { IsraelLawId = lawId, LanguageCode = language };
            db.IsraelLawAnalyses.Add(row);
        }

        row.AnalysisJson = JsonSerializer.Serialize(analysis);
        row.ModelVersion = model;
        row.SourceRevision = revision;
        row.GeneratedAt = DateTime.UtcNow;
        row.IsStale = false;
        await db.SaveChangesAsync(ct);
    }

    /// <summary>
    /// Сводные тексты законов из «ספר החוקים הפתוח».
    ///
    /// Пока — только основные законы, и это решение про место, а не про
    /// принцип: пятнадцать основных с текстом весят 248 КБ, а весь свод
    /// из двух тысяч законов — сотни мегабайт при 500 МБ бесплатного
    /// тарифа, из которых 195 уже заняты. Чтобы расширить охват, достаточно
    /// снять условие IsBasicLaw — и сначала решить, где хранить.
    ///
    /// Перечитываем, когда в источнике новая ревизия или когда сменились
    /// наши правила чистки разметки.
    /// </summary>
    private async Task<int> SyncLawTextsAsync(DateTime? since, CancellationToken ct)
    {
        const int PerRun = 30;

        await using var readDb = await dbFactory.CreateDbContextAsync(ct);
        var due = await readDb.IsraelLaws.AsNoTracking()
            .Where(l => l.IsBasicLaw && l.OpenBookUrl != null)
            .Where(l => l.FullText == null || l.FullText.CleanerVersion != WikitextCleaner.Version)
            .OrderBy(l => l.Id)
            .Take(PerRun)
            .Select(l => new { l.Id, l.OpenBookUrl })
            .ToListAsync(ct);

        // Пишем в лог и пустую очередь: иначе «шаг не дошёл» и «шагу нечего
        // делать» выглядят одинаково — молчанием, и час уходит на догадки.
        logger.LogInformation("Тексты законов: в очереди {Due}", due.Count);
        if (due.Count == 0) return 0;

        var saved = 0;
        var chars = 0;

        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var ids = due.Select(x => x.Id).ToList();
        var existing = await db.IsraelLawTexts
            .Where(t => ids.Contains(t.IsraelLawId))
            .ToDictionaryAsync(t => t.IsraelLawId, ct);

        foreach (var law in due)
        {
            var title = WikisourceClient.TitleFromUrl(law.OpenBookUrl);
            if (title is null) continue;

            var revision = await wikisource.GetTextAsync(title, ct);
            await Task.Delay(TimeSpan.FromSeconds(2), ct);
            if (revision is null) continue;

            if (!existing.TryGetValue(law.Id, out var row))
            {
                row = new IsraelLawText { IsraelLawId = law.Id };
                db.IsraelLawTexts.Add(row);
                existing[law.Id] = row;
            }

            row.SourceUrl = law.OpenBookUrl!;
            row.SourceTitle = title;
            row.Revision = revision.Revision;
            row.RevisionAt = AsUtc(revision.RevisionAt);
            row.Text = revision.Text;
            row.CharCount = revision.Text.Length;
            row.CleanerVersion = WikitextCleaner.Version;
            row.FetchedAt = DateTime.UtcNow;

            saved++;
            chars += revision.Text.Length;
        }

        await db.SaveChangesAsync(ct);

        logger.LogInformation(
            "Тексты законов: сохранено {Saved} из {Due}, знаков {Chars}", saved, due.Count, chars);

        return saved;
    }

    /// <summary>
    /// Ссылки со страницы закона на сайте Кнессета: сводный текст
    /// («ספר החוקים הפתוח») и объяснение простыми словами («כל זכות»).
    ///
    /// Единственное место, где мы берём текст закона, — и берём его ссылкой,
    /// а не текстом: в открытых данных Кнессета сводного текста нет вовсе,
    /// а собирать его самим из оригинала и всех поправок значит делать
    /// правовую консолидацию, то есть другой продукт.
    ///
    /// Тем же запросом приходит история изменений закона — по строке
    /// на изменение, с публикацией и ссылкой на PDF. Изменения бывают двух
    /// родов, и они расходятся по разным таблицам: акты Кнессета дополняют
    /// наши LawAmendments, приказы министров ложатся в LawRegulations. Этого нет
    /// в открытых данных вовсе, а старых актов нет и в KNS_Law: именно
    /// поэтому у части поправок у нас не было даже названия. Строки
    /// привязываются к нашим LawAmendments по идентификатору акта —
    /// сверено на «חוק-יסוד: הממשלה [התשכ"ח]», где двенадцать актов
    /// из OData совпали со всеми двенадцатью с сайта.
    ///
    /// Это апи сайта, а не выгрузка: частые запросы обрываются соединением.
    /// Поэтому порция за прогон и пауза между запросами, а порядок —
    /// сначала законы, которые правят живые законопроекты: их страницы
    /// читатель откроет первыми.
    /// </summary>
    private async Task<int> SyncLawSiteLinksAsync(DateTime? since, CancellationToken ct)
    {
        const int PerRun = 150;

        // Что мы забираем со страницы. Версию меняем, когда начинаем брать
        // больше: тогда законы переспрашиваются сами, и не нужен разовый
        // сброс отметки времени в миграции.
        //  site-v1 — ссылки на текст и министерства;
        //  site-v2 — плюс история изменений: поправки и подзаконные акты.
        const string SiteDataVersion = "site-v2";
        var stale = DateTime.UtcNow.AddDays(-90);

        await using var readDb = await dbFactory.CreateDbContextAsync(ct);
        var due = await readDb.IsraelLaws.AsNoTracking()
            .Where(l => l.SiteFetchedAt == null
                        || l.SiteFetchedAt < stale
                        || l.SiteDataVersion != SiteDataVersion)
            // Основные законы вперёд: их девятнадцать, они конституционное
            // ядро и самые читаемые страницы раздела. Следом — законы,
            // на которые ссылаются живые законопроекты.
            .OrderByDescending(l => l.IsBasicLaw)
            .ThenByDescending(l => readDb.Bills.Any(b => b.IsraelLawId == l.Id))
            .ThenByDescending(l => l.ValidityStartDate)
            .Take(PerRun)
            .Select(l => new { l.Id, l.KnessetIsraelLawId })
            .ToListAsync(ct);

        if (due.Count == 0) return 0;

        var found = 0;
        var docs = 0;
        var names = 0;
        var secondary = 0;
        var unmatched = 0;

        foreach (var chunk in due.Chunk(25))
        {
            await using var db = await dbFactory.CreateDbContextAsync(ct);
            var ids = chunk.Select(x => x.Id).ToList();
            var laws = await db.IsraelLaws.Where(l => ids.Contains(l.Id)).ToListAsync(ct);
            var amendments = await db.LawAmendments
                .Where(a => ids.Contains(a.IsraelLawId))
                .ToListAsync(ct);
            var regulations = await db.LawRegulations
                .Where(r => ids.Contains(r.IsraelLawId))
                .ToListAsync(ct);

            foreach (var row in chunk)
            {
                var info = await websiteClient.GetLawInfoAsync(row.KnessetIsraelLawId, ct);
                var law = laws.First(l => l.Id == row.Id);

                // Отметку ставим и на пустой ответ: иначе каждый прогон
                // спрашивал бы сайт об одних и тех же законах.
                law.SiteFetchedAt = DateTime.UtcNow;
                law.SiteDataVersion = SiteDataVersion;
                if (info is not null)
                {
                    law.OpenBookUrl = info.OpenBookUrl;
                    law.KolZchutUrl = info.KolZchutUrl;
                    law.Ministries = info.Ministries;
                    if (info.OpenBookUrl is not null) found++;

                    var mine = amendments.Where(a => a.IsraelLawId == law.Id).ToList();
                    var mineRegulations = regulations.Where(r => r.IsraelLawId == law.Id).ToList();

                    foreach (var correction in info.Corrections)
                    {
                        // Подзаконный акт — в свою таблицу. Смешивать нельзя:
                        // на счётчике поправок держится весь раздел про
                        // косвенные изменения, и приказы министров его
                        // раздули бы втрое.
                        if (correction.IsSecondary)
                        {
                            var regulation = mineRegulations
                                .FirstOrDefault(r => r.KnessetActId == correction.ActId);

                            if (regulation is null)
                            {
                                regulation = new LawRegulation
                                {
                                    IsraelLawId = law.Id,
                                    KnessetActId = correction.ActId,
                                };
                                db.LawRegulations.Add(regulation);
                                mineRegulations.Add(regulation);
                            }

                            regulation.Name = correction.Name ?? "";
                            regulation.IsIndirect = correction.IsIndirect;
                            regulation.PublicationSeries = correction.PublicationSeries;
                            regulation.MagazineNumber = correction.MagazineNumber;
                            regulation.PageNumber = correction.PageNumber;
                            regulation.PublicationDate = AsUtcNullable(correction.PublicationDate);
                            regulation.DocumentUrl = correction.DocumentUrl;
                            regulation.FetchedAt = DateTime.UtcNow;
                            secondary++;
                            continue;
                        }

                        var amendment = mine.FirstOrDefault(a => a.KnessetLawId == correction.ActId);
                        if (amendment is null)
                        {
                            // Своей строки под неё нет: LawBinding о таком акте
                            // не знает. Не выдумываем связку, а считаем — по этому
                            // числу и будет видно, нужна ли вставка.
                            unmatched++;
                            continue;
                        }

                        amendment.PublicationSeries = correction.PublicationSeries;
                        amendment.MagazineNumber = correction.MagazineNumber;
                        amendment.PageNumber = correction.PageNumber;
                        amendment.DocumentUrl = correction.DocumentUrl;
                        if (correction.DocumentUrl is not null) docs++;

                        // Название и дата акта из OData есть только у новых
                        // актов: старых нет в KNS_Law вовсе. Своё не
                        // перезаписываем — дополняем пробел.
                        if (string.IsNullOrWhiteSpace(amendment.ActName)
                            && correction.Name is { Length: > 0 } name)
                        {
                            amendment.ActName = name;
                            names++;
                        }

                        amendment.ActPublicationDate ??= AsUtcNullable(correction.PublicationDate);
                    }
                }

                await Task.Delay(TimeSpan.FromMilliseconds(700), ct);
            }

            await db.SaveChangesAsync(ct);
        }

        logger.LogInformation(
            "Страницы законов: спрошено {Asked}, текст у {Found}, документов поправок {Docs}, "
            + "названий восполнено {Names}, подзаконных актов {Secondary}, "
            + "не привязалось {Unmatched}",
            due.Count, found, docs, names, secondary, unmatched);

        return due.Count;
    }

    /// <summary>
    /// Связывает законопроекты с законами, которые они правят.
    ///
    /// Это единственный шаг, который ничего не тянет из API: связи там нет
    /// (см. <see cref="BillLawMatcher"/>), и она выводится из названия.
    /// Поэтому и `since` здесь работает иначе: пересчитываем законопроекты,
    /// изменившиеся с прошлого прогона, **и** все несопоставленные. Второе
    /// делает шаг самоисправляющимся: если закон появится в базе позже
    /// законопроекта, связь найдётся на следующем прогоне.
    /// </summary>
    private async Task<int> LinkBillsToLawsAsync(DateTime? since, CancellationToken ct)
    {
        await using var readDb = await dbFactory.CreateDbContextAsync(ct);

        var laws = await readDb.IsraelLaws.AsNoTracking()
            .Select(l => new { l.Id, l.Name, l.ValidityDesc, l.ValidityStartDate })
            .ToListAsync(ct);

        // Одинаковое ядро названия у нескольких законов бывает: старая
        // и новая редакция живут отдельными записями. Выбираем действующую,
        // при равенстве — более позднюю.
        var byKey = laws
            .GroupBy(l => BillLawMatcher.LawKey(l.Name))
            .ToDictionary(
                g => g.Key,
                g => g.OrderByDescending(l => l.ValidityDesc != null && l.ValidityDesc.Contains("תקף"))
                      .ThenByDescending(l => l.ValidityStartDate ?? DateTime.MinValue)
                      .First().Id);

        var bills = await readDb.Bills.AsNoTracking()
            .Where(b => b.LawMatch == null
                        || b.LawMatch != BillLawMatcher.Version
                        || since == null
                        || b.LastUpdatedDate > since)
            .Select(b => new { b.Id, b.Name, b.IsraelLawId, b.LawMatch })
            .ToListAsync(ct);

        var changed = new List<(int BillId, int? LawId)>();
        foreach (var bill in bills)
        {
            int? lawId = null;
            foreach (var key in BillLawMatcher.BillKeys(bill.Name))
            {
                if (byKey.TryGetValue(key, out var id)) { lawId = id; break; }
            }

            // Метку версии ставим и промахам: иначе каждый прогон разбирал бы
            // одни и те же названия заново.
            if (lawId != bill.IsraelLawId || bill.LawMatch != BillLawMatcher.Version)
                changed.Add((bill.Id, lawId));
        }

        var total = 0;
        foreach (var chunk in changed.Chunk(500))
        {
            await using var db = await dbFactory.CreateDbContextAsync(ct);
            foreach (var (billId, lawId) in chunk)
            {
                var bill = new Bill { Id = billId };
                db.Bills.Attach(bill);
                bill.IsraelLawId = lawId;
                bill.LawMatch = BillLawMatcher.Version;
                db.Entry(bill).Property(x => x.IsraelLawId).IsModified = true;
                db.Entry(bill).Property(x => x.LawMatch).IsModified = true;
            }

            await db.SaveChangesAsync(ct);
            total += chunk.Length;
        }

        var matched = changed.Count(c => c.LawId is not null);
        logger.LogInformation(
            "Связь законопроект-закон: пересчитано {Total}, найден закон у {Matched}",
            total, matched);

        return total;
    }

    /// <summary>
    /// Темы законов по рубрикатору Кнессета.
    ///
    /// Своей классификации мы не строим: у Кнессета 51 тема, и за каждой
    /// стоит источник, а не наша догадка. Тем на закон обычно две-три.
    /// </summary>
    private async Task<int> SyncLawTopicsAsync(DateTime? since, CancellationToken ct)
    {
        var rows = await client.GetLawClassificationsAsync(since, ct);
        if (rows.Count == 0) return 0;

        await using var mapDb = await dbFactory.CreateDbContextAsync(ct);
        var lawIdMap = await mapDb.IsraelLaws
            .ToDictionaryAsync(l => l.KnessetIsraelLawId, l => l.Id, ct);

        var total = 0;
        foreach (var chunk in rows.Chunk(1000))
        {
            await using var db = await dbFactory.CreateDbContextAsync(ct);
            var ids = chunk.Select(r => r.LawClassificiationID).ToList();
            var existing = await db.LawTopics
                .Where(t => ids.Contains(t.KnessetClassificationId))
                .ToDictionaryAsync(t => t.KnessetClassificationId, ct);

            foreach (var src in chunk)
            {
                // Закона ещё нет в базе — тема без закона бессмысленна.
                if (!lawIdMap.TryGetValue(src.IsraelLawID, out var israelLawId)) continue;

                if (!existing.TryGetValue(src.LawClassificiationID, out var topic))
                {
                    topic = new LawTopic { KnessetClassificationId = src.LawClassificiationID };
                    db.LawTopics.Add(topic);
                    existing[src.LawClassificiationID] = topic;
                }

                topic.IsraelLawId = israelLawId;
                topic.TopicId = src.ClassificiationID;
                topic.NameHe = Clean(src.ClassificiationDesc) ?? "";
                topic.LastUpdatedDate = AsUtc(src.LastUpdatedDate);
            }

            await db.SaveChangesAsync(ct);
            total += chunk.Length;
        }

        return total;
    }

    /// <summary>
    /// Строка из Кнессета: снимаем обрамляющие пробелы, пустое считаем отсутствующим.
    ///
    /// В выгрузке часть значений приходит с висячим пробелом — «הליכוד » вместо
    /// «הליכוד». На экране это незаметно, но фильтр законопроектов по фракции
    /// сравнивает названия через ==, и любое расхождение в пробеле молча
    /// превращает отбор в пустой результат. Чистим на входе, а не в запросах:
    /// btrim в условии отменяет индекс, и о нём легко забыть в новом запросе.
    /// </summary>
    private static string? Clean(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

    /// <summary>То же для полей, которые в модели не допускают null.</summary>
    private static string CleanRequired(string? s) => Clean(s) ?? "";

    private static DateTime AsUtc(DateTime dt) => DateTime.SpecifyKind(dt, DateTimeKind.Utc);
    private static DateTime? AsUtcNullable(DateTime? dt) => dt is null ? null : AsUtc(dt.Value);
}
