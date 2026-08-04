using Kneset.Core.Abstractions;
using Kneset.Core.Entities;
using Kneset.Web;
using Kneset.Infrastructure.Ai;
using Kneset.Infrastructure.Data;
using Kneset.Infrastructure.Knesset;
using Kneset.Web.Components;
using Kneset.Web.Components.Account;
using Kneset.Web.Services;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Localization;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

// Локализация: ru (по умолчанию), en, he, ar. he/ar рендерятся RTL (App.razor).
// Переводы читаются из БД (UiTranslations, кэш 5 мин) с fallback на .resx.
builder.Services.AddLocalization(options => options.ResourcesPath = "Resources");
builder.Services.AddMemoryCache();
builder.Services.AddSingleton<IStringLocalizer<SharedResource>, DbBackedLocalizer>();
builder.Services.AddHostedService<UiTranslationSeedService>();

// Normalize принимает и формат Npgsql, и URI «postgresql://…», который показывает Supabase.
var connectionString = PostgresConnectionString.Normalize(
    builder.Configuration.GetConnectionString("Default")
    ?? throw new InvalidOperationException(
        "Не задана строка подключения ConnectionStrings:Default " +
        "(переменная окружения ConnectionStrings__Default)"));

builder.Services.AddDbContextFactory<AppDbContext>(options =>
    options.UseNpgsql(connectionString));

// --- Аутентификация (ASP.NET Identity, cookie) ---
builder.Services.AddCascadingAuthenticationState();
builder.Services.AddScoped<IdentityRedirectManager>();
builder.Services.AddScoped<AuthenticationStateProvider, IdentityRevalidatingAuthenticationStateProvider>();

builder.Services.AddAuthentication(options =>
    {
        options.DefaultScheme = IdentityConstants.ApplicationScheme;
        options.DefaultSignInScheme = IdentityConstants.ExternalScheme;
    })
    .AddIdentityCookies();

builder.Services.AddIdentityCore<AppUser>(options =>
    {
        // Подтверждение email включим, когда появится реальный отправитель писем.
        options.SignIn.RequireConfirmedAccount = false;
        options.Stores.SchemaVersion = IdentitySchemaVersions.Version3;
    })
    .AddEntityFrameworkStores<AppDbContext>()
    .AddSignInManager()
    .AddDefaultTokenProviders();

builder.Services.AddSingleton<IEmailSender<AppUser>, IdentityNoOpEmailSender>();

builder.Services.AddHttpClient<KnessetODataClient>(http =>
{
    http.BaseAddress = new Uri(builder.Configuration["Knesset:BaseUrl"]
        ?? "https://knesset.gov.il/Odata/ParliamentInfo.svc/");
    http.Timeout = TimeSpan.FromSeconds(120);
});

builder.Services.AddHttpClient<KnessetWebsiteClient>(http =>
{
    http.BaseAddress = new Uri("https://knesset.gov.il/WebSiteApi/");
    http.Timeout = TimeSpan.FromSeconds(30);
});

if (builder.Configuration.GetValue("Sync:Enabled", true))
{
    builder.Services.AddHostedService<KnessetSyncService>();
}

// AI-анализ: провайдер выбирается конфигом. "Stub" — демо-данные без API-ключа;
// "Claude" будет добавлен на позднем этапе (ClaudeBillAnalyzer + Anthropic SDK).
var aiProvider = builder.Configuration["Ai:Provider"] ?? "Stub";
builder.Services.AddSingleton<IBillAnalyzer>(aiProvider switch
{
    "Stub" => new StubBillAnalyzer(),
    _ => throw new InvalidOperationException(
        $"Неизвестный AI-провайдер '{aiProvider}'. Доступно: Stub. Провайдер Claude будет добавлен позже.")
});
builder.Services.AddSingleton<IAnalysisTranslator>(aiProvider switch
{
    "Stub" => new StubAnalysisTranslator(),
    _ => throw new InvalidOperationException($"Неизвестный AI-провайдер '{aiProvider}'.")
});
builder.Services.AddSingleton<AnalysisQueue>();
builder.Services.AddHostedService<AnalysisWorker>();

builder.Services.AddSingleton<IInitiativeDrafter>(aiProvider switch
{
    "Stub" => new StubInitiativeDrafter(),
    _ => throw new InvalidOperationException($"Неизвестный AI-провайдер '{aiProvider}'.")
});
builder.Services.AddSingleton<DraftQueue>();
builder.Services.AddHostedService<DraftWorker>();

// Редакционные контекстные анализы («Контекст и интерпретации») из Seed/*.json.
builder.Services.AddHostedService<ContextSeedService>();

// Динамические OG-превью для шеринга.
builder.Services.AddSingleton<OgImageService>();

// На хостинге TLS обрывается на прокси, и приложению приходит обычный http,
// а исходная схема передаётся в X-Forwarded-Proto. Без её учёта UseHttpsRedirection
// зацикливает редиректы, а Navigation.BaseUri отдаёт http:// в абсолютных og:image —
// краулеры мессенджеров такие превью игнорируют.
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    // Адрес прокси заранее неизвестен, доверяем заголовкам от него.
    options.KnownNetworks.Clear();
    options.KnownProxies.Clear();
});

var app = builder.Build();

app.UseForwardedHeaders();

string[] supportedCultures = ["ru", "en", "he", "ar"];
app.UseRequestLocalization(new RequestLocalizationOptions()
    .SetDefaultCulture("ru")
    .AddSupportedCultures(supportedCultures)
    .AddSupportedUICultures(supportedCultures));

// Переключение языка: кладём культуру в cookie и возвращаемся на ту же страницу.
app.MapGet("/set-culture", (string culture, string? redirectUri, HttpContext http) =>
{
    if (supportedCultures.Contains(culture))
    {
        http.Response.Cookies.Append(
            Microsoft.AspNetCore.Localization.CookieRequestCultureProvider.DefaultCookieName,
            Microsoft.AspNetCore.Localization.CookieRequestCultureProvider.MakeCookieValue(
                new Microsoft.AspNetCore.Localization.RequestCulture(culture, culture)),
            new CookieOptions { Expires = DateTimeOffset.UtcNow.AddYears(1), IsEssential = true });
    }
    return Results.LocalRedirect(string.IsNullOrEmpty(redirectUri) ? "/" : redirectUri);
});

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}
app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.UseHttpsRedirection();

app.UseAntiforgery();

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

// Проверка живости для хостинга. Намеренно не обращается к БД: недоступность Supabase
// не должна помечать деплой упавшим и перезапускать работающий инстанс.
app.MapGet("/healthz", () => Results.Text("ok"));

// Динамические OG-картинки (кэш 1 час: краулеры мессенджеров ходят часто).
app.MapGet("/og/bills/{id:int}.png", async (
    int id, OgImageService og,
    IMemoryCache cache,
    HttpContext http, CancellationToken ct) =>
{
    var bytes = await cache.GetOrCreateAsync($"og-bill-{id}", async entry =>
    {
        entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromHours(1);
        return await og.RenderBillAsync(id, ct);
    });
    if (bytes is null) return Results.NotFound();
    http.Response.Headers.CacheControl = "public, max-age=3600";
    return Results.File(bytes, "image/png");
});

app.MapGet("/og/initiatives/{id:int}.png", async (
    int id, OgImageService og,
    IMemoryCache cache,
    HttpContext http, CancellationToken ct) =>
{
    var bytes = await cache.GetOrCreateAsync($"og-init-{id}", async entry =>
    {
        entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromHours(1);
        return await og.RenderInitiativeAsync(id, ct);
    });
    if (bytes is null) return Results.NotFound();
    http.Response.Headers.CacheControl = "public, max-age=3600";
    return Results.File(bytes, "image/png");
});

// Эндпоинты, которые нужны Identity-компонентам /Account (logout и т.п.).
app.MapAdditionalIdentityEndpoints();

app.Run();
