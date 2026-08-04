namespace Kneset.Core.Entities;

/// <summary>Связь законопроект — инициатор (источник: KNS_BillInitiator).</summary>
public class BillInitiator
{
    public int Id { get; set; }

    public int BillId { get; set; }
    public Bill Bill { get; set; } = null!;

    public int PersonId { get; set; }
    public Person Person { get; set; } = null!;

    /// <summary>true — основной инициатор, false — присоединившийся.</summary>
    public bool IsInitiator { get; set; }

    public int? Ordinal { get; set; }
}
