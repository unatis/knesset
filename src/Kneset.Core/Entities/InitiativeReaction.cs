namespace Kneset.Core.Entities;

/// <summary>
/// Реакция пользователя на гражданскую инициативу (за/против/не определился).
/// Дополняет подписи: подпись — формальная поддержка к порогу, реакция — общий
/// срез мнений, включая несогласных. Один голос на пользователя, можно изменить.
/// </summary>
public class InitiativeReaction
{
    public int Id { get; set; }

    public int InitiativeId { get; set; }
    public CitizenInitiative Initiative { get; set; } = null!;

    public string UserId { get; set; } = "";
    public AppUser User { get; set; } = null!;

    public ReactionKind Kind { get; set; }

    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
