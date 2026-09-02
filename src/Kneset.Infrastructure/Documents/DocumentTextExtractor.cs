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
    /// Версии парсеров — по одной на формат, а не одна общая. Иначе правка
    /// разбора PDF заставила бы переразобрать все одиннадцать с половиной
    /// тысяч docx, то есть скачать их заново без всякой пользы.
    /// </summary>
    public const string DocxVersion = "openxml-v2";
    public const string PdfVersion = "pdfbidi-v1";

    /// <summary>
    /// Все актуальные версии. Обходчик считает документ разобранным, если
    /// его версия есть в этом списке.
    /// </summary>
    public static readonly string[] CurrentVersions = [DocxVersion, PdfVersion];

    public static string VersionFor(Kind kind) => kind switch
    {
        Kind.Docx => DocxVersion,
        Kind.Pdf => PdfVersion,
        // Неподдерживаемые типы тоже помечаем версией: иначе обходчик будет
        // возвращаться к ним при каждом проходе.
        _ => DocxVersion,
    };

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
                Kind.Docx => new Result(kind, Sanitize(ExtractDocx(bytes)), null),
                Kind.Pdf => new Result(kind, Sanitize(ExtractPdf(bytes)), null),
                _ => new Result(kind, "", $"тип {kind} не поддерживается"),
            };
        }
        catch (Exception ex)
        {
            return new Result(kind, "", ex.GetType().Name + ": " + ex.Message);
        }
    }

    /// <summary>
    /// Чистка текста перед записью в базу.
    ///
    /// Postgres не хранит нулевой байт в text: попытка записи валит запрос
    /// с «invalid byte sequence for encoding UTF8: 0x00», причём падает вся
    /// партия, а не один документ. Ноль приходит из PDF — так выглядит глиф
    /// без сопоставленного символа. Заодно убираем прочие управляющие
    /// символы (кроме табуляции и перевода строки) и непарные суррогаты:
    /// смысла они не несут, а сломать запись могут.
    /// </summary>
    private static string Sanitize(string text)
    {
        var sb = new System.Text.StringBuilder(text.Length);
        for (var i = 0; i < text.Length; i++)
        {
            var ch = text[i];

            if (ch is '\t' or '\n') { sb.Append(ch); continue; }
            if (ch == '\r') continue;                       // \r\n → \n
            if (char.IsControl(ch)) continue;

            if (char.IsHighSurrogate(ch))
            {
                if (i + 1 < text.Length && char.IsLowSurrogate(text[i + 1]))
                {
                    sb.Append(ch).Append(text[i + 1]);
                    i++;
                }
                continue;                                   // непарный — выбрасываем
            }
            if (char.IsLowSurrogate(ch)) continue;          // непарный

            sb.Append(ch);
        }
        return sb.ToString();
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

    /// <summary>
    /// Текст из PDF с восстановлением логического порядка.
    ///
    /// page.Text использовать нельзя: он отдаёт глифы в порядке отрисовки,
    /// и для иврита строка выходит наизнанку и без пробелов между словами.
    /// Поэтому идём от глифов с координатами: собираем строки по базовой
    /// линии, внутри строки читаем справа налево, а пробелы восстанавливаем
    /// по горизонтальным зазорам между глифами.
    /// </summary>
    private static string ExtractPdf(byte[] bytes)
    {
        using var doc = PdfDocument.Open(bytes);

        var pages = doc.GetPages().Select(PageText).Where(t => t.Length > 0);
        return string.Join("\n", pages);
    }

    private static string PageText(UglyToad.PdfPig.Content.Page page)
    {
        var letters = page.Letters;
        if (letters.Count == 0) return "";

        // Направление определяем по странице целиком, а не по строке: короткая
        // строка из одних цифр иначе получила бы своё, неверное направление.
        var rtl = letters.Count(l => IsRtl(l.Value)) * 2 > letters.Count(l => IsLetterish(l.Value));

        var avgHeight = letters.Average(l => l.GlyphRectangle.Height);
        var avgWidth = letters.Average(l => l.GlyphRectangle.Width);
        var lineTolerance = Math.Max(avgHeight * 0.5, 0.5);

        // Группируем по базовой линии, а не по низу прямоугольника глифа:
        // у букв с нижним выносным элементом (ן ץ ף ק) низ ниже остальных,
        // и по нему они отрывались от своей строки в отдельную.
        var lines = new List<List<UglyToad.PdfPig.Content.Letter>>();
        foreach (var letter in letters.OrderByDescending(l => l.StartBaseLine.Y))
        {
            var current = lines.Count > 0 ? lines[^1] : null;
            if (current is null
                || Math.Abs(current[0].StartBaseLine.Y - letter.StartBaseLine.Y) > lineTolerance)
            {
                current = [];
                lines.Add(current);
            }
            current.Add(letter);
        }

        var sb = new System.Text.StringBuilder();
        foreach (var line in lines)
        {
            var ordered = rtl
                ? line.OrderByDescending(l => l.GlyphRectangle.Left).ToList()
                : line.OrderBy(l => l.GlyphRectangle.Left).ToList();

            var text = new System.Text.StringBuilder();
            for (var i = 0; i < ordered.Count; i++)
            {
                if (i > 0)
                {
                    // Зазор между соседними глифами в порядке чтения. В PDF
                    // пробел часто не рисуется вовсе — его выдаёт только
                    // расстояние. Порог считаем и от средней ширины по
                    // странице, и от ширины соседних глифов: слишком
                    // чувствительный порог рвал числа надвое, и «2021»
                    // превращалось в «1 202».
                    var prev = ordered[i - 1].GlyphRectangle;
                    var cur = ordered[i].GlyphRectangle;
                    var gap = rtl ? prev.Left - cur.Right : cur.Left - prev.Right;
                    var threshold = Math.Max(avgWidth, Math.Max(prev.Width, cur.Width)) * 0.55;
                    if (gap > threshold) text.Append(' ');
                }
                text.Append(ordered[i].Value);
            }

            var lineText = rtl ? FixLtrRuns(text.ToString()) : text.ToString();
            if (lineText.Trim().Length > 0) sb.AppendLine(lineText.TrimEnd());
        }

        return sb.ToString().TrimEnd();
    }

    /// <summary>
    /// Внутри RTL-строки цифры и латиница пишутся слева направо. После
    /// разворота строки справа налево такие вкрапления оказываются
    /// перевёрнутыми — «2026» становится «6202». Возвращаем их на место.
    /// Заодно зеркалим скобки: в визуальном порядке они противоположны
    /// логическому.
    /// </summary>
    private static string FixLtrRuns(string line)
    {
        var sb = new System.Text.StringBuilder(line.Length);
        var run = new List<char>();

        void Flush()
        {
            if (run.Count == 0) return;
            run.Reverse();
            sb.Append(run.ToArray());
            run.Clear();
        }

        foreach (var ch in line)
        {
            if (IsRtl(ch.ToString()) || ch == ' ')
            {
                Flush();
                sb.Append(Mirror(ch));
            }
            else
            {
                run.Add(Mirror(ch));
            }
        }
        Flush();
        return sb.ToString();
    }

    private static char Mirror(char ch) => ch switch
    {
        '(' => ')', ')' => '(',
        '[' => ']', ']' => '[',
        '{' => '}', '}' => '{',
        '<' => '>', '>' => '<',
        _ => ch,
    };

    private static bool IsRtl(string s) =>
        s.Length > 0 && s[0] is >= '֐' and <= 'ࣿ';

    private static bool IsLetterish(string s) =>
        s.Length > 0 && (char.IsLetter(s[0]) || char.IsDigit(s[0]));

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
