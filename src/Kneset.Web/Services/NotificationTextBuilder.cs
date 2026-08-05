using System.Text.Json;
using Kneset.Core.Entities;
using Kneset.Core.Models;
using Kneset.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Kneset.Web.Services;

/// <summary>
/// Собирает текст уведомления на языке получателя. Название законопроекта берётся
/// из перевода, сделанного AI-конвейером (BillAnalysis.TranslatedName), с фолбэком
/// на оригинал на иврите — переводы появляются лениво, по мере открытия страниц.
/// </summary>
public class NotificationTextBuilder(
    IDbContextFactory<AppDbContext> dbFactory,
    DbBackedLocalizer localizer)
{
    /// <summary>
    /// Названия законопроектов на нужном языке. Загружается один раз на прогон
    /// рассылки, чтобы не ходить в базу за каждым уведомлением.
    /// </summary>
    public async Task<Dictionary<int, string>> LoadTitlesAsync(
        IReadOnlyCollection<int> billIds, string lang, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);

        var bills = await db.Bills.AsNoTracking()
            .Where(b => billIds.Contains(b.Id))
            .Select(b => new { b.Id, b.Name, b.NameRu })
            .ToListAsync(ct);

        var titles = bills.ToDictionary(
            b => b.Id,
            b => lang == "ru" && !string.IsNullOrWhiteSpace(b.NameRu) ? b.NameRu! : b.Name);

        if (lang is "he" or "ru") return titles;

        // Для en/ar русского поля нет — ищем перевод анализа на нужный язык.
        var translated = await db.BillAnalyses.AsNoTracking()
            .Where(a => billIds.Contains(a.BillId) && a.LanguageCode == lang && !a.IsStale)
            .OrderByDescending(a => a.GeneratedAt)
            .Select(a => new { a.BillId, a.AnalysisJson })
            .ToListAsync(ct);

        foreach (var item in translated)
        {
            if (titles.ContainsKey(item.BillId) && TryReadName(item.AnalysisJson, out var name))
                titles[item.BillId] = name;
        }

        return titles;
    }

    public string BuildSubject(Notification notification, string title, string lang) =>
        notification.Kind == NotificationKind.NewBill
            ? localizer.GetString("Notif_NewBillSubject", lang, title)
            : localizer.GetString("Notif_StatusChangedSubject", lang, title);

    /// <summary>Повод: почему это уведомление пришло именно этому человеку.</summary>
    public string BuildReason(Notification notification, string lang) => notification.TriggeredBy switch
    {
        SubscriptionKind.Person => localizer.GetString("Notif_ReasonPerson", lang, notification.TriggerDetail ?? ""),
        SubscriptionKind.Keyword => localizer.GetString("Notif_ReasonKeyword", lang, notification.TriggerDetail ?? ""),
        SubscriptionKind.Bill => localizer.GetString("Notif_ReasonBill", lang),
        _ => localizer.GetString("Notif_ReasonAll", lang)
    };

    public NotificationMessage BuildMessage(
        Notification notification, string title, string lang, string address, string baseUri)
    {
        var url = $"{baseUri.TrimEnd('/')}/bills/{notification.BillId}";

        return new NotificationMessage
        {
            Address = address,
            LanguageCode = lang,
            Subject = BuildSubject(notification, title, lang),
            Body = $"{BuildSubject(notification, title, lang)}\n{BuildReason(notification, lang)}\n{url}",
            Url = url
        };
    }

    private static bool TryReadName(string analysisJson, out string name)
    {
        name = "";
        try
        {
            var result = JsonSerializer.Deserialize<BillAnalysisResult>(analysisJson);
            // Заглушка-переводчик помечает демо-строки квадратными скобками —
            // такое в уведомление не пускаем, лучше оригинал на иврите.
            if (result is null || result.TranslatedName.Length == 0 ||
                result.TranslatedName.StartsWith('['))
            {
                return false;
            }

            name = result.TranslatedName;
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }
}
