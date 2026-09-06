using System.Text.Json;
using System.Text.Json.Serialization;
using Kneset.Core.Entities;
using Kneset.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Kneset.Web.Services;

/// <summary>
/// Загрузчик партийного состава фракций из Seed/faction-parties.json.
///
/// Партий в API Кнессета нет вообще, поэтому этот слой ведётся вручную
/// и живёт файлом рядом с кодом — тем же способом, что переводы названий
/// законопроектов. Файл здесь источник, а не хранилище: смысл в том,
/// что у строки в репозитории есть автор, дата и причина, а у строки,
/// вписанной прямо в базу, нет ничего.
///
/// Upsert по FactionID Кнессета. Партии фракции переписываются целиком:
/// их две-три, сравнивать по одной дороже, чем заменить.
/// </summary>
public class FactionPartySeedService(
    IDbContextFactory<AppDbContext> dbFactory,
    IWebHostEnvironment env,
    ILogger<FactionPartySeedService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            var changed = await SeedAsync(stoppingToken);
            if (changed > 0)
            {
                logger.LogInformation("Партийный состав фракций: обновлено {Count}", changed);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Не удалось загрузить партийный состав фракций");
        }
    }

    private async Task<int> SeedAsync(CancellationToken ct)
    {
        var path = Path.Combine(env.ContentRootPath, "Seed", "faction-parties.json");
        if (!File.Exists(path))
        {
            logger.LogWarning("Файл {Path} не найден — партийный слой не загружен", path);
            return 0;
        }

        await using var stream = File.OpenRead(path);
        var file = await JsonSerializer.DeserializeAsync<SeedFile>(stream, JsonOptions, ct)
                   ?? throw new InvalidOperationException("faction-parties.json не разобрался");

        var verifiedAt = ParseDate(file.VerifiedAt);

        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var existing = await db.Factions.Include(f => f.Parties).ToDictionaryAsync(f => f.Id, ct);

        var changed = 0;
        foreach (var src in file.Factions)
        {
            if (!existing.TryGetValue(src.FactionId, out var faction))
            {
                faction = new Faction { Id = src.FactionId };
                db.Factions.Add(faction);
                changed++;
            }

            faction.NameHe = src.NameHe;
            faction.OriginKind = src.OriginKind;
            faction.GroupKey = src.Group?.Key;
            faction.GroupDate = ParseDate(src.Group?.Date);
            faction.GroupOrdinal = src.Group?.Ordinal ?? 0;
            faction.Source = src.Source;
            faction.VerifiedAt = verifiedAt;

            // Партии переписываем целиком: их две-три на фракцию.
            var wanted = src.Parties
                .Select((p, i) => new { p.Slug, p.NameHe, p.NameRu, Ordinal = i })
                .ToList();

            var same = faction.Parties.Count == wanted.Count
                       && faction.Parties.OrderBy(p => p.Ordinal)
                           .Zip(wanted, (a, b) => a.Slug == b.Slug
                                                  && a.NameHe == b.NameHe
                                                  && a.NameRu == b.NameRu
                                                  && a.Ordinal == b.Ordinal)
                           .All(x => x);

            if (!same)
            {
                db.FactionParties.RemoveRange(faction.Parties);
                faction.Parties = wanted
                    .Select(p => new FactionParty
                    {
                        FactionId = faction.Id,
                        Slug = p.Slug,
                        NameHe = p.NameHe,
                        NameRu = p.NameRu,
                        Ordinal = p.Ordinal,
                    })
                    .ToList();
                changed++;
            }
        }

        await db.SaveChangesAsync(ct);
        return changed;
    }

    private static DateOnly? ParseDate(string? value) =>
        DateOnly.TryParse(value, out var d) ? d : null;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        ReadCommentHandling = JsonCommentHandling.Skip,
    };

    private record SeedFile
    {
        [JsonPropertyName("verified_at")] public string? VerifiedAt { get; init; }
        [JsonPropertyName("factions")] public List<SeedFaction> Factions { get; init; } = [];
    }

    private record SeedFaction
    {
        [JsonPropertyName("faction_id")] public int FactionId { get; init; }
        [JsonPropertyName("name_he")] public string NameHe { get; init; } = "";
        [JsonPropertyName("origin_kind")] public string OriginKind { get; init; } = "single";
        [JsonPropertyName("group")] public SeedGroup? Group { get; init; }
        [JsonPropertyName("parties")] public List<SeedParty> Parties { get; init; } = [];
        [JsonPropertyName("source")] public string? Source { get; init; }
    }

    private record SeedGroup
    {
        [JsonPropertyName("key")] public string Key { get; init; } = "";
        [JsonPropertyName("date")] public string? Date { get; init; }
        [JsonPropertyName("ordinal")] public int Ordinal { get; init; }
    }

    private record SeedParty
    {
        [JsonPropertyName("slug")] public string Slug { get; init; } = "";
        [JsonPropertyName("name_he")] public string NameHe { get; init; } = "";
        [JsonPropertyName("name_ru")] public string? NameRu { get; init; }
    }
}
