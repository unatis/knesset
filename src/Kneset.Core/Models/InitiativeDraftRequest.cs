namespace Kneset.Core.Models;

/// <summary>Входные данные для AI-структурирования гражданской инициативы.</summary>
public record InitiativeDraftRequest
{
    public required int InitiativeId { get; init; }
    public required string Title { get; init; }
    public required string ProblemDescription { get; init; }
    public required string ProposedSolution { get; init; }
    public string LanguageCode { get; init; } = "ru";
}
