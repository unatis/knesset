namespace Kneset.Core.Entities;

/// <summary>
/// Разбор действующего закона — по строке на язык, как у законопроектов.
///
/// Отдельная таблица, а не признак у <see cref="BillAnalysis"/>, по той же
/// причине, что и у подзаконных актов: у разборов законопроектов есть
/// очередь, показ и счётчики, и подмешивать в них другую сущность значит
/// проверять «а это точно законопроект?» в каждом запросе.
///
/// Устаревание привязано к ревизии текста, а не к дате. Текст мы копируем
/// из «ספר החוקים הפתוח», и там его правят: пришла новая ревизия — разбор
/// сделан по прежнему тексту, и это надо видеть, а не считать по времени.
/// </summary>
public class IsraelLawAnalysis
{
    public int Id { get; set; }

    public int IsraelLawId { get; set; }
    public IsraelLaw IsraelLaw { get; set; } = null!;

    /// <summary>Язык разбора: en — мастер, остальные получены переводом.</summary>
    public string LanguageCode { get; set; } = "en";

    public string AnalysisJson { get; set; } = "";

    /// <summary>Модель, которая действительно сделала эту запись.</summary>
    public string ModelVersion { get; set; } = "";

    /// <summary>Ревизия текста, по которой сделан разбор.</summary>
    public long SourceRevision { get; set; }

    public DateTime GeneratedAt { get; set; }

    /// <summary>Текст закона с тех пор изменился — разбор говорит о прежней редакции.</summary>
    public bool IsStale { get; set; }
}
