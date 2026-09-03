using Kneset.Core.Models;

namespace Kneset.Core.Abstractions;

/// <summary>
/// Переводчик готового анализа на целевой язык. Конвейер: мастер-анализ генерируется
/// один раз (en), переводы делаются лениво по запросу языка — так содержание
/// идентично на всех языках (analysis-policy.md, §6) и дешевле независимых генераций.
/// </summary>
public interface IAnalysisTranslator
{
    Task<AnalysisTranslation> TranslateAsync(
        BillAnalysisResult source, string targetLanguage, CancellationToken ct = default);
}

/// <summary>
/// Перевод и та модель, которая его действительно сделала.
///
/// Версия возвращается результатом, а не читается свойством провайдера,
/// намеренно. Прежде интерфейс отдавал <c>ModelVersion</c> отдельно,
/// и составной переводчик (бесплатный до квоты, потом платный) сообщал
/// в нём того, к кому пойдёт СЛЕДУЮЩИМ. В базу попадала запись
/// «gemini-3.5-flash» о переводе, который сделал Claude, — то есть поле
/// происхождения врало. Так эту ошибку нельзя написать снова.
/// </summary>
public record AnalysisTranslation(BillAnalysisResult Analysis, string ModelVersion);
