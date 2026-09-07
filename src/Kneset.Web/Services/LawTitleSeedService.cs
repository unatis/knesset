using System.Text.Json;
using System.Text.Json.Serialization;
using Kneset.Core.Entities;
using Kneset.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Kneset.Web.Services;

/// <summary>
/// Загрузчик переводов названий действующих законов из Seed/law-titles.json.
///
/// Устроено так же, как <see cref="BillTitleSeedService"/>, и по той же
/// причине: Кнессет отдаёт названия только на иврите. Отличие в объёме —
/// законов 2 024, переведены пока девятнадцать основных, и переведены
/// вручную: у них есть принятые официальные названия на английском,
/// и машинный перевод их только испортил бы.
///
/// Ключ в файле — IsraelLawID Кнессета, а не наш Id: наш ключ выдаёт база
/// при вставке и на другой базе будет другим.
///
/// Upsert по (закон, язык). Перевод обновляется, если изменилось исходное
/// название: Кнессет правит формулировки, и старый перевод тогда говорит
/// не о том.
/// </summary>
public class LawTitleSeedService(
    IDbContextFactory<AppDbContext> dbFactory,
    IWebHostEnvironment env,
    ILogger<LawTitleSeedService> logger) : BackgroundService
{
    private static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan GiveUpAfter = TimeSpan.FromMinutes(30);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // На чистой базе законов ещё нет — ждём первой синхронизации.
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
                logger.LogError(ex, "Ошибка загрузки переводов названий законов");
                return;
            }

            if (missing == 0) return;

            if (DateTime.UtcNow > deadline)
            {
                logger.LogWarning(
                    "Переводы названий законов: {Count} записей не загружено — " +
                    "таких законов нет в базе", missing);
                return;
            }

            await Task.Delay(RetryDelay, stoppingToken);
        }
    }

    /// <summary>Возвращает число записей, для которых закон ещё не появился.</summary>
    private async Task<int> SeedAsync(CancellationToken ct)
    {
        var path = Path.Combine(env.ContentRootPath, "Seed", "law-titles.json");
        if (!File.Exists(path)) return 0;

        var json = await File.ReadAllTextAsync(path, ct);
        var seeds = JsonSerializer.Deserialize<List<TitleSeed>>(json);
        if (seeds is null || seeds.Count == 0) return 0;

        await using var db = await dbFactory.CreateDbContextAsync(ct);

        var lawMap = await db.IsraelLaws
            .Select(l => new { l.KnessetIsraelLawId, l.Id, l.Name })
            .ToDictionaryAsync(x => x.KnessetIsraelLawId, x => (x.Id, x.Name), ct);

        var wanted = seeds
            .Where(s => lawMap.ContainsKey(s.KnessetIsraelLawId))
            .Select(s => lawMap[s.KnessetIsraelLawId].Id)
            .ToList();

        var existing = await db.IsraelLawTitles
            .Where(t => wanted.Contains(t.IsraelLawId))
            .ToDictionaryAsync(t => (t.IsraelLawId, t.LanguageCode), ct);

        var missing = 0;
        var added = 0;
        var refreshed = 0;

        foreach (var seed in seeds)
        {
            if (!lawMap.TryGetValue(seed.KnessetIsraelLawId, out var law))
            {
                missing++;
                continue;
            }

            if (existing.TryGetValue((law.Id, seed.LanguageCode), out var row))
            {
                // Название в Кнессете не менялось — перевод по-прежнему верен.
                if (row.SourceName == law.Name && row.Text == seed.Text) continue;
                refreshed++;
            }
            else
            {
                row = new IsraelLawTitle { IsraelLawId = law.Id, LanguageCode = seed.LanguageCode };
                db.IsraelLawTitles.Add(row);
                existing[(law.Id, seed.LanguageCode)] = row;
                added++;
            }

            row.Text = seed.Text;
            row.SourceName = law.Name;
            row.ModelVersion = seed.ModelVersion;
            row.GeneratedAt = DateTime.UtcNow;
        }

        if (added > 0 || refreshed > 0)
        {
            await db.SaveChangesAsync(ct);
            logger.LogInformation(
                "Переводы названий законов: добавлено {Added}, обновлено {Refreshed}",
                added, refreshed);
        }

        return missing;
    }

    private record TitleSeed
    {
        [JsonPropertyName("knesset_israel_law_id")]
        public int KnessetIsraelLawId { get; init; }

        [JsonPropertyName("language_code")]
        public string LanguageCode { get; init; } = "ru";

        [JsonPropertyName("text")]
        public string Text { get; init; } = "";

        [JsonPropertyName("model_version")]
        public string ModelVersion { get; init; } = "manual-v1";
    }
}
