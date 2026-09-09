namespace Kneset.Core.Entities;

/// <summary>
/// Подзаконный акт, меняющий закон: приказ министра из «קובץ התקנות».
///
/// Отдельная таблица, а не признак у <see cref="LawAmendment"/>, и это
/// главное решение здесь. У поправки есть смысл: строка — связка
/// из KNS_LawBinding со своим ключом, а у подзаконного акта такой связки
/// нет вовсе. Но важнее другое: сложи их вместе — и каждый существующий
/// запрос придётся обвешать условием «только законы», а счётчик косвенных
/// поправок останется верным ровно до первого забытого места. На этом
/// счётчике держится весь раздел, поэтому ошибку сделали невозможной,
/// а не сторожевой.
///
/// Откуда: страница закона на сайте Кнессета отдаёт историю изменений
/// целиком, а открытые данные — только акты Кнессета. Различаем
/// по серии публикации, а не по своей догадке: «ספר החוקים» — закон,
/// «קובץ תקנות» — подзаконный акт.
///
/// Почему это стоит показывать: суммы, которые человек чувствует, —
/// пособие, штраф, порог — меняются именно приказом, а не законом.
/// И рычаг там другой: в комиссию Кнессета по такому поводу не напишешь.
/// </summary>
public class LawRegulation
{
    public int Id { get; set; }

    public int IsraelLawId { get; set; }
    public IsraelLaw IsraelLaw { get; set; } = null!;

    /// <summary>Идентификатор акта у Кнессета — по нему upsert.</summary>
    public int KnessetActId { get; set; }

    public string Name { get; set; } = "";

    /// <summary>Прямое или косвенное изменение — разметка Кнессета, как у поправок.</summary>
    public bool IsIndirect { get; set; }

    /// <summary>Серия публикации: как правило «קובץ תקנות».</summary>
    public string? PublicationSeries { get; set; }

    public string? MagazineNumber { get; set; }

    public string? PageNumber { get; set; }

    public DateTime? PublicationDate { get; set; }

    public string? DocumentUrl { get; set; }

    public DateTime FetchedAt { get; set; }
}
