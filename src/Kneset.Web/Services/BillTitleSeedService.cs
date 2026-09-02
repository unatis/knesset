using System.Text.Json;
using System.Text.Json.Serialization;
using Kneset.Core.Entities;
using Kneset.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Kneset.Web.Services;

/// <summary>
/// Загрузчик переводов названий законопроектов из Seed/bill-titles.json.
///
/// Названия приходят из Кнессета только на иврите, и человек, читающий сайт
/// по-русски, видит в списке строку, которую не может прочесть. Настоящего
/// переводчика в проекте пока нет (Ai:Provider=Stub), поэтому переводы
/// подготовлены заранее и лежат файлом рядом с кодом — тем же способом,
/// каким загружаются редакционные контекстные анализы.
///
/// Upsert по (законопроект, язык). Перевод обновляется, если изменилось
/// исходное название: Кнессет правит формулировки, и старый перевод тогда
/// говорит не о том.
/// </summary>
public class BillTitleSeedService(
    IDbContextFactory<AppDbContext> dbFactory,
    IWebHostEnvironment env,
    ILogger<BillTitleSeedService> logger) : BackgroundService
{
    private static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan GiveUpAfter = TimeSpan.FromMinutes(30);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // На чистой базе законопроектов ещё нет — ждём первой синхронизации.
        var deadline = DateTime.UtcNow + GiveUpAfter;

        while (!stoppingToken.IsCancellationRequested)
        {
            int missing;
            try
            {
                missing = await SeedAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                // Загрузчик не должен ронять приложение.
                logger.LogError(ex, "Ошибка загрузки переводов названий");
                return;
            }

            if (missing == 0) return;

            if (DateTime.UtcNow > deadline)
            {
                logger.LogWarning(
                    "Переводы названий: {Count} записей не загружено — таких " +
                    "законопроектов нет в базе", missing);
                return;
            }

            await Task.Delay(RetryDelay, stoppingToken);
        }
    }

    /// <summary>Возвращает число записей, для которых законопроект ещё не появился.</summary>
    private async Task<int> SeedAsync(CancellationToken ct)
    {
        var path = Path.Combine(env.ContentRootPath, "Seed", "bill-titles.json");
        if (!File.Exists(path)) return 0;

        var json = await File.ReadAllTextAsync(path, ct);
        var seeds = JsonSerializer.Deserialize<List<TitleSeed>>(json);
        if (seeds is null || seeds.Count == 0) return 0;

        await using var db = await dbFactory.CreateDbContextAsync(ct);

        // Одним запросом, а не по записи на законопроект: их сотни, а база
        // во Франкфурте — на каждый round trip уходит около шестидесяти
        // миллисекунд, и построчный обход занял бы минуту чистого ожидания.
        var billIdMap = await db.Bills
            .Select(b => new { b.KnessetBillId, b.Id, b.Name })
            .ToDictionaryAsync(x => x.KnessetBillId, x => (x.Id, x.Name), ct);

        var wanted = seeds
            .Where(s => billIdMap.ContainsKey(s.KnessetBillId))
            .Select(s => billIdMap[s.KnessetBillId].Id)
            .ToList();

        var existing = await db.BillTitles
            .Where(t => wanted.Contains(t.BillId))
            .ToDictionaryAsync(t => (t.BillId, t.LanguageCode), ct);

        var missing = 0;
        var added = 0;
        var refreshed = 0;

        foreach (var seed in seeds)
        {
            if (!billIdMap.TryGetValue(seed.KnessetBillId, out var bill))
            {
                missing++;
                continue;
            }

            if (existing.TryGetValue((bill.Id, seed.LanguageCode), out var row))
            {
                // Название в Кнессете не менялось — перевод по-прежнему верен.
                if (row.SourceName == bill.Name && row.Text == seed.Text) continue;
                refreshed++;
            }
            else
            {
                row = new BillTitle { BillId = bill.Id, LanguageCode = seed.LanguageCode };
                db.BillTitles.Add(row);
                existing[(bill.Id, seed.LanguageCode)] = row;
                added++;
            }

            row.Text = seed.Text;
            row.SourceName = bill.Name;
            row.ModelVersion = seed.ModelVersion;
            row.GeneratedAt = DateTime.UtcNow;
        }

        if (added > 0 || refreshed > 0)
        {
            await db.SaveChangesAsync(ct);
            logger.LogInformation(
                "Переводы названий: добавлено {Added}, обновлено {Refreshed}", added, refreshed);
        }

        return missing;
    }

    private record TitleSeed
    {
        [JsonPropertyName("knesset_bill_id")]
        public int KnessetBillId { get; init; }

        [JsonPropertyName("language_code")]
        public string LanguageCode { get; init; } = "ru";

        [JsonPropertyName("text")]
        public string Text { get; init; } = "";

        [JsonPropertyName("model_version")]
        public string ModelVersion { get; init; } = "manual-v1";
    }
}
