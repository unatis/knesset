using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;

namespace Kneset.Infrastructure.Knesset;

/// <summary>
/// Клиент внутреннего API сайта Кнессета (https://knesset.gov.il/WebSiteApi/).
/// Используется для данных, которых нет в OData, — в частности, фото депутатов.
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

    private class MkDetailsHeader
    {
        [JsonPropertyName("LobbyImage")]
        public string? LobbyImage { get; set; }
    }
}
