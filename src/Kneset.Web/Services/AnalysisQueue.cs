using System.Collections.Concurrent;
using System.Threading.Channels;

namespace Kneset.Web.Services;

/// <summary>
/// Очередь генерации AI-анализов по парам (законопроект, язык).
/// Мастер-анализ генерируется на английском; переводы — лениво по запросу языка.
/// </summary>
public class AnalysisQueue
{
    private readonly Channel<(int BillId, string Lang)> _channel =
        Channel.CreateUnbounded<(int, string)>();
    private readonly ConcurrentDictionary<(int, string), byte> _pending = new();

    /// <summary>Событие: анализ (BillId, Lang) готов (или завершился ошибкой).</summary>
    public event Action<int, string>? Completed;

    public bool TryEnqueue(int billId, string lang)
    {
        if (!_pending.TryAdd((billId, lang), 0)) return false;
        _channel.Writer.TryWrite((billId, lang));
        return true;
    }

    public bool IsPending(int billId, string lang) => _pending.ContainsKey((billId, lang));

    /// <summary>Есть ли в очереди задача по этому законопроекту на любом языке.</summary>
    public bool IsPendingAnyLang(int billId) => _pending.Keys.Any(k => k.Item1 == billId);

    public IAsyncEnumerable<(int BillId, string Lang)> ReadAllAsync(CancellationToken ct) =>
        _channel.Reader.ReadAllAsync(ct);

    public void MarkCompleted(int billId, string lang)
    {
        _pending.TryRemove((billId, lang), out _);
        Completed?.Invoke(billId, lang);
    }
}
