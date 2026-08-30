namespace Kneset.Core.Entities;

/// <summary>Где обсуждали законопроект.</summary>
public enum BillSessionKind
{
    /// <summary>Заседание комиссии (KNS_CommitteeSession).</summary>
    Committee = 1,

    /// <summary>Заседание пленума (KNS_PlenumSession).</summary>
    Plenum = 2
}

/// <summary>
/// Одно появление законопроекта в повестке заседания — комиссии или пленума.
/// Вместе такие записи и образуют историю стадий, которой в KNS_Bill нет:
/// там лежит только текущий статус без даты, когда он наступил.
///
/// В источнике это две пары сущностей: KNS_CmtSessionItem → KNS_CommitteeSession
/// и KNS_PlmSessionItem → KNS_PlenumSession. Связь по ItemID = BillID при
/// ItemTypeID = 2 («הצעת חוק» в KNS_ItemType). Здесь они сведены в одну таблицу
/// с денормализованной датой заседания: остальные свойства заседания — комиссия,
/// место, ссылка на протокол — проекту пока не нужны, а две почти пустые таблицы
/// ради них заводить незачем. Понадобятся — вынесутся отдельно.
/// </summary>
public class BillSession
{
    public int Id { get; set; }

    public int BillId { get; set; }
    public Bill Bill { get; set; } = null!;

    public BillSessionKind Kind { get; set; }

    /// <summary>Идентификатор заседания в системе Кнессета.</summary>
    public int KnessetSessionId { get; set; }

    /// <summary>
    /// Начало заседания. Бывает в будущем: Кнессет публикует повестку заранее,
    /// и это единственный источник настоящего срока «до заседания N дней».
    /// </summary>
    public DateTime StartDate { get; set; }

    /// <summary>
    /// Стадия, в которой законопроект стоял на этом заседании (KNS_Status).
    /// Может быть пустой: не у каждого пункта повестки статус проставлен.
    /// </summary>
    public int? StatusId { get; set; }

    /// <summary>
    /// Название стадии на иврите. Денормализовано, как и Bill.StatusDesc:
    /// справочник KNS_Status живёт в памяти синхронизации и в базу не пишется,
    /// а join ради восьмидесяти строк на каждую отрисовку карточки не нужен.
    /// </summary>
    public string? StatusDesc { get; set; }
}
