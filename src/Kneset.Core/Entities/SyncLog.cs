namespace Kneset.Core.Entities;

/// <summary>Журнал синхронизации с API Кнессета.</summary>
public class SyncLog
{
    public int Id { get; set; }
    public string EntityName { get; set; } = "";
    public DateTime StartedUtc { get; set; }
    public DateTime? FinishedUtc { get; set; }
    public int RecordsUpserted { get; set; }
    public string? Error { get; set; }
}
