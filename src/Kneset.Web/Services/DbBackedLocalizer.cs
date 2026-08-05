using System.Globalization;
using Kneset.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Localization;

namespace Kneset.Web.Services;

/// <summary>
/// Локализатор UI: сначала таблица UiTranslations (кэш 5 минут), затем fallback
/// на встроенные .resx. Правка перевода в базе применяется без пересборки —
/// максимум через 5 минут (или сразу после перезапуска).
/// </summary>
public class DbBackedLocalizer(
    IStringLocalizerFactory innerFactory,
    IDbContextFactory<AppDbContext> dbFactory,
    IMemoryCache cache) : IStringLocalizer<SharedResource>
{
    private static readonly TimeSpan CacheTtl = TimeSpan.FromMinutes(5);
    private readonly IStringLocalizer _inner = innerFactory.Create(typeof(SharedResource));

    public LocalizedString this[string name]
    {
        get
        {
            var value = Lookup(name);
            return value is not null
                ? new LocalizedString(name, value)
                : _inner[name];
        }
    }

    public LocalizedString this[string name, params object[] arguments]
    {
        get
        {
            var value = Lookup(name);
            return value is not null
                ? new LocalizedString(name, string.Format(value, arguments))
                : _inner[name, arguments];
        }
    }

    public IEnumerable<LocalizedString> GetAllStrings(bool includeParentCultures)
    {
        var db = GetCultureDictionary(CultureInfo.CurrentUICulture.TwoLetterISOLanguageName);
        var fromResx = _inner.GetAllStrings(includeParentCultures).ToDictionary(s => s.Name, s => s.Value);
        foreach (var (key, value) in db)
            fromResx[key] = value;
        return fromResx.Select(kv => new LocalizedString(kv.Key, kv.Value));
    }

    /// <summary>
    /// Строка на конкретном языке, без оглядки на CurrentUICulture. Нужна фоновой
    /// рассылке уведомлений: она работает вне HTTP-запроса, где культуры нет,
    /// и собирает текст на языке получателя из его настроек.
    /// </summary>
    public string GetString(string key, string lang, params object[] arguments)
    {
        var value = GetCultureDictionary(lang).GetValueOrDefault(key);

        if (value is null)
        {
            // Fallback на .resx: переключаем культуру только на время чтения.
            var previous = CultureInfo.CurrentUICulture;
            try
            {
                CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(lang);
                value = _inner[key].Value;
            }
            catch (CultureNotFoundException)
            {
                value = _inner[key].Value;
            }
            finally
            {
                CultureInfo.CurrentUICulture = previous;
            }
        }

        return arguments.Length > 0 ? string.Format(value, arguments) : value;
    }

    private string? Lookup(string key)
    {
        var lang = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName;
        return GetCultureDictionary(lang).GetValueOrDefault(key);
    }

    private Dictionary<string, string> GetCultureDictionary(string lang)
    {
        return cache.GetOrCreate($"ui-translations:{lang}", entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = CacheTtl;
            using var db = dbFactory.CreateDbContext();
            return db.UiTranslations.AsNoTracking()
                .Where(t => t.LanguageCode == lang)
                .ToDictionary(t => t.Key, t => t.Value);
        })!;
    }
}
