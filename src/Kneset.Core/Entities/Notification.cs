namespace Kneset.Core.Entities;

/// <summary>Что произошло.</summary>
public enum NotificationKind
{
    /// <summary>Появился новый законопроект.</summary>
    NewBill = 1,

    /// <summary>У отслеживаемого законопроекта сменилась стадия.</summary>
    BillStatusChanged = 2
}

/// <summary>
/// Событие для конкретного пользователя. Одно уведомление на пару
/// (пользователь, законопроект): если сработало несколько подписок сразу,
/// в TriggeredBy попадает самая конкретная из них.
/// </summary>
public class Notification
{
    public int Id { get; set; }

    public string UserId { get; set; } = "";
    public AppUser User { get; set; } = null!;

    public NotificationKind Kind { get; set; }

    public int BillId { get; set; }
    public Bill Bill { get; set; } = null!;

    /// <summary>Какая подписка сработала — чтобы показать повод («депутат X внёс»).</summary>
    public SubscriptionKind TriggeredBy { get; set; }

    /// <summary>Депутат или слово, из-за которых пришло уведомление. Для текста повода.</summary>
    public string? TriggerDetail { get; set; }

    /// <summary>
    /// Момент самого события: Bill.FirstSeenAt либо Bill.StatusChangedAt. Входит
    /// в уникальный индекс, поэтому повторный прогон рассылки дублей не создаёт,
    /// а вторая смена стадии у того же закона — создаёт новое уведомление.
    /// </summary>
    public DateTime EventAt { get; set; }

    public DateTime CreatedAt { get; set; }

    /// <summary>null — не прочитано.</summary>
    public DateTime? ReadAt { get; set; }

    public List<NotificationDelivery> Deliveries { get; set; } = [];
}
