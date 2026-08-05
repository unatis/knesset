using Microsoft.AspNetCore.Identity;

namespace Kneset.Core.Entities;

/// <summary>Как часто показывать уведомления о новых законопроектах.</summary>
public enum NotificationMode
{
    /// <summary>
    /// Отдельная запись на каждое событие. Намеренно 0: столбец в базе получает
    /// значение по умолчанию 0, и оно должно означать поведение по умолчанию.
    /// </summary>
    Immediate = 0,

    /// <summary>События «новый законопроект» за сутки схлопываются в одну строку.</summary>
    DailyDigest = 1
}

/// <summary>Пользователь платформы (ASP.NET Identity).</summary>
public class AppUser : IdentityUser
{
    /// <summary>Публичное имя — показывается как автор инициатив.</summary>
    public string? DisplayName { get; set; }

    /// <summary>
    /// Язык уведомлений (he/ar/ru/en). Рассылка идёт в фоне, без HTTP-запроса,
    /// поэтому язык интерфейса из cookie там недоступен — храним явно.
    /// </summary>
    public string PreferredLanguage { get; set; } = "ru";

    public NotificationMode NotificationMode { get; set; } = NotificationMode.Immediate;

    public List<CitizenInitiative> Initiatives { get; set; } = [];
    public List<NotificationSubscription> Subscriptions { get; set; } = [];
    public List<UserNotificationChannel> NotificationChannels { get; set; } = [];
}
