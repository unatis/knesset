namespace Kneset.Core.Entities;

public enum ReactionKind
{
    /// <summary>👍 Поддерживаю.</summary>
    Support = 1,

    /// <summary>👎 Не поддерживаю.</summary>
    Oppose = 2,

    /// <summary>🤔 Не определился.</summary>
    Undecided = 3
}

/// <summary>
/// Реакция пользователя на законопроект. Один голос на пользователя (unique-индекс),
/// голос можно изменить. Этап 2 концепции: отдельно считаем поддержку среди всех
/// и среди определившихся, всегда показываем размер выборки.
/// </summary>
public class BillReaction
{
    public int Id { get; set; }

    public int BillId { get; set; }
    public Bill Bill { get; set; } = null!;

    public string UserId { get; set; } = "";
    public AppUser User { get; set; } = null!;

    public ReactionKind Kind { get; set; }

    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
