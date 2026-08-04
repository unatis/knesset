using System.Text.Json.Serialization;

namespace Kneset.Core.Models;

/// <summary>
/// Секция «Контекст и интерпретации» — аналитический слой ПОВЕРХ фактического анализа.
/// Всё содержимое — интерпретация, UI обязан маркировать это явно.
/// Схема и правила заполнения — Ai/analysis-policy.md, раздел 4.
/// </summary>
public record BillContextResult
{
    /// <summary>Кому выгодно — по группам, с объяснением интереса.</summary>
    [JsonPropertyName("beneficiaries")]
    public List<StakeholderNote> Beneficiaries { get; init; } = [];

    /// <summary>Кто настороженно или проигрывает — по группам.</summary>
    [JsonPropertyName("concerned")]
    public List<StakeholderNote> Concerned { get; init; } = [];

    /// <summary>Цели де-факто в политическом контексте (интерпретация).</summary>
    [JsonPropertyName("de_facto_goals")]
    public List<string> DeFactoGoals { get; init; } = [];

    /// <summary>Исторические параллели с извлечёнными уроками.</summary>
    [JsonPropertyName("historical_parallels")]
    public List<HistoricalParallel> HistoricalParallels { get; init; } = [];

    /// <summary>Позиции сторон — весь спектр, включая неоднородность внутри каждой стороны.</summary>
    [JsonPropertyName("perspectives")]
    public List<PerspectiveNote> Perspectives { get; init; } = [];

    /// <summary>Влияние на общество: краткосрочное и долгосрочное.</summary>
    [JsonPropertyName("public_impact")]
    public List<string> PublicImpact { get; init; } = [];

    [JsonPropertyName("provider_note")]
    public string? ProviderNote { get; init; }
}

public record StakeholderNote
{
    [JsonPropertyName("group")]
    public string Group { get; init; } = "";

    [JsonPropertyName("note")]
    public string Note { get; init; } = "";
}

public record HistoricalParallel
{
    [JsonPropertyName("case")]
    public string Case { get; init; } = "";

    [JsonPropertyName("lesson")]
    public string Lesson { get; init; } = "";
}

/// <summary>Спектр позиций одной стороны/группы — по правилу симметрии №3.</summary>
public record PerspectiveNote
{
    [JsonPropertyName("group")]
    public string Group { get; init; } = "";

    [JsonPropertyName("positions")]
    public List<string> Positions { get; init; } = [];
}
