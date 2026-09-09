using Kneset.Core.Models;

namespace Kneset.Core.Abstractions;

/// <summary>
/// Провайдер разбора действующего закона.
///
/// Форма ответа общая с законопроектами (<see cref="BillAnalysisResult"/>):
/// сводка, кого затрагивает, что даёт и чем ограничивает, доводы двух
/// сторон, влияние на права, открытые вопросы. Общая она не из экономии —
/// читателю нужен один и тот же разбор независимо от того, смотрит он
/// на проект или на действующий закон, и переводится он тем же
/// конвейером.
/// </summary>
public interface ILawAnalyzer
{
    string ModelVersion { get; }

    Task<BillAnalysisResult> AnalyzeAsync(LawAnalysisRequest request, CancellationToken ct = default);
}
