using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;

namespace Kneset.Infrastructure.Knesset;

/// <summary>
/// Клиент внутреннего API сайта Кнессета (https://knesset.gov.il/WebSiteApi/).
/// Используется для данных, которых нет в OData, — фото депутатов и ссылки
/// со страницы действующего закона.
/// Внимание: mkId здесь — это SiteId из KNS_MkSiteCode, не PersonID.
/// </summary>
public class KnessetWebsiteClient(HttpClient http, ILogger<KnessetWebsiteClient> logger)
{
    /// <summary>URL официального фото депутата; null — фото нет или запрос не удался.</summary>
    public async Task<string?> GetMkPhotoUrlAsync(int siteId, CancellationToken ct)
    {
        try
        {
            var header = await http.GetFromJsonAsync<MkDetailsHeader>(
                $"knessetapi/MKs/GetMkdetailsHeader?mkId={siteId}&languageKey=he", ct);
            return string.IsNullOrWhiteSpace(header?.LobbyImage) ? null : header.LobbyImage;
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            logger.LogWarning("Не удалось получить фото депутата siteId={SiteId}: {Error}", siteId, ex.Message);
            return null;
        }
    }

    /// <summary>
    /// Ссылки со страницы закона. Берём здесь, потому что в OData их нет:
    /// набор документов закона (KNS_DocumentIsraelLaw) пуст во всём сервисе,
    /// а сводного действующего текста в открытых данных нет вовсе.
    ///
    /// Страница закона адресуется тем же IsraelLawID, что мы уже храним.
    /// Отдаёт «ספר החוקים הפתוח» — сводный текст в Викитеке (в выборке
    /// из сорока законов есть у тридцати шести) и «כל זכות» — объяснение
    /// простыми словами (у двадцати семи).
    ///
    /// Это апи сайта, а не выгрузка данных: быстрая череда запросов
    /// обрывается соединением, поэтому вызывающий делает паузы.
    /// </summary>
    public async Task<LawSiteInfo?> GetLawInfoAsync(int israelLawId, CancellationToken ct)
    {
        try
        {
            var item = await http.GetFromJsonAsync<LawItem>(
                $"knessetapi/LegislationItem/GetLegislationLawItem?ItemId={israelLawId}", ct);

            var general = item?.General;
            if (general is null) return null;

            return new LawSiteInfo(
                Blank(general.OpenBookUrl),
                Blank(general.KolZchutUrl),
                Blank(general.MinistriesName));
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            logger.LogWarning("Не удалось получить страницу закона {LawId}: {Error}",
                israelLawId, ex.Message);
            return null;
        }
    }

    private static string? Blank(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    public record LawSiteInfo(string? OpenBookUrl, string? KolZchutUrl, string? Ministries);

    private class LawItem
    {
        [JsonPropertyName("general")] public LawGeneral? General { get; set; }
    }

    private class LawGeneral
    {
        [JsonPropertyName("openBookUrl")] public string? OpenBookUrl { get; set; }
        [JsonPropertyName("kolZchutUrl")] public string? KolZchutUrl { get; set; }
        [JsonPropertyName("ministriesName")] public string? MinistriesName { get; set; }
    }

    private class MkDetailsHeader
    {
        [JsonPropertyName("LobbyImage")]
        public string? LobbyImage { get; set; }
    }
}
