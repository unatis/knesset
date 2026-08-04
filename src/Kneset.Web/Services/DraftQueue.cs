using System.Collections.Concurrent;
using System.Threading.Channels;

namespace Kneset.Web.Services;

/// <summary>
/// Очередь AI-структурирования гражданских инициатив (паттерн — как AnalysisQueue).
/// </summary>
public class DraftQueue
{
    private readonly Channel<int> _channel = Channel.CreateUnbounded<int>();
    private readonly ConcurrentDictionary<int, byte> _pending = new();

    /// <summary>Событие: структура для InitiativeId готова (или ошибка).</summary>
    public event Action<int>? Completed;

    public bool TryEnqueue(int initiativeId)
    {
        if (!_pending.TryAdd(initiativeId, 0)) return false;
        _channel.Writer.TryWrite(initiativeId);
        return true;
    }

    public bool IsPending(int initiativeId) => _pending.ContainsKey(initiativeId);

    public IAsyncEnumerable<int> ReadAllAsync(CancellationToken ct) =>
        _channel.Reader.ReadAllAsync(ct);

    public void MarkCompleted(int initiativeId)
    {
        _pending.TryRemove(initiativeId, out _);
        Completed?.Invoke(initiativeId);
    }
}
