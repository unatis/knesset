namespace Kneset.Core.Entities;

/// <summary>Депутат Кнессета (источник: KNS_Person + KNS_PersonToPosition).</summary>
public class Person
{
    public int Id { get; set; }

    /// <summary>Внешний ID в системе Кнессета (PersonID).</summary>
    public int KnessetPersonId { get; set; }

    public string FirstName { get; set; } = "";
    public string LastName { get; set; } = "";
    public string? GenderDesc { get; set; }
    public string? Email { get; set; }

    /// <summary>Действующий депутат текущего созыва.</summary>
    public bool IsCurrent { get; set; }

    /// <summary>Название фракции (текущей или последней).</summary>
    public string? FactionName { get; set; }

    /// <summary>ID депутата на сайте Кнессета (KNS_MkSiteCode.SiteId) — отличается от PersonID.</summary>
    public int? KnessetSiteId { get; set; }

    /// <summary>URL официального фото (fs.knesset.gov.il).</summary>
    public string? PhotoUrl { get; set; }

    public DateTime LastUpdatedDate { get; set; }

    public List<BillInitiator> InitiatedBills { get; set; } = [];

    public string FullName => $"{FirstName} {LastName}".Trim();
}
