using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Kneset.Core.Entities;
using Kneset.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Kneset.Web.Services;

/// <summary>
/// Загрузчик готовых AI-анализов из Seed/bill-analyses.json.
///
/// Витрина, а не механизм: анализы сделаны заранее на выборке законопроектов
/// и лежат файлом, чтобы карточка на странице закона показывала настоящий
/// разбор, а не демо-данные заглушки. Постоянное решение — провайдер
/// (ClaudeBillAnalyzer вместо StubBillAnalyzer) и ленивая генерация
/// по запросу, как это уже устроено в AnalysisWorker.
///
/// Когда провайдер появится, этот загрузчик и его файл нужно убрать: иначе
/// в базе останутся анализы, которые невозможно переgenерировать и чью
/// версию политики никто не отследит.
///
/// Upsert по (законопроект, язык). Признак устаревания сравнивается
/// с Bill.LastUpdatedDate: если Кнессет правил законопроект после
/// генерации, анализ помечается IsStale, а не подменяется молча.
/// </summary>
public class BillAnalysisSeedService(
    IDbContextFactory<AppDbContext> dbFactory,
    IWebHostEnvironment env,
    ILogger<BillAnalysisSeedService> logger) : BackgroundService
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
                logger.LogError(ex, "Ошибка загрузки AI-анализов");
                return;
            }

            if (missing == 0) return;

            if (DateTime.UtcNow > deadline)
            {
                logger.LogWarning(
                    "AI-анализы: {Count} записей не загружено — таких " +
                    "законопроектов нет в базе", missing);
                return;
            }

            await Task.Delay(RetryDelay, stoppingToken);
        }
    }

    /// <summary>Возвращает число записей, для которых законопроект ещё не появился.</summary>
    private async Task<int> SeedAsync(CancellationToken ct)
    {
        var path = Path.Combine(env.ContentRootPath, "Seed", "bill-analyses.json");
        if (!File.Exists(path)) return 0;

        var json = await File.ReadAllTextAsync(path, ct);
        var seeds = JsonSerializer.Deserialize<List<AnalysisSeed>>(json);
        if (seeds is null || seeds.Count == 0) return 0;

        await using var db = await dbFactory.CreateDbContextAsync(ct);

        var ids = seeds.Select(s => s.KnessetBillId).ToList();
        var bills = await db.Bills
            .Where(b => ids.Contains(b.KnessetBillId))
            .Select(b => new { b.KnessetBillId, b.Id, b.LastUpdatedDate })
            .ToDictionaryAsync(x => x.KnessetBillId, x => (x.Id, x.LastUpdatedDate), ct);

        var wanted = bills.Values.Select(v => v.Id).ToList();
        var existing = await db.BillAnalyses
            .Where(a => wanted.Contains(a.BillId))
            .ToDictionaryAsync(a => (a.BillId, a.LanguageCode), ct);

        var missing = 0;
        var added = 0;
        var refreshed = 0;

        foreach (var seed in seeds)
        {
            if (!bills.TryGetValue(seed.KnessetBillId, out var bill))
            {
                missing++;
                continue;
            }

            var payload = seed.Analysis?.ToJsonString() ?? "";
            if (payload.Length == 0) continue;

            if (existing.TryGetValue((bill.Id, seed.LanguageCode), out var row))
            {
                // Сравниваем версию модели, а не сам JSON: колонка типа jsonb
                // нормализуется Postgres (порядок ключей, пробелы), поэтому
                // побайтовое сравнение никогда не совпадёт и загрузчик
                // перезаписывал бы все записи при каждом старте.
                if (row.ModelVersion == seed.ModelVersion) continue;
                refreshed++;
            }
            else
            {
                row = new BillAnalysis { BillId = bill.Id, LanguageCode = seed.LanguageCode };
                db.BillAnalyses.Add(row);
                existing[(bill.Id, seed.LanguageCode)] = row;
                added++;
            }

            row.AnalysisJson = payload;
            row.ModelVersion = seed.ModelVersion;
            row.GeneratedAt = DateTime.UtcNow;
            row.BillLastUpdatedAt = bill.LastUpdatedDate;
            row.IsStale = false;
        }

        if (added > 0 || refreshed > 0)
        {
            await db.SaveChangesAsync(ct);
            logger.LogInformation(
                "AI-анализы: добавлено {Added}, обновлено {Refreshed}", added, refreshed);
        }

        return missing;
    }

    private record AnalysisSeed
    {
        [JsonPropertyName("knesset_bill_id")]
        public int KnessetBillId { get; init; }

        [JsonPropertyName("language_code")]
        public string LanguageCode { get; init; } = "ru";

        [JsonPropertyName("model_version")]
        public string ModelVersion { get; init; } = "";

        /// <summary>
        /// Разбор как есть: в базе он лежит строкой jsonb, поэтому разбирать
        /// его в типы и собирать обратно незачем — достаточно сохранить форму.
        /// </summary>
        [JsonPropertyName("analysis")]
        public JsonNode? Analysis { get; init; }
    }
}
