using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using UglyToad.PdfPig;

namespace Kneset.Infrastructure.Documents;

/// <summary>
/// Извлечение текста из документов Кнессета.
///
/// Метка Format в KNS_DocumentBill ненадёжна: файлы, помеченные «DOC», по факту
/// оказались .docx (ZIP/OpenXML), а не бинарным Word 97. Поэтому тип определяем
/// по сигнатуре, а не по метке.
///
/// Docx предпочтительнее PDF: в нём текст лежит логическим порядком, тогда как
/// извлечение иврита из PDF зависит от того, чем файл сгенерирован, и может
/// давать перевёрнутый порядок символов. Проверять это — задача HebrewOrder.
/// </summary>
public static class DocumentTextExtractor
{
    /// <summary>
    /// Версия парсера. Пишется вместе с текстом: когда разбор улучшится
    /// (в первую очередь PDF, которому нужен bidi-порядок), смена версии
    /// заставит обходчик переразобрать старое, не трогая остальное.
    /// </summary>
    public const string Version = "openxml-v2";

    public enum Kind { Unknown, Docx, Pdf, Ole2Doc, Rtf, Empty }

    public record Result(Kind Kind, string Text, string? Error)
    {
        public int CharCount => Text.Length;
    }

    /// <summary>Тип файла по сигнатуре первых байтов.</summary>
    public static Kind Sniff(ReadOnlySpan<byte> b) => b.Length < 8 ? Kind.Empty
        : b[0] == 0x50 && b[1] == 0x4B ? Kind.Docx      // ZIP — OpenXML
        : b[0] == 0x25 && b[1] == 0x50 ? Kind.Pdf       // %PDF
        : b[0] == 0xD0 && b[1] == 0xCF ? Kind.Ole2Doc   // Word 97
        : b[0] == 0x7B && b[1] == 0x5C ? Kind.Rtf       // {\rtf
        : Kind.Unknown;

    public static Result Extract(byte[] bytes)
    {
        var kind = Sniff(bytes);
        try
        {
            return kind switch
            {
                Kind.Docx => new Result(kind, ExtractDocx(bytes), null),
                Kind.Pdf => new Result(kind, ExtractPdf(bytes), null),
                _ => new Result(kind, "", $"тип {kind} не поддерживается"),
            };
        }
        catch (Exception ex)
        {
            return new Result(kind, "", ex.GetType().Name + ": " + ex.Message);
        }
    }

    private static string ExtractDocx(byte[] bytes)
    {
        using var stream = new MemoryStream(bytes, writable: false);
        using var doc = WordprocessingDocument.Open(stream, isEditable: false);

        var body = doc.MainDocumentPart?.Document.Body;
        if (body is null) return "";

        // По абзацам, а не InnerText целиком: InnerText склеивает весь документ
        // в одну строку без границ абзацев, и текст становится нечитаемым.
        var paragraphs = body.Descendants<Paragraph>()
            .Select(ParagraphText)
            .Select(t => t.Trim())
            .Where(t => t.Length > 0);

        return string.Join("\n", paragraphs);
    }

    /// <summary>
    /// Текст абзаца с сохранением разделителей.
    ///
    /// InnerText нельзя: он молча выбрасывает табуляции и переводы строк,
    /// а в шапке законопроекта Кнессета поля разделены именно табуляцией —
    /// и «חבר הכנסת» склеивалось с именем депутата в одно слово.
    /// </summary>
    private static string ParagraphText(Paragraph p)
    {
        var sb = new System.Text.StringBuilder();
        foreach (var node in p.Descendants())
        {
            switch (node)
            {
                case Text t: sb.Append(t.Text); break;
                case TabChar: sb.Append('\t'); break;
                case Break: sb.Append('\n'); break;
                case CarriageReturn: sb.Append('\n'); break;
            }
        }
        return sb.ToString();
    }

    private static string ExtractPdf(byte[] bytes)
    {
        using var doc = PdfDocument.Open(bytes);
        return string.Join("\n", doc.GetPages().Select(page => page.Text));
    }

    /// <summary>
    /// Проверка порядка символов в иврите: если извлечение отдало глифы
    /// в порядке отрисовки, а не логическом, строка выйдет наизнанку.
    ///
    /// Ищем фразу «הצעת חוק» (законопроект) и её обращение. Именно фразу,
    /// а не отдельное слово «חוק»: его обращение «קוח» встречается внутри
    /// обычных слов — например в «פיקוח» (надзор) — и одиночное слово даёт
    /// ложные срабатывания. Фраза длиннее и такой омонимии не имеет.
    /// </summary>
    public static (int Forward, int Reversed) HebrewOrder(string text)
        => (Count(text, "הצעת חוק"), Count(text, "קוח תעצה"));

    private static int Count(string haystack, string needle)
    {
        var n = 0;
        for (var i = haystack.IndexOf(needle, StringComparison.Ordinal); i >= 0;
             i = haystack.IndexOf(needle, i + needle.Length, StringComparison.Ordinal))
            n++;
        return n;
    }
}
