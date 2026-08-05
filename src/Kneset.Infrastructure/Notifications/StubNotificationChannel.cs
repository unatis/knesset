using Kneset.Core.Abstractions;
using Kneset.Core.Entities;
using Kneset.Core.Models;
using Microsoft.Extensions.Logging;

namespace Kneset.Infrastructure.Notifications;

/// <summary>
/// Заглушка канала доставки: наружу ничего не уходит, сообщение пишется в лог.
/// Один класс на все неподключённые каналы — у почты, мессенджеров и SMS
/// различается только реальная отправка, которой здесь и нет.
///
/// Подключение настоящего канала: отдельный класс с IsConfigured = true
/// и регистрация вместо заглушки в Program.cs — по образцу AI-провайдеров.
/// </summary>
public class StubNotificationChannel(
    NotificationChannelKind kind,
    ILogger<StubNotificationChannel> logger) : INotificationChannel
{
    public NotificationChannelKind Kind => kind;

    /// <summary>Всегда false: в настройках канал показывается как «в разработке».</summary>
    public bool IsConfigured => false;

    public Task<DeliveryResult> SendAsync(NotificationMessage message, CancellationToken ct)
    {
        logger.LogInformation(
            "Демо-доставка {Channel} → {Address} [{Lang}]: {Subject} | {Url}",
            kind, Mask(message.Address), message.LanguageCode, message.Subject, message.Url);

        return Task.FromResult(DeliveryResult.Simulated());
    }

    /// <summary>В логи не должны попадать чужие адреса и номера целиком.</summary>
    private static string Mask(string address)
    {
        if (string.IsNullOrEmpty(address)) return "(не задан)";
        if (address.Length <= 4) return new string('*', address.Length);

        var at = address.IndexOf('@');
        return at > 1
            ? $"{address[0]}***{address[(at - 1)..]}"
            : $"{address[..2]}***{address[^2..]}";
    }
}
