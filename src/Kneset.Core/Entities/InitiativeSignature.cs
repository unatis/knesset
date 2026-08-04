namespace Kneset.Core.Entities;

/// <summary>Подпись пользователя под инициативой. Одна на пользователя (unique-индекс).</summary>
public class InitiativeSignature
{
    public int Id { get; set; }

    public int InitiativeId { get; set; }
    public CitizenInitiative Initiative { get; set; } = null!;

    public string UserId { get; set; } = "";
    public AppUser User { get; set; } = null!;

    public DateTime SignedAt { get; set; }
}
