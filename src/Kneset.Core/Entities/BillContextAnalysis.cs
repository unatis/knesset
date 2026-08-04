namespace Kneset.Core.Entities;

/// <summary>
/// Сохранённый контекстный анализ («Контекст и интерпретации») законопроекта.
/// Отдельная сущность от BillAnalysis: другой жанр (интерпретация, не факты),
/// другая схема, другие правила показа.
/// </summary>
public class BillContextAnalysis
{
    public int Id { get; set; }

    public int BillId { get; set; }
    public Bill Bill { get; set; } = null!;

    /// <summary>Сериализованный BillContextResult, jsonb.</summary>
    public string ContextJson { get; set; } = "";

    public string ModelVersion { get; set; } = "";
    public string LanguageCode { get; set; } = "ru";
    public DateTime GeneratedAt { get; set; }
    public bool IsStale { get; set; }
}
