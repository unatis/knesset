using Kneset.Core.Abstractions;
using Kneset.Core.Entities;
using Kneset.Core.Models;

namespace Kneset.Infrastructure.Notifications;

/// <summary>
/// Колокольчик на сайте. Единственный канал, который работает по-настоящему.
/// Доставка здесь — это сама строка в таблице Notifications, которую создаёт
/// сервис рассылки, поэтому отправлять отдельно нечего.
/// </summary>
public class InAppNotificationChannel : INotificationChannel
{
    public NotificationChannelKind Kind => NotificationChannelKind.InApp;

    public bool IsConfigured => true;

    public Task<DeliveryResult> SendAsync(NotificationMessage message, CancellationToken ct) =>
        Task.FromResult(DeliveryResult.Sent());
}
