using System.Globalization;
using Kneset.Core.Entities;
using Kneset.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Localization;

namespace Kneset.Web.Services;

/// <summary>
/// Импорт UI-переводов из .resx в таблицу UiTranslations при старте.
/// Добавляет только отсутствующие пары (Key, LanguageCode): правки, сделанные
/// в базе, при перезапуске не затираются.
/// </summary>
public class UiTranslationSeedService(
    IStringLocalizerFactory localizerFactory,
    IDbContextFactory<AppDbContext> dbFactory,
    ILogger<UiTranslationSeedService> logger) : IHostedService
{
    private static readonly string[] Cultures = ["ru", "en", "he", "ar"];

    public async Task StartAsync(CancellationToken ct)
    {
        try
        {
            var inner = localizerFactory.Create(typeof(SharedResource));
            await using var db = await dbFactory.CreateDbContextAsync(ct);

            var existing = (await db.UiTranslations
                    .Select(t => new { t.Key, t.LanguageCode })
                    .ToListAsync(ct))
                .Select(t => (t.Key, t.LanguageCode))
                .ToHashSet();

            var originalCulture = CultureInfo.CurrentUICulture;
            var added = 0;
            try
            {
                foreach (var lang in Cultures)
                {
                    CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(lang);
                    foreach (var s in inner.GetAllStrings(includeParentCultures: true))
                    {
                        if (existing.Contains((s.Name, lang))) continue;
                        db.UiTranslations.Add(new UiTranslation
                        {
                            Key = s.Name,
                            LanguageCode = lang,
                            Value = s.Value,
                            UpdatedAt = DateTime.UtcNow
                        });
                        added++;
                    }
                }
            }
            finally
            {
                CultureInfo.CurrentUICulture = originalCulture;
            }

            if (added > 0)
            {
                await db.SaveChangesAsync(ct);
                logger.LogInformation("UI-переводы: импортировано {Count} строк из .resx в базу", added);
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Ошибка импорта UI-переводов");
        }
    }

    public Task StopAsync(CancellationToken ct) => Task.CompletedTask;
}
