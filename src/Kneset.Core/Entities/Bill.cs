namespace Kneset.Core.Entities;

/// <summary>Законопроект Кнессета (источник: KNS_Bill).</summary>
public class Bill
{
    public int Id { get; set; }

    /// <summary>Внешний ID в системе Кнессета (BillID).</summary>
    public int KnessetBillId { get; set; }

    /// <summary>Название на иврите.</summary>
    public string Name { get; set; } = "";

    /// <summary>Название на русском (заполняется AI-анализом).</summary>
    public string? NameRu { get; set; }

    public int? StatusId { get; set; }
    public string? StatusDesc { get; set; }

    /// <summary>Номер созыва Кнессета.</summary>
    public int KnessetNum { get; set; }

    /// <summary>Тип законопроекта (правительственный, частный и т.д.).</summary>
    public string? SubTypeDesc { get; set; }

    public int? Number { get; set; }
    public DateTime? PublicationDate { get; set; }

    /// <summary>Дата последнего обновления в системе Кнессета.</summary>
    public DateTime LastUpdatedDate { get; set; }

    /// <summary>
    /// Когда синхронизация впервые увидела этот законопроект. Именно это, а не
    /// PublicationDate из Кнессета, означает «новый для нас»: при первом наполнении
    /// базы прилетает вся история за два созыва.
    /// </summary>
    public DateTime FirstSeenAt { get; set; }

    /// <summary>Когда в последний раз менялась стадия (StatusId). null — не менялась.</summary>
    public DateTime? StatusChangedAt { get; set; }

    /// <summary>Краткое описание закона (если есть в источнике).</summary>
    public string? SummaryLaw { get; set; }

    public List<BillInitiator> Initiators { get; set; } = [];
    public List<BillAnalysis> Analyses { get; set; } = [];
    public List<BillReaction> Reactions { get; set; } = [];
}
