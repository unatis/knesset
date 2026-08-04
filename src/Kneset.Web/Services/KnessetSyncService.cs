using Kneset.Core.Entities;
using Kneset.Infrastructure.Data;
using Kneset.Infrastructure.Knesset;
using Microsoft.EntityFrameworkCore;

namespace Kneset.Web.Services;

/// <summary>
/// Фоновая синхронизация с API Кнессета: статусы → депутаты → фракции → законопроекты → инициаторы.
/// Запускается при старте и далее каждые 6 часов. Инкрементальная: фильтрует по LastUpdatedDate
/// с момента последней успешной синхронизации (SyncLogs).
/// </summary>
public class KnessetSyncService(
    IDbContextFactory<AppDbContext> dbFactory,
    KnessetODataClient client,
    KnessetWebsiteClient websiteClient,
    ILogger<KnessetSyncService> logger) : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromHours(6);
    private Dictionary<int, string> _statusDescById = [];

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
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

            await Task.Delay(Interval, stoppingToken);
        }
    }

    private async Task SyncAllAsync(CancellationToken ct)
    {
        await SyncStatusesAsync(ct);
        await RunStepAsync("Persons", SyncPersonsAsync, ct);
        await RunStepAsync("Factions", SyncFactionsAsync, ct);
        await RunStepAsync("Photos", SyncPhotosAsync, ct);
        await RunStepAsync("Bills", SyncBillsAsync, ct);
        await RunStepAsync("BillInitiators", SyncInitiatorsAsync, ct);
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
            person.FirstName = src.FirstName ?? "";
            person.LastName = src.LastName ?? "";
            person.GenderDesc = src.GenderDesc;
            person.Email = src.Email;
            person.IsCurrent = src.IsCurrent ?? false;
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
            .ToDictionary(g => g.Key, g => g.First().FactionName);

        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var ids = factionByPerson.Keys.ToList();
        var persons = await db.Persons
            .Where(p => ids.Contains(p.KnessetPersonId))
            .ToListAsync(ct);

        foreach (var person in persons)
            person.FactionName = factionByPerson[person.KnessetPersonId];

        await db.SaveChangesAsync(ct);
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
                    bill = new Bill { KnessetBillId = src.BillID };
                    db.Bills.Add(bill);
                }

                // Анализ устаревает только при содержательных изменениях (название, статус,
                // описание) — технические обновления LastUpdatedDate не тратят генерации.
                var contentChanged = bill.Id != 0 &&
                    (bill.Name != (src.Name ?? "") ||
                     bill.StatusId != src.StatusID ||
                     bill.SummaryLaw != src.SummaryLaw);

                bill.Name = src.Name ?? "";
                bill.KnessetNum = src.KnessetNum ?? 0;
                bill.SubTypeDesc = src.SubTypeDesc;
                bill.StatusId = src.StatusID;
                bill.StatusDesc = src.StatusID is int sid && _statusDescById.TryGetValue(sid, out var desc)
                    ? desc : null;
                bill.Number = src.Number;
                bill.PublicationDate = AsUtcNullable(src.PublicationDate);
                bill.SummaryLaw = src.SummaryLaw;
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

    private static DateTime AsUtc(DateTime dt) => DateTime.SpecifyKind(dt, DateTimeKind.Utc);
    private static DateTime? AsUtcNullable(DateTime? dt) => dt is null ? null : AsUtc(dt.Value);
}
