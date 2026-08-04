namespace Kneset.Core.Entities;

public enum InitiativeStatus
{
    /// <summary>Черновик — видит только автор.</summary>
    Draft = 0,

    /// <summary>Опубликована — открыта для подписей.</summary>
    Published = 1,

    /// <summary>Порог подписей достигнут — пакет готов для передачи депутатам.</summary>
    ThresholdReached = 2
}

/// <summary>
/// Гражданская законодательная инициатива. Юридически НЕ официальный законопроект —
/// платформа маркирует это явно; официально закон вносят правительство/депутаты/комиссии.
/// </summary>
public class CitizenInitiative
{
    public int Id { get; set; }

    public string Title { get; set; } = "";

    /// <summary>Какую проблему решает инициатива (описание автора).</summary>
    public string ProblemDescription { get; set; } = "";

    /// <summary>Предлагаемое решение (описание автора).</summary>
    public string ProposedSolution { get; set; } = "";

    public string AuthorId { get; set; } = "";
    public AppUser Author { get; set; } = null!;

    public InitiativeStatus Status { get; set; }

    /// <summary>AI-структура (сериализованный InitiativeDraftResult), jsonb.</summary>
    public string? StructuredJson { get; set; }

    /// <summary>Версия модели/провайдера AI-структуры.</summary>
    public string? ModelVersion { get; set; }

    /// <summary>Порог подписей, зафиксированный при создании.</summary>
    public int SignatureThreshold { get; set; }

    public DateTime CreatedAt { get; set; }
    public DateTime? PublishedAt { get; set; }

    public List<InitiativeSignature> Signatures { get; set; } = [];
}
