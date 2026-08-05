namespace Kneset.Web.Services;

/// <summary>
/// Сигнал открытым страницам, что появились новые уведомления — колокольчик
/// обновляет счётчик без перезагрузки. Тот же приём, что у AnalysisQueue.Completed.
/// </summary>
public class NotificationEvents
{
    public event Action? Created;

    public void RaiseCreated() => Created?.Invoke();
}
