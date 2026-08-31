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

    /// <summary>
    /// Пункты повесток комиссий по законопроектам. Фильтр по ItemID отсекает
    /// чужие созывы: без него сущность отдаёт 41 тысячу строк за всю историю,
    /// с ним — около четырёх. ItemTypeID = 2 оставляет только законопроекты,
    /// иначе в выборку попадут запросы, повестки дня и заседания как таковые.
    /// </summary>
    public Task<List<KnsCmtSessionItem>> GetCommitteeSessionItemsAsync(
        int minBillId, DateTime? since, CancellationToken ct)
    {
        var filter = $"ItemTypeID eq 2 and ItemID ge {minBillId}";
        var sinceFilter = SinceFilter(since);
        if (sinceFilter is not null) filter += $" and {sinceFilter}";
        return GetPagedAsync<KnsCmtSessionItem>("KNS_CmtSessionItem", filter, ct);
    }

    /// <summary>То же для пленума: 107 тысяч строк за всю историю против восьми.</summary>
    public Task<List<KnsPlmSessionItem>> GetPlenumSessionItemsAsync(
        int minBillId, DateTime? since, CancellationToken ct)
    {
        var filter = $"ItemTypeID eq 2 and ItemID ge {minBillId}";
        var sinceFilter = SinceFilter(since);
        if (sinceFilter is not null) filter += $" and {sinceFilter}";
        return GetPagedAsync<KnsPlmSessionItem>("KNS_PlmSessionItem", filter, ct);
    }

    /// <summary>
    /// Заседания комиссий нужных созывов.
    ///
    /// Инкремент идёт по LastUpdatedDate самого заседания — не путать с пунктом
    /// повестки. Различие тут и было причиной, по которой сущность раньше
    /// забиралась целиком: при переносе сидения меняется запись заседания,
    /// а у пунктов повестки отметка остаётся прежней, и инкремент по ним
    /// перенос пропускает. По самим заседаниям он его как раз приносит.
    ///
    /// Цена полной выкачки оказалась велика: только комиссий 25-го созыва
    /// 10 825, код берёт ещё и предыдущий, а страница ограничена сотней
    /// записей — $top=1000 сервис всё равно урезает до 100. Выходило
    /// две-три сотни запросов на каждый прогон синхронизации.
    ///
    /// Взамен вызывающий обязан добирать даты неизменившихся заседаний из
    /// своей базы: в ответе их не будет. См. SyncBillSessionsAsync.
    /// </summary>
    public Task<List<KnsCommitteeSession>> GetCommitteeSessionsAsync(
        int minKnesset, DateTime? since, CancellationToken ct)
    {
        var filter = $"KnessetNum ge {minKnesset}";
        var sinceFilter = SinceFilter(since);
        if (sinceFilter is not null) filter += $" and {sinceFilter}";
        return GetPagedAsync<KnsCommitteeSession>("KNS_CommitteeSession", filter, ct);
    }

    /// <summary>
    /// Заседания пленума нужных созывов — их несколько сотен. Инкремент по той
    /// же причине и с тем же условием, что у комиссий.
    /// </summary>
    public Task<List<KnsPlenumSession>> GetPlenumSessionsAsync(
        int minKnesset, DateTime? since, CancellationToken ct)
    {
        var filter = $"KnessetNum ge {minKnesset}";
        var sinceFilter = SinceFilter(since);
        if (sinceFilter is not null) filter += $" and {sinceFilter}";
        return GetPagedAsync<KnsPlenumSession>("KNS_PlenumSession", filter, ct);
    }

    public Task<List<KnsIsraelLaw>> GetIsraelLawsAsync(DateTime? since, CancellationToken ct) =>
        GetPagedAsync<KnsIsraelLaw>("KNS_IsraelLaw", SinceFilter(since), ct);

    /// <summary>Принятые акты потоком: их за шестьдесят тысяч, копить в памяти незачем.</summary>
    public IAsyncEnumerable<List<KnsLaw>> StreamActsAsync(DateTime? since, CancellationToken ct) =>
        StreamPagedAsync<KnsLaw>("KNS_Law", SinceFilter(since), ct);

    public Task<List<KnsLawBinding>> GetLawBindingsAsync(DateTime? since, CancellationToken ct) =>
        GetPagedAsync<KnsLawBinding>("KNS_LawBinding", SinceFilter(since), ct);

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

    /// <summary>
    /// Отдаёт страницы по мере получения, а не одним списком в конце.
    /// Нужно для больших наборов вроде KNS_Law (за шестьдесят тысяч записей):
    /// вызывающий сохраняет каждую страницу сразу, и прерванная загрузка
    /// не пропадает целиком — на бесплатном хостинге инстанс засыпает
    /// раньше, чем успевает пройти шесть сотен страниц.
    /// API ограничивает страницу сотней записей независимо от $top.
    /// </summary>
    public async IAsyncEnumerable<List<T>> StreamPagedAsync<T>(
        string entity, string? filter,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
    {
        var skip = 0;
        var total = 0;
        while (true)
        {
            var url = $"{entity}?$format=json&$top={PageSize}&$skip={skip}";
            if (filter is not null) url += $"&$filter={Uri.EscapeDataString(filter)}";

            var page = await GetWithRetryAsync<T>(url, ct);
            if (page.Count > 0)
            {
                total += page.Count;
                yield return page;
            }

            if (page.Count < PageSize) break;
            skip += PageSize;
        }
        logger.LogInformation("Кнессет API: {Entity} — получено {Count} записей (потоком)", entity, total);
    }

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
