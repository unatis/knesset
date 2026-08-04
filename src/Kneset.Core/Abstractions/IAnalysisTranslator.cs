using Kneset.Core.Models;

namespace Kneset.Core.Abstractions;

/// <summary>
/// Переводчик готового анализа на целевой язык. Конвейер: мастер-анализ генерируется
/// один раз (en), переводы делаются лениво по запросу языка — так содержание
/// идентично на всех языках (analysis-policy.md, §6) и дешевле независимых генераций.
/// </summary>
public interface IAnalysisTranslator
{
    string ModelVersion { get; }

    Task<BillAnalysisResult> TranslateAsync(
        BillAnalysisResult source, string targetLanguage, CancellationToken ct = default);
}
