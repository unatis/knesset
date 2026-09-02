namespace Kneset.Core.Entities;

/// <summary>
/// Текст документа законопроекта, извлечённый из файла.
///
/// Отдельная таблица, а не колонка в BillDocuments: тексты нужны редко —
/// поиску и AI-анализу, — а список документов на странице закона грузится
/// всегда. Колонкой она тянула бы мегабайты в каждый запрос списка и
/// требовала бы вечной дисциплины с проекциями в EF. Та же причина, по
/// которой отдельной сделана <see cref="BillTitle"/>.
/// </summary>
public class BillDocumentText
{
    public int Id { get; set; }

    public int BillDocumentId { get; set; }
    public BillDocument BillDocument { get; set; } = null!;

    public string Text { get; set; } = "";

    /// <summary>
    /// Длина текста. Дублирует Text.Length намеренно: позволяет считать
    /// объём и находить подозрительно короткие извлечения, не вычитывая
    /// сам текст из базы.
    /// </summary>
    public int CharCount { get; set; }

    /// <summary>
    /// Чем разобрано. Парсеры будем улучшать — в частности, PDF на иврите
    /// требует восстановления bidi-порядка, — и версия позволяет
    /// переразобрать старое, не трогая остальное.
    /// </summary>
    public string ExtractorVersion { get; set; } = "";

    /// <summary>
    /// SHA-256 исходного файла. Кнессет правит документы по ходу
    /// законодательного процесса; расхождение хеша означает, что текст
    /// устарел и файл надо разобрать заново.
    /// </summary>
    public string SourceHash { get; set; } = "";

    /// <summary>Размер исходного файла в байтах.</summary>
    public int SourceBytes { get; set; }

    public DateTime ExtractedAt { get; set; }

    /// <summary>
    /// Итог разбора: ok | empty | unsupported | error. Неудачу тоже
    /// сохраняем — иначе обходчик будет вечно возвращаться к тем же
    /// файлам, которые не разбираются в принципе (сканы, PPT, битые).
    /// </summary>
    public string Status { get; set; } = "";

    public string? Error { get; set; }
}
