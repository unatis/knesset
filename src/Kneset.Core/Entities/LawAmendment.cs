namespace Kneset.Core.Entities;

/// <summary>
/// Связь принятого акта с законом, который он затрагивает (источник: KNS_LawBinding).
/// Кнессет сам различает прямую и косвенную поправку — именно это различие
/// делает запись ценной, см. <see cref="IsIndirect"/>.
/// </summary>
public class LawAmendment
{
    public int Id { get; set; }

    /// <summary>Внешний идентификатор связки (LawBindingID).</summary>
    public int KnessetBindingId { get; set; }

    /// <summary>Закон, который затронут.</summary>
    public int IsraelLawId { get; set; }
    public IsraelLaw IsraelLaw { get; set; } = null!;

    /// <summary>Принятый акт, который вносит изменение (KNS_Law.LawID).</summary>
    public int KnessetLawId { get; set; }

    /// <summary>Название акта — подтягивается из KNS_Law.</summary>
    public string? ActName { get; set; }

    public DateTime? ActPublicationDate { get; set; }

    /// <summary>Тип связи: «оригинальный закон» либо «изменяющий».</summary>
    public string? BindingTypeDesc { get; set; }

    /// <summary>Тип поправки: прямая (ישיר) или косвенная (עקיף).</summary>
    public string? AmendmentTypeDesc { get; set; }

    /// <summary>
    /// Косвенная поправка — когда акт про одну тему меняет закон про другую.
    /// Распространённый способ провести изменение незаметно, поэтому выделяется
    /// в интерфейсе отдельно. Признак берётся из разметки самого Кнессета.
    /// </summary>
    public bool IsIndirect { get; set; }

    /// <summary>Связь описывает не поправку, а сам факт создания закона.</summary>
    public bool IsOriginal { get; set; }

    /// <summary>
    /// Публикация акта в «Рэумот» и ссылка на неё. Приходит не из OData,
    /// а со страницы закона на сайте Кнессета: в открытых данных этого нет,
    /// а старых актов (идентификаторы вида 159524) нет и в KNS_Law вообще —
    /// именно поэтому у части поправок у нас не было даже названия.
    ///
    /// Серия, номер сборника и страница — официальная ссылка на закон:
    /// «ספר החוקים 1559, עמ' 52». Своего номера у израильского закона нет,
    /// ссылаются именно на публикацию.
    /// </summary>
    public string? PublicationSeries { get; set; }

    public string? MagazineNumber { get; set; }

    public string? PageNumber { get; set; }

    /// <summary>PDF публикации на fs.knesset.gov.il, если он есть.</summary>
    public string? DocumentUrl { get; set; }

    public DateTime LastUpdatedDate { get; set; }
}
