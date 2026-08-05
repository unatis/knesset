namespace Kneset.Core.Entities;

/// <summary>Способ доставки уведомления.</summary>
public enum NotificationChannelKind
{
    /// <summary>Колокольчик на сайте. Работает всегда, отключить нельзя.</summary>
    InApp = 1,

    Email = 2,
    Telegram = 3,
    WhatsApp = 4,
    FacebookMessenger = 5,
    Sms = 6
}

/// <summary>
/// Настройка канала доставки для пользователя: включён ли и куда слать.
/// Адрес хранится отдельно от аккаунта: почта для уведомлений может отличаться
/// от почты для входа, а у мессенджеров это вообще не почта (chat_id, номер).
/// </summary>
public class UserNotificationChannel
{
    public int Id { get; set; }

    public string UserId { get; set; } = "";
    public AppUser User { get; set; } = null!;

    public NotificationChannelKind Channel { get; set; }

    public bool IsEnabled { get; set; }

    /// <summary>Адрес доставки: email, chat_id Telegram, номер телефона.</summary>
    public string? Address { get; set; }

    public DateTime UpdatedAt { get; set; }
}
