namespace Kneset.Core.Entities;

public enum CommentKind
{
    /// <summary>Обычный комментарий.</summary>
    General = 0,

    /// <summary>Аргумент «за».</summary>
    ArgumentFor = 1,

    /// <summary>Аргумент «против».</summary>
    ArgumentAgainst = 2,

    /// <summary>Вопрос.</summary>
    Question = 3,

    /// <summary>Предложение по изменению.</summary>
    Suggestion = 4
}

/// <summary>
/// Комментарий к законопроекту ИЛИ гражданской инициативе (заполнено ровно одно из
/// BillId/InitiativeId — контролируется в коде). Kind позволяет группировать
/// обсуждение: аргументы за/против, вопросы, предложения — как в концепции платформы.
/// </summary>
public class Comment
{
    public int Id { get; set; }

    public int? BillId { get; set; }
    public Bill? Bill { get; set; }

    public int? InitiativeId { get; set; }
    public CitizenInitiative? Initiative { get; set; }

    public string UserId { get; set; } = "";
    public AppUser User { get; set; } = null!;

    public CommentKind Kind { get; set; }

    public string Text { get; set; } = "";

    public DateTime CreatedAt { get; set; }
}
