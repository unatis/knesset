namespace Kneset.Core.Entities;

/// <summary>
/// Действующий закон Израиля (источник: KNS_IsraelLaw). Это не законопроект:
/// законопроект — предложение, а здесь свод того, что уже принято и действует.
/// </summary>
public class IsraelLaw
{
    public int Id { get; set; }

    /// <summary>Внешний идентификатор в системе Кнессета (IsraelLawID).</summary>
    public int KnessetIsraelLawId { get; set; }

    public string Name { get; set; } = "";

    /// <summary>Созыв, при котором закон принят.</summary>
    public int? KnessetNum { get; set; }

    /// <summary>Основной закон — часть конституционного каркаса Израиля.</summary>
    public bool IsBasicLaw { get; set; }

    public bool IsBudgetLaw { get; set; }

    /// <summary>Статус действия: в силе, отменён и т.п.</summary>
    public string? ValidityDesc { get; set; }

    public DateTime? PublicationDate { get; set; }
    public DateTime? ValidityStartDate { get; set; }
    public DateTime? ValidityFinishDate { get; set; }

    public DateTime LastUpdatedDate { get; set; }

    /// <summary>Акты, которые этот закон меняли или которыми он был создан.</summary>
    public List<LawAmendment> Amendments { get; set; } = [];
}
