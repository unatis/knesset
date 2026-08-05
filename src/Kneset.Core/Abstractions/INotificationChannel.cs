using Kneset.Core.Entities;
using Kneset.Core.Models;

namespace Kneset.Core.Abstractions;

/// <summary>
/// Канал доставки уведомлений. Реализации подключаются по одной на канал, как
/// сделано с AI-провайдерами: сейчас почти везде заглушки, реальная отправка
/// добавляется отдельным классом без изменения остального кода.
/// </summary>
public interface INotificationChannel
{
    NotificationChannelKind Kind { get; }

    /// <summary>
    /// false — канал показывается в настройках с пометкой «в разработке»:
    /// сообщения не уходят наружу, а только записываются в лог.
    /// </summary>
    bool IsConfigured { get; }

    Task<DeliveryResult> SendAsync(NotificationMessage message, CancellationToken ct);
}

public record DeliveryResult(DeliveryStatus Status, string? Error = null)
{
    public static DeliveryResult Sent() => new(DeliveryStatus.Sent);
    public static DeliveryResult Simulated() => new(DeliveryStatus.Simulated);
    public static DeliveryResult Failed(string error) => new(DeliveryStatus.Failed, error);
}
