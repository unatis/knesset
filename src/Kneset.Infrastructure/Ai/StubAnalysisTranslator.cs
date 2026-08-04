using Kneset.Core.Abstractions;
using Kneset.Core.Models;

namespace Kneset.Infrastructure.Ai;

/// <summary>
/// Заглушка переводчика анализов: возвращает исходный анализ с пометкой.
/// Реальный перевод (Claude, дешёвая модель) подключится сменой Ai:Provider.
/// </summary>
public class StubAnalysisTranslator : IAnalysisTranslator
{
    public string ModelVersion => "stub-translate-v1";

    public async Task<BillAnalysisResult> TranslateAsync(
        BillAnalysisResult source, string targetLanguage, CancellationToken ct = default)
    {
        await Task.Delay(TimeSpan.FromSeconds(1), ct);
        return source with
        {
            ProviderNote = $"⚠ Демо: перевод на «{targetLanguage}» появится с подключением AI-провайдера."
        };
    }
}
