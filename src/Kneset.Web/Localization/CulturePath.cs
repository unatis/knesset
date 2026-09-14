using Microsoft.AspNetCore.Localization;

namespace Kneset.Web.Localization;

/// <summary>
/// Язык в адресе: «/ru/laws/45», «/he/laws/45», «/en/…», «/ar/…».
///
/// Раньше язык жил в cookie, и для машины-читателя все четыре языка были
/// одним адресом: поисковику нечего было индексировать на русском,
/// ответной машине — нечего процитировать. Теперь у каждого языка свой
/// адрес, а страницы связаны через hreflang.
///
/// Устроено через PathBase, а не через параметр маршрута: middleware
/// снимает префикс в <see cref="HttpRequest.PathBase"/>, и дальше
/// приложение живёт как в подпапке — маршруты @page не меняются,
/// относительные ссылки и статика сами уходят под префикс через
/// <c>&lt;base href="/ru/"&gt;</c>, а хаб «/_blazor» под PathBase —
/// штатный сценарий Blazor. Ссылки с ведущим слэшем префикс теряют,
/// поэтому внутри приложения они относительные.
/// </summary>
public static class CulturePath
{
    public static readonly string[] Supported = ["ru", "en", "he", "ar"];

    public const string Default = "ru";

    /// <summary>Ключ в HttpContext.Items: язык, снятый с адреса.</summary>
    public const string ItemKey = "kt-culture";

    /// <summary>
    /// Адреса, которые живут вне языка: служебные точки Blazor, картинки
    /// для соцсетей, отладочные выгрузки, проверка живости. Их не
    /// перенаправляем и не префиксуем.
    /// </summary>
    private static readonly string[] Outside = ["/_", "/og", "/dev", "/healthz", "/api"];

    /// <summary>«/ru/laws/45» → («ru», «/laws/45»). false — префикса нет.</summary>
    public static bool TrySplit(PathString path, out string culture, out PathString rest)
    {
        culture = "";
        rest = path;

        var value = path.Value ?? "";
        if (value.Length < 3 || value[0] != '/') return false;

        var end = value.IndexOf('/', 1);
        var segment = end < 0 ? value[1..] : value[1..end];
        if (segment.Length != 2) return false;

        var match = Supported.FirstOrDefault(c => string.Equals(c, segment, StringComparison.OrdinalIgnoreCase));
        if (match is null) return false;

        culture = match;
        rest = end < 0 ? PathString.Empty : new PathString(value[end..]);
        return true;
    }

    /// <summary>
    /// Страница ли это — то, что стоит перенаправить на адрес с языком.
    /// Файлы с расширением и служебные пути остаются как есть.
    /// </summary>
    public static bool IsPage(HttpRequest request)
    {
        if (!HttpMethods.IsGet(request.Method) && !HttpMethods.IsHead(request.Method)) return false;

        var path = request.Path.Value ?? "/";
        if (Outside.Any(prefix => path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))) return false;

        var lastSlash = path.LastIndexOf('/');
        var lastSegment = lastSlash >= 0 ? path[(lastSlash + 1)..] : path;
        return !lastSegment.Contains('.');
    }

    /// <summary>
    /// Какой язык подставить, когда его нет в адресе: сначала cookie
    /// (человек уже выбирал), потом заголовок браузера, потом русский.
    /// </summary>
    public static string Preferred(HttpRequest request)
    {
        var cookie = request.Cookies[CookieRequestCultureProvider.DefaultCookieName];
        if (cookie is not null)
        {
            var parsed = CookieRequestCultureProvider.ParseCookieValue(cookie);
            var fromCookie = Match(parsed?.UICultures.FirstOrDefault().Value);
            if (fromCookie is not null) return fromCookie;
        }

        foreach (var language in request.GetTypedHeaders().AcceptLanguage.OrderByDescending(l => l.Quality ?? 1))
        {
            var tag = language.Value.Value;
            if (tag is null) continue;
            var fromHeader = Match(tag.Split('-')[0]);
            if (fromHeader is not null) return fromHeader;
        }

        return Default;
    }

    private static string? Match(string? value) =>
        value is null
            ? null
            : Supported.FirstOrDefault(c => string.Equals(c, value, StringComparison.OrdinalIgnoreCase));
}

/// <summary>
/// Снимает язык с адреса в PathBase; адрес без языка перенаправляет
/// на адрес с языком.
/// </summary>
public sealed class CulturePathMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context)
    {
        var request = context.Request;

        if (CulturePath.TrySplit(request.Path, out var culture, out var rest))
        {
            // «/ru» без завершающего слэша: базовый адрес «/ru/» такой документ
            // не содержит, и маршрутизатор Blazor на клиенте отказывается
            // работать. Дописываем слэш навсегда.
            if (!rest.HasValue)
            {
                context.Response.Redirect($"/{culture}/{request.QueryString}", permanent: true);
                return;
            }

            request.PathBase = request.PathBase.Add("/" + culture);
            request.Path = rest;
            context.Items[CulturePath.ItemKey] = culture;
            RememberChoice(context, culture);
            await next(context);
            return;
        }

        if (CulturePath.IsPage(request))
        {
            // Временный редирект, а не постоянный: адрес без языка — это
            // «выбери за меня», и ответ зависит от того, кто спрашивает.
            var preferred = CulturePath.Preferred(request);
            context.Response.Redirect($"/{preferred}{request.Path.Value}{request.QueryString}", permanent: false);
            return;
        }

        await next(context);
    }

    /// <summary>
    /// Запоминает язык в cookie при каждом показе страницы. Тогда адрес без
    /// языка ведёт туда, где человек был в последний раз, а переключатель
    /// языка становится обычной ссылкой в один переход — без промежуточного
    /// запроса, который ставил cookie и возвращал 302, и без второй загрузки.
    /// Путь cookie — корень явно: по умолчанию он сузился бы до «/ru».
    /// </summary>
    private static void RememberChoice(HttpContext context, string culture)
    {
        if (!CulturePath.IsPage(context.Request)) return;

        var wanted = CookieRequestCultureProvider.MakeCookieValue(new RequestCulture(culture, culture));
        if (context.Request.Cookies[CookieRequestCultureProvider.DefaultCookieName] == wanted) return;

        context.Response.Cookies.Append(
            CookieRequestCultureProvider.DefaultCookieName, wanted,
            new CookieOptions { Expires = DateTimeOffset.UtcNow.AddYears(1), IsEssential = true, Path = "/" });
    }
}
