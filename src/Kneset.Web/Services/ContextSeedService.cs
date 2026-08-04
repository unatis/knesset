using System.Text.Json;
using Kneset.Core.Entities;
using Kneset.Core.Models;
using Kneset.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Kneset.Web.Services;

/// <summary>
/// Сидер редакционных контекстных анализов из Seed/context-analyses.json.
/// Механизм для ручной/редакционной подготовки секций «Контекст и интерпретации»,
/// пока автоматическая генерация не подключена. Upsert по (KnessetBillId, ModelVersion):
/// уже загруженные записи не дублируются.
/// </summary>
public class ContextSeedService(
    IDbContextFactory<AppDbContext> dbFactory,
    IWebHostEnvironment env,
    ILogger<ContextSeedService> logger) : BackgroundService
{
    private static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan GiveUpAfter = TimeSpan.FromMinutes(30);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // На чистой базе законопроекты появляются только после первой синхронизации,
        // поэтому сидер ждёт их и повторяет попытки, а не сдаётся на первом проходе.
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
                // Сидер не должен ронять приложение.
                logger.LogError(ex, "Ошибка загрузки сидов контекстного анализа");
                return;
            }

            if (missing == 0) return;

            if (DateTime.UtcNow > deadline)
            {
                logger.LogWarning(
                    "Сид контекста: {Count} записей так и не загружено — соответствующих " +
                    "законопроектов нет в базе", missing);
                return;
            }

            await Task.Delay(RetryDelay, stoppingToken);
        }
    }

    /// <summary>Загружает недостающие сиды. Возвращает число записей, для которых
    /// законопроект ещё не появился в базе.</summary>
    private async Task<int> SeedAsync(CancellationToken ct)
    {
        var path = Path.Combine(env.ContentRootPath, "Seed", "context-analyses.json");
        if (!File.Exists(path)) return 0;

        var json = await File.ReadAllTextAsync(path, ct);
        var seeds = JsonSerializer.Deserialize<List<ContextSeed>>(json);
        if (seeds is null || seeds.Count == 0) return 0;

        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var missing = 0;

        foreach (var seed in seeds)
        {
            var bill = await db.Bills.AsNoTracking()
                .FirstOrDefaultAsync(b => b.KnessetBillId == seed.KnessetBillId, ct);
            if (bill is null)
            {
                missing++;
                continue;
            }

            var exists = await db.BillContextAnalyses.AnyAsync(c =>
                c.BillId == bill.Id && c.ModelVersion == seed.ModelVersion && !c.IsStale, ct);
            if (exists) continue;

            db.BillContextAnalyses.Add(new BillContextAnalysis
            {
                BillId = bill.Id,
                ContextJson = JsonSerializer.Serialize(seed.Context),
                ModelVersion = seed.ModelVersion,
                LanguageCode = seed.LanguageCode,
                GeneratedAt = DateTime.UtcNow
            });
            logger.LogInformation("Сид контекста: загружен анализ для законопроекта {KnessetBillId}",
                seed.KnessetBillId);
        }

        await db.SaveChangesAsync(ct);
        return missing;
    }

    private record ContextSeed
    {
        [System.Text.Json.Serialization.JsonPropertyName("knesset_bill_id")]
        public int KnessetBillId { get; init; }

        [System.Text.Json.Serialization.JsonPropertyName("model_version")]
        public string ModelVersion { get; init; } = "";

        [System.Text.Json.Serialization.JsonPropertyName("language_code")]
        public string LanguageCode { get; init; } = "ru";

        [System.Text.Json.Serialization.JsonPropertyName("context")]
        public BillContextResult Context { get; init; } = new();
    }
}
