using System.Globalization;
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

// Переводы названий законопроектов: настоящего переводчика пока нет,
// названия подготовлены заранее и лежат файлом рядом с кодом.
builder.Services.AddHostedService<BillTitleSeedService>();

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
