using System.Text.Json;
using Kneset.Core.Entities;
using Kneset.Core.Models;
using Kneset.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Kneset.Web.Services;

/// <summary>
/// Одноразовый сидер редакционных контекстных анализов из Seed/context-analyses.json.
/// Механизм для ручной/редакционной подготовки секций «Контекст и интерпретации»,
/// пока автоматическая генерация не подключена. Upsert по (KnessetBillId, ModelVersion):
/// уже загруженные записи не дублируются.
/// </summary>
public class ContextSeedService(
    IDbContextFactory<AppDbContext> dbFactory,
    IWebHostEnvironment env,
    ILogger<ContextSeedService> logger) : IHostedService
{
    public async Task StartAsync(CancellationToken ct)
    {
        try
        {
            var path = Path.Combine(env.ContentRootPath, "Seed", "context-analyses.json");
            if (!File.Exists(path)) return;

            var json = await File.ReadAllTextAsync(path, ct);
            var seeds = JsonSerializer.Deserialize<List<ContextSeed>>(json);
            if (seeds is null || seeds.Count == 0) return;

            await using var db = await dbFactory.CreateDbContextAsync(ct);
            foreach (var seed in seeds)
            {
                var bill = await db.Bills.AsNoTracking()
                    .FirstOrDefaultAsync(b => b.KnessetBillId == seed.KnessetBillId, ct);
                if (bill is null)
                {
                    logger.LogWarning("Сид контекста: законопроект {KnessetBillId} не найден в базе", seed.KnessetBillId);
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
                logger.LogInformation("Сид контекста: загружен анализ для законопроекта {KnessetBillId}", seed.KnessetBillId);
            }
            await db.SaveChangesAsync(ct);
        }
        catch (Exception ex)
        {
            // Сидер не должен ронять приложение.
            logger.LogError(ex, "Ошибка загрузки сидов контекстного анализа");
        }
    }

    public Task StopAsync(CancellationToken ct) => Task.CompletedTask;

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
