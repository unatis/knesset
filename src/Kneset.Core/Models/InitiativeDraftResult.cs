using System.Text.Json.Serialization;

namespace Kneset.Core.Models;

/// <summary>
/// Юридическая структура гражданской инициативы, подготовленная AI.
/// Эта же схема будет structured output при подключении Claude API.
/// </summary>
public record InitiativeDraftResult
{
    /// <summary>Цель инициативы, сформулированная юридически.</summary>
    [JsonPropertyName("goal")]
    public string Goal { get; init; } = "";

    /// <summary>Предлагаемые изменения (по пунктам).</summary>
    [JsonPropertyName("proposed_changes")]
    public List<string> ProposedChanges { get; init; } = [];

    /// <summary>Существующие законы, которые затрагивает инициатива.</summary>
    [JsonPropertyName("affected_laws")]
    public List<string> AffectedLaws { get; init; } = [];

    /// <summary>Пояснительная записка: зачем нужен закон, кого затрагивает.</summary>
    [JsonPropertyName("explanatory_note")]
    public string ExplanatoryNote { get; init; } = "";

    [JsonPropertyName("financial_impact")]
    public string? FinancialImpactNote { get; init; }

    [JsonPropertyName("social_impact")]
    public string? SocialImpactNote { get; init; }

    /// <summary>Возможные риски и возражения.</summary>
    [JsonPropertyName("risks")]
    public List<string> Risks { get; init; } = [];

    /// <summary>Пометка провайдера (например, «демо-данные»). null — обычный результат.</summary>
    [JsonPropertyName("provider_note")]
    public string? ProviderNote { get; init; }
}
