using System.Text.Json.Serialization;

namespace Kneset.Infrastructure.Knesset;

/// <summary>Обёртка ответа OData: {"value":[...]}.</summary>
public class ODataResponse<T>
{
    [JsonPropertyName("value")]
    public List<T> Value { get; set; } = [];
}

public class KnsBill
{
    public int BillID { get; set; }
    public int? KnessetNum { get; set; }
    public string? Name { get; set; }
    public int? SubTypeID { get; set; }
    public string? SubTypeDesc { get; set; }
    public int? StatusID { get; set; }
    public int? Number { get; set; }
    public DateTime? PublicationDate { get; set; }
    public string? SummaryLaw { get; set; }
    public DateTime LastUpdatedDate { get; set; }
}

public class KnsPerson
{
    public int PersonID { get; set; }
    public string? LastName { get; set; }
    public string? FirstName { get; set; }
    public string? GenderDesc { get; set; }
    public string? Email { get; set; }
    public bool? IsCurrent { get; set; }
    public DateTime LastUpdatedDate { get; set; }
}

public class KnsBillInitiator
{
    public int BillInitiatorID { get; set; }
    public int BillID { get; set; }
    public int PersonID { get; set; }
    public bool? IsInitiator { get; set; }
    public int? Ordinal { get; set; }
    public DateTime LastUpdatedDate { get; set; }
}

public class KnsStatus
{
    public int StatusID { get; set; }
    public string? Desc { get; set; }
    public string? TypeDesc { get; set; }
}

public class KnsMkSiteCode
{
    public string? MKSiteCode { get; set; }
    public int KnsID { get; set; }
    public int SiteId { get; set; }
}

public class KnsPersonToPosition
{
    public int PersonToPositionID { get; set; }
    public int PersonID { get; set; }
    public int? KnessetNum { get; set; }
    public string? FactionName { get; set; }
    public bool? IsCurrent { get; set; }
}
