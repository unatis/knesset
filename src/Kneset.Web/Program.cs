using System.Globalization;
using Anthropic;
using Kneset.Core.Abstractions;
using Kneset.Core.Entities;
using Kneset.Web;
using Kneset.Infrastructure.Ai;
using Kneset.Infrastructure.Data;
using Kneset.Infrastructure.Knesset;
using Kneset.Infrastructure.Notifications;
using Kneset.Web.Components;
using Kneset.Web.Components.Account;
using Kneset.Web.Services;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Server.Circuits;
using Microsoft.AspNetCore.DataProtection;
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
// Конкретный тип нужен фоновой рассылке: у неё нет CurrentUICulture, и она
// запрашивает строки на языке получателя через DbBackedLocalizer.GetString(key, lang).
builder.Services.AddSingleton<DbBackedLocalizer>();
builder.Services.AddSingleton<IStringLocalizer<SharedResource>>(sp =>
    sp.GetRequiredService<DbBackedLocalizer>());
builder.Services.AddHostedService<UiTranslationSeedService>();

// Язык, выбранный человеком, должен действовать и в цепи Blazor, а не только
// при первой отрисовке страницы. Фабрика вызывается в момент создания цепи —
// внутри запроса, где прослойка локализации уже разобрала куку.
builder.Services.AddScoped<CircuitHandler>(_ =>
    new CultureCircuitHandler(CultureInfo.CurrentCulture, CultureInfo.CurrentUICulture));

// Normalize принимает и формат Npgsql, и URI «postgresql://…», который показывает Supabase.
var connectionString = PostgresConnectionString.Normalize(
    builder.Configuration.GetConnectionString("Default")
    ?? throw new InvalidOperationException(
        "Не задана строка подключения ConnectionStrings:Default " +
        "(переменная окружения ConnectionStrings__Default)"));

builder.Services.AddDbContextFactory<AppDbContext>(options =>
    options.UseNpgsql(connectionString));

// Связка ключей шифрования — в базе, а не в файловой системе контейнера.
// SetApplicationName фиксирует имя приложения: по умолчанию оно выводится из пути
// к содержимому, а он на хостинге может измениться — и тогда ключи «потеряются».
builder.Services.AddDataProtection()
    .PersistKeysToDbContext<AppDbContext>()
    .SetApplicationName("kneset-tracker");

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
    // Свои тексты ошибок: базовый describer отвечает по-английски при любой
    // культуре, а его сообщения видны в обычных местах — правила пароля,
    // занятая почта, протухшая ссылка из письма.
    .AddErrorDescriber<Kneset.Web.Validation.LocalizedIdentityErrorDescriber>()
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

// Уведомления. По-настоящему доставляется только колокольчик на сайте; почта,
// мессенджеры и SMS показываются в настройках с пометкой «в разработке» и пишут
// сообщения в лог. Подключение реального канала — свой класс INotificationChannel
// вместо заглушки, как это сделано с AI-провайдерами.
builder.Services.AddSingleton<INotificationChannel, InAppNotificationChannel>();
foreach (var stubChannel in new[]
         {
             NotificationChannelKind.Email,
             NotificationChannelKind.Telegram,
             NotificationChannelKind.WhatsApp,
             NotificationChannelKind.FacebookMessenger,
             NotificationChannelKind.Sms
         })
{
    var kind = stubChannel;
    builder.Services.AddSingleton<INotificationChannel>(sp =>
        new StubNotificationChannel(kind, sp.GetRequiredService<ILogger<StubNotificationChannel>>()));
}

builder.Services.AddSingleton<NotificationEvents>();
builder.Services.AddSingleton<NotificationTextBuilder>();
builder.Services.AddSingleton<NotificationDispatchService>();
// Личная лента и столбец контекста на главной: связь подписок с законопроектами.
builder.Services.AddSingleton<SubscriptionRelevanceService>();
// Подпись под инициативой: нужна и странице инициативы, и карточке в ленте.
builder.Services.AddSingleton<InitiativeSigningService>();

if (builder.Configuration.GetValue("Sync:Enabled", true))
{
    builder.Services.AddHostedService<KnessetSyncService>();
}

// Документы в текст. Выключено по умолчанию: разовый проход по корпусу —
// это несколько часов и 11.5 тысяч запросов к Кнессету, он не должен
// стартовать сам при каждом подъёме приложения.
builder.Services.AddHostedService<DocumentTextService>();

// AI-анализ: провайдер выбирается конфигом. "Stub" — демо-данные без ключа,
// "Claude" — настоящий разбор через Claude API.
//
// Модели анализа и перевода задаются отдельно и это не экономия ради экономии.
// Замер на 11 законопроектах: анализ — то место, где модели расходятся
// по существу (одна заполняла нехватку данных выдуманными позициями),
// а перевод готового разбора решений не принимает, там достаточно дешёвой
// модели. Отсюда Opus на анализ и Sonnet на перевод по умолчанию.
var aiProvider = builder.Configuration["Ai:Provider"] ?? "Stub";
var analysisModel = builder.Configuration["Ai:AnalysisModel"] ?? "claude-opus-5";
var translationModel = builder.Configuration["Ai:TranslationModel"] ?? "claude-sonnet-5";

if (aiProvider == "Claude")
{
    var apiKey = builder.Configuration["Ai:ApiKey"]
        ?? Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY")
        ?? throw new InvalidOperationException(
            "Ai:Provider=Claude требует ключ: Ai:ApiKey или ANTHROPIC_API_KEY");

    builder.Services.AddSingleton(new AnthropicClient { ApiKey = apiKey });
    builder.Services.AddHttpClient();
}

builder.Services.AddSingleton<IBillAnalyzer>(sp => aiProvider switch
{
    "Stub" => new StubBillAnalyzer(),
    "Claude" => new ClaudeBillAnalyzer(
        sp.GetRequiredService<AnthropicClient>(), analysisModel),
    _ => throw new InvalidOperationException(
        $"Неизвестный AI-провайдер '{aiProvider}'. Доступно: Stub, Claude.")
});

// Перевод разбора. Если задан ключ Gemini, переводим бесплатно, пока
// не упрёмся в суточную квоту (20 запросов на модель), и только потом платно.
// Gemini годится именно здесь: изобретать позиции при переводе нечего,
// а на 2 316 названиях законопроектов он справился без нареканий и бесплатно.
builder.Services.AddSingleton<IAnalysisTranslator>(sp =>
{
    if (aiProvider == "Stub") return new StubAnalysisTranslator();
    if (aiProvider != "Claude")
    {
        throw new InvalidOperationException($"Неизвестный AI-провайдер '{aiProvider}'.");
    }

    var paid = new ClaudeAnalysisTranslator(
        sp.GetRequiredService<AnthropicClient>(), translationModel);

    var geminiKey = builder.Configuration["Ai:Gemini:Key"]
        ?? Environment.GetEnvironmentVariable("GEMINI_KEY");
    if (string.IsNullOrWhiteSpace(geminiKey)) return paid;

    var free = new GeminiAnalysisTranslator(
        sp.GetRequiredService<IHttpClientFactory>().CreateClient(),
        geminiKey,
        builder.Configuration["Ai:Gemini:Model"] ?? "gemini-3.5-flash");

    return new FallbackAnalysisTranslator(
        free, paid, sp.GetRequiredService<ILogger<FallbackAnalysisTranslator>>());
});
builder.Services.AddSingleton<AnalysisQueue>();
builder.Services.AddHostedService<AnalysisWorker>();

// Структурирование гражданских инициатив — отдельная задача, к провайдеру
// пока не переведена. Свой ключ конфига, чтобы Ai:Provider=Claude не создавал
// впечатления, будто она тоже работает через модель.
var drafterProvider = builder.Configuration["Ai:DrafterProvider"] ?? "Stub";
builder.Services.AddSingleton<IInitiativeDrafter>(drafterProvider switch
{
    "Stub" => new StubInitiativeDrafter(),
    _ => throw new InvalidOperationException(
        $"Неизвестный провайдер структурирования '{drafterProvider}'. Доступно: Stub.")
});
builder.Services.AddSingleton<DraftQueue>();
builder.Services.AddHostedService<DraftWorker>();

// Редакционные контекстные анализы («Контекст и интерпретации») из Seed/*.json.
builder.Services.AddHostedService<ContextSeedService>();

// Переводы названий законопроектов: настоящего переводчика пока нет,
// названия подготовлены заранее и лежат файлом рядом с кодом.
builder.Services.AddHostedService<BillTitleSeedService>();

// Готовые AI-анализы на выборке законопроектов — витрина, пока провайдер
// не подключён. Убрать вместе с файлом Seed/bill-analyses.json, когда
// анализ начнёт генерироваться сам.
builder.Services.AddHostedService<BillAnalysisSeedService>();

// Номер текущего созыва: нужен окну влияния, чтобы отличать живой
// законопроект от прекратившегося вместе со своим созывом.
builder.Services.AddSingleton<CurrentKnessetService>();

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

// Атрибуты валидации создаёт среда исполнения, внедрить в них локализатор
// нельзя — отдаём его статически, см. Validation/LocalizedValidation.cs.
Kneset.Web.Validation.ValidationLocalizer.Use(
    app.Services.GetRequiredService<IStringLocalizer<SharedResource>>());

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

// Служебная выгрузка для ручного перевода названий: отдаёт оригиналы
// законопроектов из окна влияния, у которых ещё нет перевода на язык lang.
// Только в Development — в проде маршрут не регистрируется.
if (app.Environment.IsDevelopment())
{
    // Статистика по документам законопроектов — оценить объём работы по
    // превращению их в текст и переводу.
    // Сколько места занято в базе. На бесплатном тарифе Supabase лимит 500 МБ,
    // и от остатка зависит, можно ли хранить извлечённые тексты документов.
    // Зонд по документам: что на самом деле лежит по ссылкам. Метка Format
    // у Кнессета ненадёжна — «DOC» может оказаться и Word 97, и RTF, и docx,
    // а от этого зависит выбор парсера. Смотрим сигнатуру, а не метку.
    // Пробное извлечение текста: скачивает выборку и разбирает её,
    // показывая объём и порядок символов в иврите. Нужно, чтобы решить,
    // из какого формата разбирать и годится ли PDF вообще.
    // Переразбор конкретных документов по KnessetDocumentId. Нужен, чтобы
    // проверять правку разбора на тех самых файлах, где нашёлся дефект,
    // а не на случайной выборке. Ничего не сохраняет.
    // Вход для AI-анализа: то же, что получил бы BillAnalysisRequest.
    // Нужен, чтобы сравнивать провайдеров на строго одинаковых данных,
    // а не на том, что каждый скрипт собрал по-своему.
    // Помечает разборы законопроекта устаревшими, чтобы воркер сгенерировал
    // их заново. Нужно после правки политики или промпта: сами по себе
    // разборы не перегенерируются, IsStale ставится только при изменении
    // текста законопроекта в Кнессете.
    // Сколько разборов от заглушки лежит в базе и сколько из них считаются
    // свежими. Свежая заглушка блокирует генерацию настоящего разбора:
    // воркер работает только когда свежего нет.
    // Сколько версий разбора накопилось у одного законопроекта и различаются
    // ли они по содержанию. История нужна, чтобы сравнить, как менялся разбор;
    // одинаковые заглушки сравнивать не с чем.
    // Разовая операция: снять блокировку с законопроектов, у которых свежий
    // разбор сделан заглушкой. Воркер генерирует только когда свежего нет,
    // поэтому демо-запись навсегда занимает место настоящего разбора.
    //
    // mode=stale помечает устаревшими (обратимо, история сохраняется),
    // mode=delete удаляет (необратимо, зато не копит пустые версии).
    // Скачивает шрифты Google к себе, чтобы браузер посетителя не обращался
    // к чужому домену. Разовая операция: результат — файлы в wwwroot/fonts
    // и готовые правила @font-face, которыми заменяется @import в app.css.
    //
    // Делается из приложения, а не вручную, по двум причинам: подмножества
    // (latin, cyrillic и прочие) и их unicode-range знает только сам Google,
    // а переписывать их руками — верный способ потерять диапазон и получить
    // квадратики вместо букв.
    app.MapGet("/dev/fetch-fonts", async (
        IHttpClientFactory httpFactory, IWebHostEnvironment env, CancellationToken ct) =>
    {
        var http = httpFactory.CreateClient();
        http.Timeout = TimeSpan.FromSeconds(60);
        // Современный User-Agent нужен, чтобы Google отдал woff2, а не ttf:
        // формат он выбирает по клиенту.
        http.DefaultRequestHeaders.Add("User-Agent",
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 " +
            "(KHTML, like Gecko) Chrome/131.0.0.0 Safari/537.36");

        const string cssUrl = "https://fonts.googleapis.com/css2" +
            "?family=Instrument+Serif:ital@0;1" +
            "&family=Geist:wght@400;500;600;700" +
            "&family=JetBrains+Mono:wght@400;500&display=swap";

        var css = await http.GetStringAsync(cssUrl, ct);

        var dir = Path.Combine(env.WebRootPath, "fonts");
        Directory.CreateDirectory(dir);

        // Нужны только письменности, которых нет в локальных многоскриптовых
        // файлах. Иврит и арабский приходят из них, греческий и вьетнамский
        // не нужны вовсе — качать их значит платить весом за неиспользуемое.
        string[] wanted = ["latin", "latin-ext", "cyrillic", "cyrillic-ext"];

        var blocks = System.Text.RegularExpressions.Regex.Matches(
            css, @"/\*\s*(?<subset>[a-z\-]+)\s*\*/\s*(?<body>@font-face\s*\{[^}]*\})",
            System.Text.RegularExpressions.RegexOptions.Singleline);

        // Типизированная запись, а не анонимный объект: по ней потом
        // суммируется вес, а через рефлексию это читалось бы отвратительно.
        var saved = new List<(string Family, string Weight, string Style,
            string Subset, string Name, int Bytes)>();
        var rules = new System.Text.StringBuilder();

        foreach (System.Text.RegularExpressions.Match block in blocks)
        {
            var subset = block.Groups["subset"].Value;
            if (!wanted.Contains(subset)) continue;

            var body = block.Groups["body"].Value;
            var url = System.Text.RegularExpressions.Regex
                .Match(body, @"url\((?<u>https://[^)]+\.woff2)\)").Groups["u"].Value;
            if (url.Length == 0) continue;

            var family = System.Text.RegularExpressions.Regex
                .Match(body, "font-family:\\s*['\"](?<f>[^'\"]+)").Groups["f"].Value;
            var weight = System.Text.RegularExpressions.Regex
                .Match(body, @"font-weight:\s*(?<w>[^;]+)").Groups["w"].Value.Trim();
            var style = System.Text.RegularExpressions.Regex
                .Match(body, @"font-style:\s*(?<s>[^;]+)").Groups["s"].Value.Trim();
            var range = System.Text.RegularExpressions.Regex
                .Match(body, @"unicode-range:\s*(?<r>[^;]+)").Groups["r"].Value.Trim();

            var name = $"{family.Replace(" ", "")}-{weight}-{(style == "italic" ? "i" : "n")}-{subset}.woff2";
            var bytes = await http.GetByteArrayAsync(url, ct);
            await File.WriteAllBytesAsync(Path.Combine(dir, name), bytes, ct);

            saved.Add((family, weight, style, subset, name, bytes.Length));

            rules.AppendLine("@font-face {");
            rules.AppendLine($"    font-family: '{family}';");
            rules.AppendLine($"    font-style: {(style.Length > 0 ? style : "normal")};");
            rules.AppendLine($"    font-weight: {(weight.Length > 0 ? weight : "400")};");
            rules.AppendLine("    font-display: swap;");
            rules.AppendLine($"    src: url('fonts/{name}') format('woff2');");
            if (range.Length > 0) rules.AppendLine($"    unicode-range: {range};");
            rules.AppendLine("}");
        }

        return Results.Json(new
        {
            saved = saved.Count,
            totalKb = saved.Sum(x => x.Bytes) / 1024,
            files = saved.Select(x => new
            {
                x.Family, x.Weight, x.Style, x.Subset, x.Name, kb = x.Bytes / 1024,
            }),
            css = rules.ToString(),
        });
    });

    // Все разборы одного законопроекта: какие языки есть, кто сделал,
    // не устарели ли. Нужно, когда карточка показывает не тот язык,
    // который выбран, — по одному экрану не понять, перевод ещё идёт
    // или он упал.
    app.MapGet("/dev/bill-analyses", async (
        int billId, IDbContextFactory<AppDbContext> factory, CancellationToken ct) =>
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        return Results.Json(await db.BillAnalyses
            .Where(a => a.BillId == billId)
            .OrderBy(a => a.GeneratedAt)
            .Select(a => new
            {
                a.LanguageCode,
                a.ModelVersion,
                a.IsStale,
                a.GeneratedAt,
                chars = (int?)(a.AnalysisJson == null ? null : (int?)a.AnalysisJson.Length),
            })
            .ToListAsync(ct));
    });

    app.MapGet("/dev/stale-stubs", async (
        string mode, IDbContextFactory<AppDbContext> factory, CancellationToken ct) =>
    {
        await using var db = await factory.CreateDbContextAsync(ct);

        var stubs = db.BillAnalyses.Where(a => a.ModelVersion.StartsWith("stub"));

        var affected = mode switch
        {
            "stale" => await stubs.Where(a => !a.IsStale)
                .ExecuteUpdateAsync(s => s.SetProperty(a => a.IsStale, true), ct),
            "delete" => await stubs.ExecuteDeleteAsync(ct),
            _ => throw new ArgumentException("mode: stale или delete"),
        };

        return Results.Json(new { mode, affected });
    });

    app.MapGet("/dev/analysis-dupes", async (
        IDbContextFactory<AppDbContext> factory, CancellationToken ct) =>
    {
        await using var db = await factory.CreateDbContextAsync(ct);

        var rows = await db.BillAnalyses
            .Select(a => new
            {
                a.BillId,
                a.LanguageCode,
                a.ModelVersion,
                a.IsStale,
                len = a.AnalysisJson.Length,
            })
            .ToListAsync(ct);

        var perBill = rows
            .GroupBy(r => r.BillId)
            .Select(g => new
            {
                billId = g.Key,
                versions = g.Count(),
                langs = g.Select(x => x.LanguageCode).Distinct().Count(),
                // Одинаковая длина при одинаковой версии модели — почти
                // наверняка тот же текст.
                distinctContent = g.Select(x => x.ModelVersion + ":" + x.len).Distinct().Count(),
            })
            .OrderByDescending(x => x.versions)
            .Take(10)
            .ToList();

        return Results.Json(new
        {
            total = rows.Count,
            bills = rows.Select(r => r.BillId).Distinct().Count(),
            top = perBill,
        });
    });

    app.MapGet("/dev/stub-analyses", async (
        IDbContextFactory<AppDbContext> factory, CancellationToken ct) =>
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        return Results.Json(new
        {
            byVersion = await db.BillAnalyses
                .GroupBy(a => new { a.ModelVersion, a.IsStale })
                .Select(g => new { g.Key.ModelVersion, g.Key.IsStale, count = g.Count() })
                .OrderByDescending(x => x.count)
                .ToListAsync(ct),
            // Законопроекты, у которых свежий разбор — от заглушки.
            billsBlockedByStub = await db.BillAnalyses
                .Where(a => !a.IsStale && a.ModelVersion.StartsWith("stub"))
                .Select(a => a.BillId).Distinct().CountAsync(ct),
        });
    });

    app.MapGet("/dev/stale-analysis", async (
        int billId, IDbContextFactory<AppDbContext> factory, CancellationToken ct) =>
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var rows = await db.BillAnalyses
            .Where(a => a.BillId == billId && !a.IsStale)
            .ToListAsync(ct);

        foreach (var row in rows) row.IsStale = true;
        await db.SaveChangesAsync(ct);

        return Results.Json(new { billId, marked = rows.Count });
    });

    app.MapGet("/dev/analysis-input", async (
        int take, int minChars, int maxChars,
        IDbContextFactory<AppDbContext> factory, CancellationToken ct) =>
    {
        int[] influenceStages = [108, 113, 150, 111, 114, 130, 141];
        await using var db = await factory.CreateDbContextAsync(ct);

        var descs = await db.Bills
            .Where(b => b.StatusId != null && influenceStages.Contains(b.StatusId.Value))
            .Select(b => b.StatusDesc).Distinct().ToListAsync(ct);

        // Берём законопроекты окна влияния, у которых текст умещается
        // целиком: сравнение моделей не должно упираться в то, что одной
        // достался обрезанный документ. Порядок детерминированный —
        // выборка должна воспроизводиться.
        var rows = await db.Bills
            .Where(b => descs.Contains(b.StatusDesc))
            .Select(b => new
            {
                b.Id,
                b.KnessetBillId,
                b.Name,
                b.SubTypeDesc,
                b.StatusDesc,
                b.KnessetNum,
                b.SummaryLaw,
                committee = b.Committee != null ? b.Committee.Name : null,
                initiators = b.Initiators
                    .Where(i => i.Person != null)
                    .Select(i => i.Person!.FirstName + " " + i.Person!.LastName)
                    .Take(10).ToList(),
                // Предпочитаем docx: логический порядок против
                // восстановленного по координатам глифов.
                doc = b.Documents
                    .Where(d => d.ExtractedText != null && d.ExtractedText.Status == "ok")
                    .OrderBy(d => d.Format == "DOC" ? 0 : 1)
                    .Select(d => new
                    {
                        d.Id,
                        d.GroupTypeDesc,
                        d.Format,
                        text = d.ExtractedText!.Text,
                        chars = d.ExtractedText!.CharCount,
                    })
                    .FirstOrDefault(),
            })
            .Where(x => x.doc != null && x.doc.chars >= minChars && x.doc.chars <= maxChars)
            .OrderBy(x => x.KnessetBillId)
            .Take(take)
            .ToListAsync(ct);

        return Results.Json(rows);
    });

    app.MapGet("/dev/reextract", async (
        string ids, IDbContextFactory<AppDbContext> factory,
        IHttpClientFactory httpFactory, CancellationToken ct) =>
    {
        var wanted = ids.Split(',').Select(int.Parse).ToList();

        await using var db = await factory.CreateDbContextAsync(ct);
        var docs = await db.BillDocuments
            .Where(d => wanted.Contains(d.KnessetDocumentId))
            .ToListAsync(ct);

        var http = httpFactory.CreateClient();
        http.Timeout = TimeSpan.FromSeconds(120);

        var results = new List<object>();
        foreach (var d in docs)
        {
            var bytes = await http.GetByteArrayAsync(d.Url, ct);
            var r = Kneset.Infrastructure.Documents.DocumentTextExtractor.Extract(bytes);
            var (forward, reversed) = Kneset.Infrastructure.Documents
                .DocumentTextExtractor.HebrewOrder(r.Text);

            var revAt = r.Text.IndexOf("קוח תעצה", StringComparison.Ordinal);
            results.Add(new
            {
                d.KnessetDocumentId, d.Format, d.GroupTypeDesc,
                chars = r.CharCount,
                forward, reversed,
                around = revAt >= 0
                    ? r.Text.Substring(Math.Max(0, revAt - 90),
                        Math.Min(200, r.Text.Length - Math.Max(0, revAt - 90)))
                    : r.Text[..Math.Min(160, r.Text.Length)],
            });
        }

        return Results.Json(results);
    });

    app.MapGet("/dev/docextract", async (
        int take, IDbContextFactory<AppDbContext> factory,
        IHttpClientFactory httpFactory, CancellationToken ct) =>
    {
        await using var db = await factory.CreateDbContextAsync(ct);

        var groups = await db.BillDocuments
            .GroupBy(d => new { d.Format, d.GroupTypeDesc })
            .Select(g => new { g.Key.Format, g.Key.GroupTypeDesc, count = g.Count() })
            .OrderByDescending(x => x.count)
            .ToListAsync(ct);

        var sample = new List<BillDocument>();
        foreach (var g in groups)
        {
            if (sample.Count >= take) break;
            sample.AddRange(await db.BillDocuments
                .Where(d => d.Format == g.Format && d.GroupTypeDesc == g.GroupTypeDesc)
                .OrderBy(d => d.KnessetDocumentId)
                .Take(2).ToListAsync(ct));
        }

        var http = httpFactory.CreateClient();
        http.Timeout = TimeSpan.FromSeconds(60);

        var results = new List<object>();
        foreach (var d in sample.Take(take))
        {
            try
            {
                var bytes = await http.GetByteArrayAsync(d.Url, ct);
                var r = Kneset.Infrastructure.Documents.DocumentTextExtractor.Extract(bytes);
                var (forward, reversed) = Kneset.Infrastructure.Documents
                    .DocumentTextExtractor.HebrewOrder(r.Text);

                results.Add(new
                {
                    d.KnessetDocumentId, d.Format, d.GroupTypeDesc,
                    bytes = bytes.Length,
                    kind = r.Kind.ToString(),
                    chars = r.CharCount,
                    forward, reversed,
                    r.Error,
                    head = r.Text.Length > 0 ? r.Text[..Math.Min(160, r.Text.Length)].Replace("\n", " / ") : "",
                });
            }
            catch (Exception ex)
            {
                results.Add(new
                {
                    d.KnessetDocumentId, d.Format, d.GroupTypeDesc,
                    error = ex.GetType().Name + ": " + ex.Message,
                });
            }

            await Task.Delay(300, ct);
        }

        return Results.Json(results);
    });

    app.MapGet("/dev/docprobe", async (
        int take, IDbContextFactory<AppDbContext> factory,
        IHttpClientFactory httpFactory, CancellationToken ct) =>
    {
        await using var db = await factory.CreateDbContextAsync(ct);

        // Ступенчатая выборка: по два документа на каждую пару формат+тип,
        // иначе получим сорок одинаковых законопроектов к предварительному чтению.
        var groups = await db.BillDocuments
            .GroupBy(d => new { d.Format, d.GroupTypeDesc })
            .Select(g => new { g.Key.Format, g.Key.GroupTypeDesc, count = g.Count() })
            .OrderByDescending(x => x.count)
            .ToListAsync(ct);

        var sample = new List<BillDocument>();
        foreach (var g in groups)
        {
            if (sample.Count >= take) break;
            sample.AddRange(await db.BillDocuments
                .Where(d => d.Format == g.Format && d.GroupTypeDesc == g.GroupTypeDesc)
                .OrderBy(d => d.KnessetDocumentId)
                .Take(2).ToListAsync(ct));
        }

        var http = httpFactory.CreateClient();
        http.Timeout = TimeSpan.FromSeconds(60);

        var results = new List<object>();
        foreach (var d in sample.Take(take))
        {
            try
            {
                using var resp = await http.GetAsync(d.Url, ct);
                var bytes = resp.IsSuccessStatusCode
                    ? await resp.Content.ReadAsByteArrayAsync(ct)
                    : Array.Empty<byte>();
                results.Add(new
                {
                    d.KnessetDocumentId, d.Format, d.GroupTypeDesc,
                    status = (int)resp.StatusCode,
                    contentType = resp.Content.Headers.ContentType?.ToString(),
                    length = bytes.Length,
                    head = Convert.ToHexString(bytes.Take(8).ToArray()),
                    kind = Sniff(bytes),
                });
            }
            catch (Exception ex)
            {
                results.Add(new
                {
                    d.KnessetDocumentId, d.Format, d.GroupTypeDesc,
                    error = ex.GetType().Name,
                });
            }

            // Вежливость к серверу Кнессета: не долбим его подряд.
            await Task.Delay(300, ct);
        }

        return Results.Json(results);

        static string Sniff(byte[] b) =>
            b.Length < 8 ? "пусто"
            : b[0] == 0xD0 && b[1] == 0xCF ? "OLE2 — Word 97 .doc"
            : b[0] == 0x50 && b[1] == 0x4B ? "ZIP — .docx/.xlsx/.pptx"
            : b[0] == 0x25 && b[1] == 0x50 ? "PDF"
            : b[0] == 0x7B && b[1] == 0x5C ? "RTF"
            : b[0] == 0xFF && b[1] == 0xD8 ? "JPEG"
            : "неизвестно";
    });

    app.MapGet("/dev/dbsize", async (
        IDbContextFactory<AppDbContext> factory, CancellationToken ct) =>
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var conn = db.Database.GetDbConnection();
        await conn.OpenAsync(ct);

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT 'ВСЕГО' AS name, pg_database_size(current_database()) AS bytes
            UNION ALL
            SELECT relname, pg_total_relation_size(c.oid)
            FROM pg_class c JOIN pg_namespace n ON n.oid = c.relnamespace
            WHERE n.nspname = 'public' AND c.relkind = 'r'
            ORDER BY bytes DESC
            """;

        var rows = new List<object>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
            rows.Add(new { name = reader.GetString(0), bytes = reader.GetInt64(1) });

        return Results.Json(rows);
    });

    app.MapGet("/dev/docstats", async (
        IDbContextFactory<AppDbContext> factory, CancellationToken ct) =>
    {
        await using var db = await factory.CreateDbContextAsync(ct);

        int[] influenceStages = [108, 113, 150, 111, 114, 130, 141];
        var influenceDescs = await db.Bills
            .Where(b => b.StatusId != null && influenceStages.Contains(b.StatusId.Value))
            .Select(b => b.StatusDesc).Distinct().ToListAsync(ct);

        var byFormat = await db.BillDocuments
            .GroupBy(d => new { d.Format, d.GroupTypeDesc })
            .Select(g => new { g.Key.Format, g.Key.GroupTypeDesc, count = g.Count() })
            .OrderByDescending(x => x.count)
            .ToListAsync(ct);

        return Results.Json(new
        {
            bills = await db.Bills.CountAsync(ct),
            documents = await db.BillDocuments.CountAsync(ct),
            billsWithDocs = await db.BillDocuments.Select(d => d.BillId).Distinct().CountAsync(ct),
            byFormat,
            // Метка DOC на практике означает .docx, а из него иврит
            // извлекается логическим порядком. Отсюда вопрос: у скольких
            // законопроектов вообще есть docx, а сколько остаются с одним PDF.
            withDocx = await db.BillDocuments.Where(d => d.Format == "DOC")
                .Select(d => d.BillId).Distinct().CountAsync(ct),
            pdfOnly = await db.BillDocuments.Where(d => d.Format == "PDF")
                .Select(d => d.BillId).Distinct()
                .Except(db.BillDocuments.Where(d => d.Format == "DOC").Select(d => d.BillId).Distinct())
                .CountAsync(ct),
            // То же в разрезе окна влияния — там перевод и анализ нужны в первую очередь.
            windowWithDocx = await db.BillDocuments
                .Where(d => d.Format == "DOC" && influenceDescs.Contains(d.Bill.StatusDesc))
                .Select(d => d.BillId).Distinct().CountAsync(ct),
            // Сколько PDF без docx-двойника вообще отбирается — проверка того,
            // что условие отбора переводится в SQL, а не отсекает всё молча.
            pdfWithoutDocx = await db.BillDocuments
                .Where(d => d.Format == "PDF")
                .Where(d => !db.BillDocuments
                    .Where(x => x.Format == "DOC")
                    .Any(x => x.BillId == d.BillId && x.GroupTypeDesc == d.GroupTypeDesc))
                .CountAsync(ct),
            pdfDone = await db.BillDocumentTexts
                .CountAsync(t => t.ExtractorVersion == "pdfbidi-v1", ct),
            // Отработала ли правка с табуляциями: до неё InnerText их выбрасывал.
            byVersion = await db.BillDocumentTexts
                .GroupBy(t => t.ExtractorVersion)
                .Select(g => new
                {
                    version = g.Key,
                    count = g.Count(),
                    withTabs = g.Count(x => x.Text.Contains("\t")),
                    glued = g.Count(x => x.Text.Contains("חבר הכנסתג")),
                })
                .ToListAsync(ct),
            // Какие именно документы вышли наизнанку — чтобы чинить причину,
            // а не догадку о ней.
            reversedDocs = await db.BillDocumentTexts
                .Where(t => t.Text.Contains("קוח תעצה"))
                .Select(t => new
                {
                    t.BillDocumentId,
                    doc = t.BillDocument.KnessetDocumentId,
                    t.BillDocument.Format,
                    t.BillDocument.GroupTypeDesc,
                    t.CharCount,
                    t.SourceBytes,
                    t.BillDocument.Url,
                    head = t.Text.Substring(0, 120),
                    // Соотношение прямых и обратных вхождений отвечает на вопрос,
                    // сломан документ целиком или это единичный фрагмент.
                    revAt = t.Text.IndexOf("קוח תעצה"),
                    fwdAt = t.Text.IndexOf("הצעת חוק"),
                    around = t.Text.Substring(
                        Math.Max(0, t.Text.IndexOf("קוח תעצה") - 90), 200),
                })
                .ToListAsync(ct),
            // Символы нулевой ширины в извлечённом тексте: они не управляющие,
            // поэтому чистку прошли, но стоят там, где должен быть пробел.
            withZeroWidth = await db.BillDocumentTexts
                .CountAsync(t => t.Text.Contains("\uFEFF"), ct),
            withZeroWidthDocx = await db.BillDocumentTexts
                .CountAsync(t => t.Text.Contains("\uFEFF")
                    && t.ExtractorVersion.StartsWith("openxml"), ct),
            withZeroWidthPdf = await db.BillDocumentTexts
                .CountAsync(t => t.Text.Contains("\uFEFF")
                    && t.ExtractorVersion.StartsWith("pdfbidi"), ct),
            // Контроль качества: сохранился ли иврит логическим порядком.
            // Ищем фразу «הצעת חוק» и её обращение — если извлечение
            // перевернуло строку, найдётся второе, а не первое.
            hebrewForward = await db.BillDocumentTexts
                .CountAsync(t => t.Text.Contains("הצעת חוק"), ct),
            hebrewReversed = await db.BillDocumentTexts
                .CountAsync(t => t.Text.Contains("קוח תעצה"), ct),
            sample = await db.BillDocumentTexts
                .Where(t => t.Status == "ok")
                .OrderBy(t => t.Id).Take(3)
                .Select(t => t.Text.Substring(0, 150))
                .ToListAsync(ct),
            // Законопроекты окна влияния без единого файла: свежие они
            // (Кнессет ещё не опубликовал) или старые (пробел синхронизации)?
            noDocsBills = await db.Bills
                .Where(b => influenceDescs.Contains(b.StatusDesc) && !b.Documents.Any())
                .OrderBy(b => b.LastUpdatedDate)
                .Select(b => new
                {
                    b.KnessetBillId,
                    b.KnessetNum,
                    b.StatusDesc,
                    b.PublicationDate,
                    b.LastUpdatedDate,
                    b.FirstSeenAt,
                    name = b.Name.Substring(0, Math.Min(60, b.Name.Length)),
                })
                .ToListAsync(ct),

            // Водяной знак синхронизации документов — с какого момента
            // мы вообще их забирали.
            docSyncLog = await db.SyncLogs
                .Where(l => l.EntityName.Contains("Document"))
                .OrderByDescending(l => l.StartedUtc)
                .Select(l => new { l.EntityName, l.StartedUtc, l.FinishedUtc, l.RecordsUpserted, l.Error })
                .Take(5)
                .ToListAsync(ct),

            // Покрытие в разрезе ЗАКОНОПРОЕКТОВ, а не документов: у закона
            // может быть пять файлов, и важно, есть ли текст хотя бы у одного.
            billsTotal = await db.Bills.CountAsync(ct),
            billsWithText = await db.Bills.CountAsync(b =>
                b.Documents.Any(d => d.ExtractedText != null && d.ExtractedText.Status == "ok"), ct),
            billsNoDocs = await db.Bills.CountAsync(b => !b.Documents.Any(), ct),
            billsDocsNoText = await db.Bills.CountAsync(b =>
                b.Documents.Any()
                && !b.Documents.Any(d => d.ExtractedText != null && d.ExtractedText.Status == "ok"), ct),

            // То же по окну влияния.
            windowTotal = await db.Bills.CountAsync(b => influenceDescs.Contains(b.StatusDesc), ct),
            windowWithText = await db.Bills.CountAsync(b =>
                influenceDescs.Contains(b.StatusDesc)
                && b.Documents.Any(d => d.ExtractedText != null && d.ExtractedText.Status == "ok"), ct),
            windowNoDocs = await db.Bills.CountAsync(b =>
                influenceDescs.Contains(b.StatusDesc) && !b.Documents.Any(), ct),
            windowDocsNoText = await db.Bills.CountAsync(b =>
                influenceDescs.Contains(b.StatusDesc) && b.Documents.Any()
                && !b.Documents.Any(d => d.ExtractedText != null && d.ExtractedText.Status == "ok"), ct),

            // Ход извлечения текста: по статусам и сколько символов уже лежит.
            extraction = await db.BillDocumentTexts
                .GroupBy(t => t.Status)
                .Select(g => new { status = g.Key, count = g.Count(), chars = g.Sum(x => (long)x.CharCount) })
                .ToListAsync(ct),
            docsTotal = await db.BillDocuments.CountAsync(d => d.Format == "DOC", ct),
            docsDone = await db.BillDocuments
                .CountAsync(d => d.Format == "DOC" && d.ExtractedText != null, ct),
            windowPdfOnly = await db.BillDocuments
                .Where(d => d.Format == "PDF" && influenceDescs.Contains(d.Bill.StatusDesc))
                .Select(d => d.BillId).Distinct()
                .Except(db.BillDocuments.Where(d => d.Format == "DOC").Select(d => d.BillId).Distinct())
                .CountAsync(ct),
            // DOC и PDF часто один и тот же текст: уникальным считаем
            // пару (законопроект, тип документа).
            uniqueTexts = await db.BillDocuments
                .Select(d => new { d.BillId, d.GroupTypeDesc })
                .Distinct().CountAsync(ct),
            // Сколько из них попадает в окно влияния.
            inWindow = await db.BillDocuments
                .Where(d => influenceDescs.Contains(d.Bill.StatusDesc))
                .Select(d => new { d.BillId, d.GroupTypeDesc })
                .Distinct().CountAsync(ct),
        });
    });

    app.MapGet("/dev/untranslated", async (
        string lang, int take, IDbContextFactory<AppDbContext> factory, CancellationToken ct) =>
    {
        int[] influenceStages = [108, 113, 150, 111, 114, 130, 141];
        await using var db = await factory.CreateDbContextAsync(ct);

        // Один и тот же этап встречается под несколькими StatusId, поэтому
        // отбираем по описанию — так же, как это делает фильтр на /bills.
        var descs = await db.Bills
            .Where(b => b.StatusId != null && influenceStages.Contains(b.StatusId.Value))
            .Select(b => b.StatusDesc)
            .Distinct()
            .ToListAsync(ct);

        var rows = await db.Bills
            .Where(b => descs.Contains(b.StatusDesc))
            .Where(b => !b.Titles.Any(t => t.LanguageCode == lang && t.SourceName == b.Name))
            .OrderBy(b => b.KnessetBillId)
            .Take(take)
            .Select(b => new { id = b.KnessetBillId, name = b.Name })
            .ToListAsync(ct);

        return Results.Json(rows);
    });
}

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
