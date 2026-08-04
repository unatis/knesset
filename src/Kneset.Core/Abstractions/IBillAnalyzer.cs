using Kneset.Core.Models;

namespace Kneset.Core.Abstractions;

/// <summary>
/// Провайдер AI-анализа законопроекта. Реализации: StubBillAnalyzer (демо),
/// позже — ClaudeBillAnalyzer (Anthropic API). Выбор — конфиг Ai:Provider.
/// </summary>
public interface IBillAnalyzer
{
    /// <summary>Идентификатор модели/версии провайдера — сохраняется вместе с анализом.</summary>
    string ModelVersion { get; }

    Task<BillAnalysisResult> AnalyzeAsync(BillAnalysisRequest request, CancellationToken ct = default);
}
