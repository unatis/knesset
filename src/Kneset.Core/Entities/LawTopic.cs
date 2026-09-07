namespace Kneset.Core.Entities;

/// <summary>
/// Тема закона по рубрикатору Кнессета (источник: KNS_IsraelLawClassificiation,
/// опечатка в самом наборе).
///
/// Своей классификации мы не придумываем — и это главное в этой таблице.
/// Рубрикатор ведёт Кнессет: 51 тема, у закона обычно две-три. Читателю
/// нужна тема, чтобы найти своё («здравоохранение», «налоги») и подписаться
/// на неё, а нам важно, чтобы за темой стоял источник, а не наша догадка.
///
/// Название темы храним на иврите как оно пришло; переводы живут
/// в ресурсах ключами LawTopic_{TopicId} — там их видно переводчику
/// рядом с остальным интерфейсом.
/// </summary>
public class LawTopic
{
    public int Id { get; set; }

    /// <summary>Внешний идентификатор связки (LawClassificiationID).</summary>
    public int KnessetClassificationId { get; set; }

    public int IsraelLawId { get; set; }
    public IsraelLaw IsraelLaw { get; set; } = null!;

    /// <summary>Идентификатор темы в рубрикаторе Кнессета — ключ перевода.</summary>
    public int TopicId { get; set; }

    /// <summary>Название темы на иврите, как пришло из источника.</summary>
    public string NameHe { get; set; } = "";

    public DateTime LastUpdatedDate { get; set; }
}
