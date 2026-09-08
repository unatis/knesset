using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Kneset.Core.Legislation;
using Microsoft.Extensions.Logging;

namespace Kneset.Infrastructure.Knesset;

/// <summary>
/// Клиент «ספר החוקים הפתוח» — сводных текстов законов в Викитеке.
///
/// Это тот же источник, на который ведёт сам Кнессет со страницы закона,
/// и другого сводного текста в открытом доступе нет: в OData Кнессета
/// текстов законов нет вовсе.
///
/// Викимедиа просит представляться и не частить: без внятного User-Agent
/// и с частыми запросами приходит 429. Поэтому пауза между запросами
/// на стороне вызывающего, а здесь — повтор с нарастающим ожиданием.
/// </summary>
public class WikisourceClient(HttpClient http, ILogger<WikisourceClient> logger)
{
    private static readonly TimeSpan FirstRetryDelay = TimeSpan.FromSeconds(5);
    private const int Attempts = 4;

    /// <summary>
    /// Название страницы из адреса: «.../wiki/חוק-יסוד:_הצבא» → «חוק-יסוד: הצבא».
    /// Возвращает null, если адрес ведёт не в Викитеку.
    /// </summary>
    public static string? TitleFromUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url)) return null;
        if (!url.Contains("wikisource.org/wiki/", StringComparison.OrdinalIgnoreCase)) return null;

        var index = url.IndexOf("/wiki/", StringComparison.OrdinalIgnoreCase);
        var raw = url[(index + "/wiki/".Length)..];
        if (raw.Length == 0) return null;

        return Uri.UnescapeDataString(raw).Replace('_', ' ');
    }

    public async Task<LawTextRevision?> GetTextAsync(string title, CancellationToken ct)
    {
        var url = "w/api.php?action=query&format=json&formatversion=2"
                  + "&prop=revisions&rvprop=ids%7Ctimestamp%7Ccontent&rvslots=main"
                  + $"&titles={Uri.EscapeDataString(title)}";

        var delay = FirstRetryDelay;
        for (var attempt = 1; attempt <= Attempts; attempt++)
        {
            try
            {
                using var response = await http.GetAsync(url, ct);

                // 429 — обычный ответ Викимедиа на частые запросы, не ошибка.
                if (response.StatusCode == HttpStatusCode.TooManyRequests && attempt < Attempts)
                {
                    await Task.Delay(delay, ct);
                    delay *= 2;
                    continue;
                }

                response.EnsureSuccessStatusCode();
                var body = await response.Content.ReadFromJsonAsync<QueryResponse>(ct);

                var page = body?.Query?.Pages?.FirstOrDefault();
                if (page is null || page.Missing) return null;

                var revision = page.Revisions?.FirstOrDefault();
                var wikitext = revision?.Slots?.Main?.Content;
                if (revision is null || string.IsNullOrWhiteSpace(wikitext)) return null;

                var text = WikitextCleaner.Clean(wikitext);
                if (text.Length == 0) return null;

                return new LawTextRevision(revision.RevId, revision.Timestamp, text);
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
            {
                if (attempt == Attempts)
                {
                    logger.LogWarning("Не удалось получить текст «{Title}»: {Error}", title, ex.Message);
                    return null;
                }

                await Task.Delay(delay, ct);
                delay *= 2;
            }
        }

        return null;
    }

    public record LawTextRevision(long Revision, DateTime RevisionAt, string Text);

    private class QueryResponse
    {
        [JsonPropertyName("query")] public QueryBody? Query { get; set; }
    }

    private class QueryBody
    {
        [JsonPropertyName("pages")] public List<Page>? Pages { get; set; }
    }

    private class Page
    {
        [JsonPropertyName("missing")] public bool Missing { get; set; }
        [JsonPropertyName("revisions")] public List<Revision>? Revisions { get; set; }
    }

    private class Revision
    {
        [JsonPropertyName("revid")] public long RevId { get; set; }
        [JsonPropertyName("timestamp")] public DateTime Timestamp { get; set; }
        [JsonPropertyName("slots")] public Slots? Slots { get; set; }
    }

    private class Slots
    {
        [JsonPropertyName("main")] public Slot? Main { get; set; }
    }

    private class Slot
    {
        [JsonPropertyName("content")] public string? Content { get; set; }
    }
}
