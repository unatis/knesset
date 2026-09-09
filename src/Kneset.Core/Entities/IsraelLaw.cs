namespace Kneset.Core.Entities;

/// <summary>
/// Действующий закон Израиля (источник: KNS_IsraelLaw). Это не законопроект:
/// законопроект — предложение, а здесь свод того, что уже принято и действует.
/// </summary>
public class IsraelLaw
{
    public int Id { get; set; }

    /// <summary>Внешний идентификатор в системе Кнессета (IsraelLawID).</summary>
    public int KnessetIsraelLawId { get; set; }

    public string Name { get; set; } = "";

    /// <summary>Созыв, при котором закон принят.</summary>
    public int? KnessetNum { get; set; }

    /// <summary>Основной закон — часть конституционного каркаса Израиля.</summary>
    public bool IsBasicLaw { get; set; }

    public bool IsBudgetLaw { get; set; }

    /// <summary>Статус действия: в силе, отменён и т.п.</summary>
    public string? ValidityDesc { get; set; }

    public DateTime? PublicationDate { get; set; }
    public DateTime? ValidityStartDate { get; set; }
    public DateTime? ValidityFinishDate { get; set; }

    public DateTime LastUpdatedDate { get; set; }

    /// <summary>Акты, которые этот закон меняли или которыми он был создан.</summary>
    public List<LawAmendment> Amendments { get; set; } = [];

    /// <summary>
    /// Сводный действующий текст закона — «ספר החוקים הפתוח» в Викитеке.
    /// Ссылку даёт сайт Кнессета; в открытых данных текста нет вовсе.
    /// </summary>
    public string? OpenBookUrl { get; set; }

    /// <summary>Объяснение простыми словами на «כל זכות», если оно есть.</summary>
    public string? KolZchutUrl { get; set; }

    /// <summary>Ответственные министерства — строкой, как отдаёт сайт.</summary>
    public string? Ministries { get; set; }

    /// <summary>
    /// Что именно мы забрали со страницы в прошлый раз. Меняется, когда шаг
    /// начинает собирать больше, — тогда законы переспрашиваются сами,
    /// и не нужен разовый сброс отметки в миграции.
    /// </summary>
    public string? SiteDataVersion { get; set; }

    /// <summary>Когда последний раз спрашивали сайт Кнессета об этом законе.</summary>
    public DateTime? SiteFetchedAt { get; set; }

    /// <summary>Подзаконные акты — отдельно от поправок, см. LawRegulation.</summary>
    public List<LawRegulation> Regulations { get; set; } = [];

    /// <summary>Разборы по языкам — отдельно от разборов законопроектов.</summary>
    public List<IsraelLawAnalysis> Analyses { get; set; } = [];

    /// <summary>Копия сводного текста, если он у нас есть.</summary>
    public IsraelLawText? FullText { get; set; }

    /// <summary>Темы по рубрикатору Кнессета. Своих не придумываем.</summary>
    public List<LawTopic> Topics { get; set; } = [];

    /// <summary>Переводы названия. Оригинал — в Name.</summary>
    public List<IsraelLawTitle> Titles { get; set; } = [];
}
