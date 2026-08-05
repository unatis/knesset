namespace Kneset.Core.Entities;

public enum DeliveryStatus
{
    /// <summary>Отправлено по-настоящему.</summary>
    Sent = 1,

    /// <summary>Канал ещё не подключён: заглушка записала сообщение в лог.</summary>
    Simulated = 2,

    /// <summary>Отправка не удалась, подробности в Error.</summary>
    Failed = 3,

    /// <summary>Не отправляли: у пользователя нет адреса для этого канала.</summary>
    Skipped = 4
}

/// <summary>
/// Попытка доставки уведомления в конкретный канал. Нужна не только для истории:
/// без неё после засыпания инстанса на середине рассылки внешние каналы получили бы
/// повторную отправку — у уведомления в приложении такой защитой служит сама строка.
/// </summary>
public class NotificationDelivery
{
    public int Id { get; set; }

    public int NotificationId { get; set; }
    public Notification Notification { get; set; } = null!;

    public NotificationChannelKind Channel { get; set; }

    public DeliveryStatus Status { get; set; }

    public string? Error { get; set; }

    public DateTime AttemptedAt { get; set; }
}
