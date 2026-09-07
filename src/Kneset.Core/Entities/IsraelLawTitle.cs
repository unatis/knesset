namespace Kneset.Core.Entities;

/// <summary>
/// Название действующего закона на языке интерфейса.
///
/// Устроено по образцу <see cref="BillTitle"/>, и по той же причине:
/// оригинал живёт в IsraelLaw.Name и остаётся первой строкой везде.
/// Название закона в Израиле — формула, на которую ссылаются другие законы
/// и суды, и ставить машинный перевод выше неё нельзя.
///
/// Отдельная таблица, а не колонки по языкам: строка на язык добавляется
/// без миграции.
/// </summary>
public class IsraelLawTitle
{
    public int Id { get; set; }

    public int IsraelLawId { get; set; }
    public IsraelLaw IsraelLaw { get; set; } = null!;

    /// <summary>Код языка: ru, en, ar. Иврит не хранится — он в IsraelLaw.Name.</summary>
    public string LanguageCode { get; set; } = "";

    public string Text { get; set; } = "";

    /// <summary>Чем переведено: отличить ручной перевод от машинного и один провайдер от другого.</summary>
    public string ModelVersion { get; set; } = "";

    /// <summary>
    /// Название закона в момент перевода. Кнессет правит формулировки —
    /// сравнение с текущим IsraelLaw.Name показывает, что перевод устарел.
    /// </summary>
    public string SourceName { get; set; } = "";

    public DateTime GeneratedAt { get; set; }
}
