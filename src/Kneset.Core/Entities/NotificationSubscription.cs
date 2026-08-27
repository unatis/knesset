namespace Kneset.Core.Entities;

/// <summary>На что подписан пользователь.</summary>
public enum SubscriptionKind
{
    /// <summary>Все новые законопроекты. Включается по умолчанию при регистрации.</summary>
    AllNewBills = 1,

    /// <summary>Новые законопроекты конкретного депутата-инициатора.</summary>
    Person = 2,

    /// <summary>Новые законопроекты, в названии которых встречается слово.</summary>
    Keyword = 3,

    /// <summary>Изменения стадии конкретного законопроекта.</summary>
    Bill = 4
}

/// <summary>
/// Подписка пользователя на события. Одна таблица на все виды: у каждого вида
/// свой набор полей, а уникальность обеспечивается через нормализованный TargetKey —
/// уникальный индекс по nullable-колонкам вёл бы себя непредсказуемо.
/// </summary>
public class NotificationSubscription
{
    public int Id { get; set; }

    public string UserId { get; set; } = "";
    public AppUser User { get; set; } = null!;

    public SubscriptionKind Kind { get; set; }

    /// <summary>Депутат — для Kind = Person.</summary>
    public int? PersonId { get; set; }
    public Person? Person { get; set; }

    /// <summary>Законопроект — для Kind = Bill.</summary>
    public int? BillId { get; set; }
    public Bill? Bill { get; set; }

    /// <summary>Слово для поиска по названию — для Kind = Keyword.</summary>
    public string? Keyword { get; set; }

    /// <summary>
    /// Нормализованная цель подписки: "", "person:123", "bill:45", "kw:חינוך".
    /// Существует только ради уникального индекса (UserId, Kind, TargetKey).
    /// </summary>
    public string TargetKey { get; set; } = "";

    public DateTime CreatedAt { get; set; }

    /// <summary>
    /// Попадает ли законопроект под подписку на слово. Единственное место, где
    /// задано это правило: им пользуются и рассылка уведомлений, и личная лента
    /// на главной. Сравнение регистронезависимое и по обоим названиям —
    /// человек мог подписаться и на ивритское слово, и на русское.
    /// </summary>
    public static bool MatchesKeyword(string name, string? nameRu, string keyword)
    {
        var needle = keyword.Trim();
        if (needle.Length == 0) return false;

        return name.Contains(needle, StringComparison.OrdinalIgnoreCase) ||
               (nameRu is not null && nameRu.Contains(needle, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Собирает TargetKey — единственное место, где задаётся его формат.</summary>
    public static string BuildTargetKey(SubscriptionKind kind, int? personId, int? billId, string? keyword) =>
        kind switch
        {
            SubscriptionKind.AllNewBills => "",
            SubscriptionKind.Person => $"person:{personId}",
            SubscriptionKind.Bill => $"bill:{billId}",
            SubscriptionKind.Keyword => $"kw:{keyword?.Trim().ToLowerInvariant()}",
            _ => throw new ArgumentOutOfRangeException(nameof(kind))
        };
}
