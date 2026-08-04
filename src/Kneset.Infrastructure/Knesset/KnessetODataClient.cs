using System.Net.Http.Json;
using Microsoft.Extensions.Logging;

namespace Kneset.Infrastructure.Knesset;

/// <summary>
/// Клиент официального OData API Кнессета (https://knesset.gov.il/Odata/ParliamentInfo.svc/).
/// Сервис отвечает в формате OData v3 JSON light: {"value":[...]}, литералы дат — datetime'...'.
/// </summary>
public class KnessetODataClient(HttpClient http, ILogger<KnessetODataClient> logger)
{
    private const int PageSize = 100;

    public Task<List<KnsStatus>> GetStatusesAsync(CancellationToken ct) =>
        GetPagedAsync<KnsStatus>("KNS_Status", filter: null, ct);

    public Task<List<KnsPerson>> GetPersonsAsync(DateTime? since, CancellationToken ct) =>
        GetPagedAsync<KnsPerson>("KNS_Person", SinceFilter(since), ct);

    public Task<List<KnsMkSiteCode>> GetMkSiteCodesAsync(CancellationToken ct) =>
        GetPagedAsync<KnsMkSiteCode>("KNS_MkSiteCode", filter: null, ct);

    public Task<List<KnsPersonToPosition>> GetCurrentFactionMembershipsAsync(CancellationToken ct) =>
        GetPagedAsync<KnsPersonToPosition>("KNS_PersonToPosition",
            "IsCurrent eq true and FactionName ne null", ct);

    public Task<List<KnsBill>> GetBillsAsync(int minKnessetNum, DateTime? since, CancellationToken ct)
    {
        var filter = $"KnessetNum ge {minKnessetNum}";
        var sinceFilter = SinceFilter(since);
        if (sinceFilter is not null) filter += $" and {sinceFilter}";
        return GetPagedAsync<KnsBill>("KNS_Bill", filter, ct);
    }

    public Task<List<KnsBillInitiator>> GetBillInitiatorsAsync(int minBillId, DateTime? since, CancellationToken ct)
    {
        var filter = $"BillID ge {minBillId}";
        var sinceFilter = SinceFilter(since);
        if (sinceFilter is not null) filter += $" and {sinceFilter}";
        return GetPagedAsync<KnsBillInitiator>("KNS_BillInitiator", filter, ct);
    }

    /// <summary>Максимальный номер созыва среди законопроектов (= текущий созыв).</summary>
    public async Task<int> GetLatestKnessetNumAsync(CancellationToken ct)
    {
        var url = "KNS_Bill?$format=json&$top=1&$orderby=KnessetNum desc";
        var response = await http.GetFromJsonAsync<ODataResponse<KnsBill>>(url, ct);
        return response?.Value.FirstOrDefault()?.KnessetNum
               ?? throw new InvalidOperationException("Не удалось определить текущий созыв Кнессета.");
    }

    private static string? SinceFilter(DateTime? since) =>
        since is null ? null : $"LastUpdatedDate gt datetime'{since:yyyy-MM-ddTHH:mm:ss}'";

    private async Task<List<T>> GetPagedAsync<T>(string entity, string? filter, CancellationToken ct)
    {
        var all = new List<T>();
        var skip = 0;
        while (true)
        {
            var url = $"{entity}?$format=json&$top={PageSize}&$skip={skip}";
            if (filter is not null) url += $"&$filter={Uri.EscapeDataString(filter)}";

            var page = await GetWithRetryAsync<T>(url, ct);
            all.AddRange(page);
            if (page.Count < PageSize) break;
            skip += PageSize;
        }
        logger.LogInformation("Кнессет API: {Entity} — получено {Count} записей", entity, all.Count);
        return all;
    }

    private async Task<List<T>> GetWithRetryAsync<T>(string url, CancellationToken ct)
    {
        var delays = new[] { 2, 5, 15 };
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                var response = await http.GetFromJsonAsync<ODataResponse<T>>(url, ct);
                return response?.Value ?? [];
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException
                                       && attempt < delays.Length && !ct.IsCancellationRequested)
            {
                logger.LogWarning("Кнессет API: сбой запроса {Url} (попытка {Attempt}): {Error}. Повтор через {Delay}с",
                    url, attempt + 1, ex.Message, delays[attempt]);
                await Task.Delay(TimeSpan.FromSeconds(delays[attempt]), ct);
            }
        }
    }
}
