using Kneset.Core.Abstractions;
using Kneset.Core.Models;

namespace Kneset.Infrastructure.Ai;

/// <summary>
/// Заглушка переводчика анализов: возвращает исходный анализ с пометкой.
/// Реальный перевод (Claude, дешёвая модель) подключится сменой Ai:Provider.
/// </summary>
public class StubAnalysisTranslator : IAnalysisTranslator
{
    public async Task<AnalysisTranslation> TranslateAsync(
        BillAnalysisResult source, string targetLanguage,
        string? sourceDocument = null, CancellationToken ct = default)
    {
        await Task.Delay(TimeSpan.FromSeconds(1), ct);
        return new AnalysisTranslation(
            source with
            {
                ProviderNote =
                    $"⚠ Демо: перевод на «{targetLanguage}» появится с подключением AI-провайдера.",
            },
            "stub-translate-v1");
    }
}
