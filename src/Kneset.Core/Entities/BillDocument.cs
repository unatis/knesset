namespace Kneset.Core.Entities;

/// <summary>
/// Файл законопроекта на сайте Кнессета — сам текст с пояснительной запиской.
///
/// Содержания у нас до сих пор не было вовсе: KNS_Bill отдаёт заголовок, тип,
/// стадию и почти всегда пустой SummaryLaw. Текст живёт отдельной сущностью
/// KNS_DocumentBill, по файлу на формат: один и тот же документ приходит
/// дважды, DOC и PDF, с общим DocumentBillID.
///
/// Храним ссылку, а не файл. Раздавать чужой документ со своего адреса значит
/// брать на себя его свежесть и подменять первоисточник копией; ни того,
/// ни другого проекту не нужно. Встроить их файл в страницу всё равно нельзя:
/// fs.knesset.gov.il отдаёт PDF с X-Frame-Options SAMEORIGIN и разрешает
/// frame-ancestors только доменам Кнессета.
/// </summary>
public class BillDocument
{
    public int Id { get; set; }

    public int BillId { get; set; }
    public Bill Bill { get; set; } = null!;

    /// <summary>
    /// Идентификатор документа в системе Кнессета. Общий у форматов одного
    /// документа, поэтому сам по себе ключом быть не может — только вместе
    /// с форматом.
    /// </summary>
    public int KnessetDocumentId { get; set; }

    /// <summary>
    /// Что это за документ: «הצעת חוק לדיון מוקדם», «נוסח לקריאה ראשונה»
    /// и подобное. На иврите, как и остальные подписи из источника.
    /// </summary>
    public string? GroupTypeDesc { get; set; }

    /// <summary>Формат файла: PDF, DOC и прочее, как его называет источник.</summary>
    public string Format { get; set; } = "";

    public string Url { get; set; } = "";

    public DateTime LastUpdatedDate { get; set; }
}
