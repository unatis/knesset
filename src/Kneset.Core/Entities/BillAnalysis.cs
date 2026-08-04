namespace Kneset.Core.Entities;

/// <summary>Сохранённый AI-анализ законопроекта. AnalysisJson хранится в jsonb.</summary>
public class BillAnalysis
{
    public int Id { get; set; }

    public int BillId { get; set; }
    public Bill Bill { get; set; } = null!;

    /// <summary>Структурированный анализ (сериализованный BillAnalysisResult).</summary>
    public string AnalysisJson { get; set; } = "";

    /// <summary>Версия модели/провайдера, сгенерировавшей анализ.</summary>
    public string ModelVersion { get; set; } = "";

    public string LanguageCode { get; set; } = "ru";

    public DateTime GeneratedAt { get; set; }

    /// <summary>Законопроект изменился после генерации — анализ устарел.</summary>
    public bool IsStale { get; set; }

    /// <summary>LastUpdatedDate законопроекта на момент генерации.</summary>
    public DateTime BillLastUpdatedAt { get; set; }
}
