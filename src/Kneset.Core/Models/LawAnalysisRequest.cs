namespace Kneset.Core.Models;

/// <summary>
/// Входные данные для разбора действующего закона.
///
/// Отличие от <see cref="BillAnalysisRequest"/> не в полях, а в вопросе.
/// У законопроекта спрашивают «что предлагают и чем это кончится»,
/// у действующего закона — «что он требует сегодня, кого обязывает
/// и в чём его оспаривают». Поэтому отдельный запрос и отдельная подводка,
/// хотя форма ответа и политика анализа общие.
/// </summary>
public record LawAnalysisRequest
{
    public required int IsraelLawId { get; init; }
    public required string NameHebrew { get; init; }

    /// <summary>Основной закон — конституционного веса, и это меняет разбор.</summary>
    public bool IsBasicLaw { get; init; }

    /// <summary>Статус действия из данных Кнессета: תקף, בטל, נושן.</summary>
    public string? ValidityDesc { get; init; }

    public DateTime? PublicationDate { get; init; }

    /// <summary>Сколько раз закон меняли и когда в последний раз — из истории поправок.</summary>
    public int AmendmentCount { get; init; }

    public DateTime? LastAmendedAt { get; init; }

    /// <summary>Сколько подзаконных актов его меняло: суммы и приложения меняют приказом.</summary>
    public int RegulationCount { get; init; }

    /// <summary>Сводный текст закона.</summary>
    public string? FullText { get; init; }

    /// <summary>Откуда текст и какой ревизии — модель должна знать цену источника.</summary>
    public string? TextSource { get; init; }

    public DateTime? TextRevisionAt { get; init; }

    /// <summary>Язык результата (ru/he/ar/en).</summary>
    public string LanguageCode { get; init; } = "en";
}
