namespace Kneset.Core.Models;

/// <summary>Входные данные для AI-анализа законопроекта.</summary>
public record BillAnalysisRequest
{
    public required int BillId { get; init; }
    public required string NameHebrew { get; init; }
    public string? SubTypeDesc { get; init; }
    public string? StatusDesc { get; init; }
    public int KnessetNum { get; init; }
    public string? SummaryLaw { get; init; }

    /// <summary>Полный текст законопроекта, если доступен.</summary>
    public string? FullText { get; init; }

    /// <summary>Язык результата (ru/he/ar/en).</summary>
    public string LanguageCode { get; init; } = "ru";
}
