using Kneset.Core.Entities;
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
