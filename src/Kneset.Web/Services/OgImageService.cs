using Kneset.Core.Entities;
using Kneset.Infrastructure.Data;
using Kneset.Web.Components.Shared;
using Microsoft.EntityFrameworkCore;
using SixLabors.Fonts;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace Kneset.Web.Services;

/// <summary>
/// Отрисовка динамических OG-превью (1200×630) для шеринга: закон — название,
/// окно влияния, бар реакций; инициатива — прогресс подписей + бар реакций.
/// Результаты кэшируются на уровне endpoint'ов (IMemoryCache, 1 час).
/// ImageSharp кроссплатформенный (System.Drawing на Linux не работает).
/// </summary>
public class OgImageService
{
    private const int Width = 1200;
    private const int Height = 630;

    private static readonly Color BgTop = Color.ParseHex("1A1A40");
    private static readonly Color BgBottom = Color.ParseHex("4A206E");
    private static readonly Color Green = Color.ParseHex("198754");
    private static readonly Color Yellow = Color.ParseHex("FFC107");
    private static readonly Color Red = Color.ParseHex("DC3545");
    private static readonly Color Gray = Color.ParseHex("6C757D");
    private static readonly Color TextMuted = Color.ParseHex("C8C8DC");

    private readonly IDbContextFactory<AppDbContext> _dbFactory;
    private readonly CurrentKnessetService _currentKnesset;
    private readonly FontFamily[] _fallbacks;
    private readonly FontFamily[] _titleFallbacks;
    private readonly Font _brand;
    private readonly Font _title;
    private readonly Font _subtitle;
    private readonly Font _label;

    public OgImageService(
        IDbContextFactory<AppDbContext> dbFactory,
        CurrentKnessetService currentKnesset,
        IWebHostEnvironment env)
    {
        _dbFactory = dbFactory;
        _currentKnesset = currentKnesset;

        var fonts = new FontCollection();
        // ВАЖНО: весь текст рендерится ОДНИМ мультискриптовым шрифтом (иврит, арабский,
        // кириллица, латиница и цифры в одном файле) — Rubik под OFL, см. wwwroot/fonts/README.md.
        // Смешивать шрифты внутри строки нельзя: при fallback'е на отдельный ивритский шрифт
        // bidi-движок переставляет цифры («2026» → «0226»).
        var multiBold = fonts.Add(Path.Combine(env.WebRootPath, "fonts", "Title-Multiscript.ttf"));
        var multiRegular = fonts.Add(Path.Combine(env.WebRootPath, "fonts", "Body-Multiscript.ttf"));

        _fallbacks = [multiRegular];
        _titleFallbacks = [multiRegular];

        _brand = multiBold.CreateFont(34, FontStyle.Bold);
        _title = multiBold.CreateFont(52, FontStyle.Bold);
        _subtitle = multiRegular.CreateFont(34, FontStyle.Regular);
        _label = multiBold.CreateFont(30, FontStyle.Bold);
    }

    public async Task<byte[]?> RenderBillAsync(int billId, CancellationToken ct)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var bill = await db.Bills.AsNoTracking().FirstOrDefaultAsync(b => b.Id == billId, ct);
        if (bill is null) return null;

        var support = await db.BillReactions.CountAsync(r => r.BillId == billId && r.Kind == ReactionKind.Support, ct);
        var oppose = await db.BillReactions.CountAsync(r => r.BillId == billId && r.Kind == ReactionKind.Oppose, ct);
        var undecided = await db.BillReactions.CountAsync(r => r.BillId == billId && r.Kind == ReactionKind.Undecided, ct);

        var currentKnesset = await _currentKnesset.GetAsync(ct);
        var influence = InfluenceWindowBadge.Classify(bill.StatusDesc, bill.KnessetNum, currentKnesset);
        var (influenceColor, influenceText) = influence?.LabelKey switch
        {
            "Inf_Open" => (Green, "Influence window OPEN — committee stage"),
            "Inf_Limited" => (Yellow, "Influence limited — awaiting plenum"),
            "Inf_Pending" => (Gray, "Awaiting a committee"),
            "Inf_Expired" => (Gray, $"Knesset {bill.KnessetNum} term ended"),
            "Inf_Late" => (Red, "Passed — too late to influence"),
            "Inf_Frozen" => (Gray, "Frozen"),
            _ => (Gray, $"Knesset {bill.KnessetNum}")
        };

        return Render(image =>
        {
            DrawHeader(image, $"Knesset {bill.KnessetNum}");

            // Название на иврите — RTL, выравнивание вправо, перенос строк.
            var titleOptions = new RichTextOptions(_title)
            {
                Origin = new PointF(Width - 70, 150),
                WrappingLength = Width - 140,
                HorizontalAlignment = HorizontalAlignment.Right,
                // Auto: bidi-алгоритм сам определяет направление по первому сильному
                // символу (иврит → RTL), а числа внутри остаются LTR.
                TextDirection = TextDirection.Auto,
                FallbackFontFamilies = _titleFallbacks,
                LineSpacing = 1.15f
            };
            image.Mutate(x => x.DrawText(titleOptions, Truncate(bill.Name, 110), Color.White));

            // Русское название подзаголовком.
            if (!string.IsNullOrEmpty(bill.NameRu))
            {
                var subOptions = new RichTextOptions(_subtitle)
                {
                    Origin = new PointF(70, 345),
                    WrappingLength = Width - 140,
                    FallbackFontFamilies = _fallbacks
                };
                image.Mutate(x => x.DrawText(subOptions, Truncate(bill.NameRu, 90), TextMuted));
            }

            // Бейдж окна влияния: цветная точка + текст.
            image.Mutate(x => x.Fill(influenceColor,
                new SixLabors.ImageSharp.Drawing.EllipsePolygon(90, 462, 14)));
            image.Mutate(x => x.DrawText(
                new RichTextOptions(_label) { Origin = new PointF(120, 444), FallbackFontFamilies = _fallbacks },
                influenceText, Color.White));

            DrawReactionBar(image, support, oppose, undecided, y: 530);
        });
    }

    public async Task<byte[]?> RenderInitiativeAsync(int initiativeId, CancellationToken ct)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var initiative = await db.CitizenInitiatives.AsNoTracking()
            .FirstOrDefaultAsync(i => i.Id == initiativeId && i.Status != InitiativeStatus.Draft, ct);
        if (initiative is null) return null;

        var signatures = await db.InitiativeSignatures.CountAsync(s => s.InitiativeId == initiativeId, ct);
        var support = await db.InitiativeReactions.CountAsync(r => r.InitiativeId == initiativeId && r.Kind == ReactionKind.Support, ct);
        var oppose = await db.InitiativeReactions.CountAsync(r => r.InitiativeId == initiativeId && r.Kind == ReactionKind.Oppose, ct);
        var undecided = await db.InitiativeReactions.CountAsync(r => r.InitiativeId == initiativeId && r.Kind == ReactionKind.Undecided, ct);

        return Render(image =>
        {
            DrawHeader(image, "Civic Initiative");

            var titleOptions = new RichTextOptions(_title)
            {
                Origin = new PointF(70, 160),
                WrappingLength = Width - 140,
                FallbackFontFamilies = _fallbacks,
                LineSpacing = 1.15f
            };
            image.Mutate(x => x.DrawText(titleOptions, Truncate(initiative.Title, 110), Color.White));

            // Прогресс подписей.
            var percent = Math.Min(1.0, signatures / (double)Math.Max(1, initiative.SignatureThreshold));
            const int barWidth = Width - 140;
            image.Mutate(x => x.Fill(Color.ParseHex("FFFFFF22"), new SixLabors.ImageSharp.Drawing.RectangularPolygon(70, 420, barWidth, 26)));
            image.Mutate(x => x.Fill(Green, new SixLabors.ImageSharp.Drawing.RectangularPolygon(70, 420, (float)(barWidth * percent), 26)));
            image.Mutate(x => x.DrawText(
                new RichTextOptions(_label) { Origin = new PointF(70, 462) },
                $"{signatures:N0} of {initiative.SignatureThreshold:N0} signatures", Color.White));

            DrawReactionBar(image, support, oppose, undecided, y: 545);
        });
    }

    private static byte[] Render(Action<Image<Rgba32>> draw)
    {
        using var image = new Image<Rgba32>(Width, Height);
        image.Mutate(x => x.Fill(new LinearGradientBrush(
            new PointF(0, 0), new PointF(Width, Height),
            GradientRepetitionMode.None,
            new ColorStop(0, BgTop), new ColorStop(1, BgBottom))));

        draw(image);

        using var ms = new MemoryStream();
        image.SaveAsPng(ms);
        return ms.ToArray();
    }

    private void DrawHeader(Image<Rgba32> image, string rightText)
    {
        image.Mutate(x => x.DrawText(
            new RichTextOptions(_brand) { Origin = new PointF(70, 50) },
            "Kneset Tracker", TextMuted));
        image.Mutate(x => x.DrawText(
            new RichTextOptions(_brand)
            {
                Origin = new PointF(Width - 70, 50),
                HorizontalAlignment = HorizontalAlignment.Right
            },
            rightText, TextMuted));
        image.Mutate(x => x.Fill(Color.ParseHex("FFFFFF33"), new SixLabors.ImageSharp.Drawing.RectangularPolygon(70, 108, Width - 140, 2)));
    }

    private void DrawReactionBar(Image<Rgba32> image, int support, int oppose, int undecided, int y)
    {
        var total = support + oppose + undecided;
        if (total == 0) return;

        const int barWidth = Width - 140;
        float x = 70;
        var segments = new[]
        {
            (Count: support, Color: Green),
            (Count: undecided, Color: Yellow),
            (Count: oppose, Color: Red)
        };
        foreach (var segment in segments)
        {
            if (segment.Count == 0) continue;
            var w = (float)barWidth * segment.Count / total;
            image.Mutate(m => m.Fill(segment.Color, new SixLabors.ImageSharp.Drawing.RectangularPolygon(x, y, w, 30)));
            x += w;
        }

        var supportPercent = (int)Math.Round(support * 100.0 / total);
        image.Mutate(m => m.DrawText(
            new RichTextOptions(_label)
            {
                Origin = new PointF(Width - 70, y - 42),
                HorizontalAlignment = HorizontalAlignment.Right
            },
            $"{supportPercent}% support · {total:N0} votes", Color.White));
    }

    private static string Truncate(string text, int max) =>
        text.Length <= max ? text : text[..max] + "…";
}

