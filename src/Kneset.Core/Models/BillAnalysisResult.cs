using System.Text.Json.Serialization;

namespace Kneset.Core.Models;

/// <summary>
/// Структурированный результат AI-анализа. Эта же схема будет использоваться
/// как structured output при подключении Claude API.
/// </summary>
public record BillAnalysisResult
{
    /// <summary>Название законопроекта, переведённое на язык анализа.</summary>
    [JsonPropertyName("translated_name")]
    public string TranslatedName { get; init; } = "";

    /// <summary>Что предлагает инициатива, простыми словами.</summary>
    [JsonPropertyName("summary")]
    public string Summary { get; init; } = "";

    /// <summary>Кого затрагивает (наёмные работники, пенсионеры, бизнес...).</summary>
    [JsonPropertyName("affected_groups")]
    public List<string> AffectedGroups { get; init; } = [];

    [JsonPropertyName("potential_benefits")]
    public List<AnalysisPoint> PotentialBenefits { get; init; } = [];

    [JsonPropertyName("potential_risks")]
    public List<AnalysisPoint> PotentialRisks { get; init; } = [];

    [JsonPropertyName("arguments_for")]
    public List<AnalysisPoint> ArgumentsFor { get; init; } = [];

    [JsonPropertyName("arguments_against")]
    public List<AnalysisPoint> ArgumentsAgainst { get; init; } = [];

    /// <summary>Вопросы, на которые текст инициативы не даёт ответа.</summary>
    [JsonPropertyName("open_questions")]
    public List<string> OpenQuestions { get; init; } = [];

    [JsonPropertyName("financial_impact")]
    public FinancialImpact? FinancialImpact { get; init; }

    /// <summary>Пометка провайдера (например, «демо-данные»). null — обычный анализ.</summary>
    [JsonPropertyName("provider_note")]
    public string? ProviderNote { get; init; }
}

/// <summary>Один пункт анализа с указанием источника и степени уверенности.</summary>
public record AnalysisPoint
{
    [JsonPropertyName("text")]
    public string Text { get; init; } = "";

    /// <summary>Тип утверждения: fact | inference | risk | position | insufficient_data.</summary>
    [JsonPropertyName("kind")]
    public string Kind { get; init; } = "inference";

    /// <summary>Ссылка на источник (пункт законопроекта, документ).</summary>
    [JsonPropertyName("source_ref")]
    public string? SourceRef { get; init; }

    /// <summary>Уверенность 0..1.</summary>
    [JsonPropertyName("confidence")]
    public double? Confidence { get; init; }
}

public record FinancialImpact
{
    /// <summary>Влияние на граждан.</summary>
    [JsonPropertyName("citizens")]
    public string? Citizens { get; init; }

    /// <summary>Влияние на государственный бюджет.</summary>
    [JsonPropertyName("government")]
    public string? Government { get; init; }
}
