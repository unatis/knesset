using System.Globalization;
using Kneset.Core.Entities;
using Kneset.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Localization;

namespace Kneset.Web.Services;

/// <summary>
/// Импорт UI-переводов из .resx в таблицу UiTranslations при старте.
///
/// Добавляет отсутствующие пары (Key, LanguageCode) и обновляет те, которых
/// никто не трогал руками — то есть где текущее значение совпадает с тем,
/// что сидер записал в прошлый раз (SeededValue). Правки, сделанные прямо
/// в базе, остаются нетронутыми: в этом и был смысл прежнего «только
/// добавляем», но он заодно делал невидимыми правки в самих .resx —
/// изменённый текст молча игнорировался, и приходилось заводить ключ-двойник.
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

            var existing = await db.UiTranslations
                .ToDictionaryAsync(t => (t.Key, t.LanguageCode), ct);

            var originalCulture = CultureInfo.CurrentUICulture;
            var added = 0;
            var updated = 0;
            var kept = 0;
            try
            {
                foreach (var lang in Cultures)
                {
                    CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(lang);
                    foreach (var s in inner.GetAllStrings(includeParentCultures: true))
                    {
                        if (existing.TryGetValue((s.Name, lang), out var row))
                        {
                            if (row.Value == s.Value)
                            {
                                // Текст совпадает, но SeededValue может быть пуст
                                // у строк, засеянных до появления колонки.
                                // Проставим, чтобы следующую правку было с чем сверить.
                                row.SeededValue ??= s.Value;
                                continue;
                            }

                            // Значение разошлось с .resx. Обновляем только если
                            // в базе стоит ровно то, что сидер туда и записал:
                            // иначе это чья-то правка, и затирать её нельзя.
                            if (row.SeededValue is null || row.SeededValue == row.Value)
                            {
                                row.Value = s.Value;
                                row.SeededValue = s.Value;
                                row.UpdatedAt = DateTime.UtcNow;
                                updated++;
                            }
                            else kept++;

                            continue;
                        }

                        db.UiTranslations.Add(new UiTranslation
                        {
                            Key = s.Name,
                            LanguageCode = lang,
                            Value = s.Value,
                            SeededValue = s.Value,
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

            if (db.ChangeTracker.HasChanges())
            {
                await db.SaveChangesAsync(ct);
                logger.LogInformation(
                    "UI-переводы: добавлено {Added}, обновлено из .resx {Updated}, " +
                    "сохранено правок в базе {Kept}", added, updated, kept);
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Ошибка импорта UI-переводов");
        }
    }

    public Task StopAsync(CancellationToken ct) => Task.CompletedTask;
}
