using System.Text;
using System.Text.RegularExpressions;

namespace Kneset.Core.Legislation;

/// <summary>
/// Викиразметка страницы закона → читаемый текст.
///
/// Главное отличие от обычной чистки вики-текста: у шаблонов вроде
/// «{{ח:סעיף|1|עקרונות יסוד|...}}» содержимое и есть текст закона.
/// Выбрасывать шаблоны целиком нельзя — от статьи останется номер;
/// поэтому шаблон разворачивается в свои аргументы.
///
/// Проверено на пятнадцати основных законах: получается статья за статьёй
/// с номером и заголовком, подписи под законом и ссылка на публикацию.
/// </summary>
public static class WikitextCleaner
{
    /// <summary>Версия чистки. Меняется вместе с правилами — тогда видно, что пора перечитать.</summary>
    public const string Version = "wiki-v5";

    private static readonly Regex NoInclude = new(@"(?s)<noinclude>.*?</noinclude>", RegexOptions.Compiled);
    private static readonly Regex Ref = new(@"(?s)<ref[^>]*>.*?</ref>", RegexOptions.Compiled);
    private static readonly Regex SelfClosingRef = new(@"<ref[^>]*/>", RegexOptions.Compiled);
    private static readonly Regex Category = new(@"\[\[קטגוריה:[^\]]*\]\]", RegexOptions.Compiled);
    private static readonly Regex Template = new(@"\{\{([^{}]*)\}\}", RegexOptions.Compiled);
    private static readonly Regex WikiLink = new(@"\[\[([^\[\]|]*\|)?([^\[\]]*)\]\]", RegexOptions.Compiled);
    private static readonly Regex ExternalLink = new(@"\[(https?://\S+)\s+([^\]]*)\]", RegexOptions.Compiled);
    private static readonly Regex Html = new(@"<[^>]+>", RegexOptions.Compiled);
    private static readonly Regex Emphasis = new(@"'{2,}", RegexOptions.Compiled);
    private static readonly Regex Heading = new(@"^\s*={2,}\s*(.*?)\s*={2,}\s*$", RegexOptions.Compiled | RegexOptions.Multiline);
    private static readonly Regex Spaces = new(@"[ \t]{2,}", RegexOptions.Compiled);
    private static readonly Regex BlankLines = new(@"(\r?\n\s*){3,}", RegexOptions.Compiled);

    /// <summary>Служебные аргументы шаблонов: идентификатор закона, номер ревизии.</summary>
    private static readonly Regex NumericOnly = new(@"^\d{4,}$", RegexOptions.Compiled);

    /// <summary>
    /// Голый адрес в аргументе шаблона — это ссылка на публикацию в «Рэумот».
    /// В тексте закона она читается как мусор, а сами публикации уже показаны
    /// списком поправок, каждая со своей подписью и ссылкой.
    /// </summary>
    private static readonly Regex BareUrl = new(@"^https?://\S+$", RegexOptions.Compiled);

    public static string Clean(string wikitext)
    {
        if (string.IsNullOrWhiteSpace(wikitext)) return "";

        var text = wikitext;

        // Служебные шаблоны-символы. В MediaWiki «=» и «|» внутри аргумента
        // шаблона приходится экранировать через {{=}} и {{!}}, и в адресах
        // это встречается постоянно:
        // «...Download?path{{=}}HebrewVerdicts/...&fileName{{=}}...».
        // Разворачивать их как обычные шаблоны нельзя — аргументов у них нет,
        // и от знака равенства оставался пробел, после чего адрес переставал
        // быть адресом и уезжал в текст закона.
        text = text.Replace("{{=}}", "=").Replace("{{!}}", "|");

        text = NoInclude.Replace(text, " ");
        text = Ref.Replace(text, " ");
        text = SelfClosingRef.Replace(text, " ");
        text = Category.Replace(text, " ");

        // Шаблоны разворачиваем изнутри наружу: вложенность у страниц
        // законов доходит до нескольких уровней.
        for (var pass = 0; pass < 10; pass++)
        {
            var expanded = Template.Replace(text, m => ExpandTemplate(m.Groups[1].Value));
            if (expanded == text) break;
            text = expanded;
        }

        text = WikiLink.Replace(text, m => m.Groups[2].Value);
        text = ExternalLink.Replace(text, m => m.Groups[2].Value);
        text = Html.Replace(text, " ");
        text = text
            .Replace("&nbsp;", " ")
            .Replace("&quot;", "\"")
            .Replace("&lt;", "<")
            .Replace("&gt;", ">")
            .Replace("&amp;", "&");
        text = Emphasis.Replace(text, "");
        text = Heading.Replace(text, "$1");
        text = Spaces.Replace(text, " ");
        text = BlankLines.Replace(text, "\n\n");

        return CollapseRepeats(text).Trim();
    }

    /// <summary>
    /// Шаблон → его аргументы. Имя шаблона отбрасываем: оно про оформление,
    /// а не про закон. Числовые идентификаторы вроде «2000046» тоже:
    /// это внутренний ключ Кнессета, читателю он в тексте не нужен.
    /// </summary>
    private static string ExpandTemplate(string body)
    {
        var parts = body.Split('|');
        var kept = new List<string>();

        foreach (var part in parts.Skip(1))
        {
            var value = part.Trim();

            // Адрес проверяем до разбора именованных аргументов: в запросе
            // ссылки есть свои «=», и деление по первому из них превращало
            // адрес в обрывок, который проверку на адрес уже не проходил.
            // Так в тексте закона оставалась ссылка на решение суда
            // с пробелами вместо «=».
            if (value.Length == 0 || BareUrl.IsMatch(value)) continue;

            value = NamedArgument(value);
            if (value.Length == 0 || NumericOnly.IsMatch(value) || BareUrl.IsMatch(value)) continue;
            kept.Add(value);
        }

        // Шаблон ссылки хранит и адрес, и подпись: «{{ח:חיצוני|הכרזה על
        // הקמת מדינת ישראל|שבהכרזה על הקמת מדינת ישראל}}». После
        // разворачивания текст читался бы дважды, причём во второй раз
        // с предлогом, так что точное сравнение такое не поймает.
        // Поэтому убираем аргумент, целиком входящий в другой.
        if (kept.Count > 1)
        {
            kept = kept
                .Where(one => !kept.Any(other =>
                    other.Length > one.Length
                    && one.Length >= 4
                    && other.Contains(one, StringComparison.Ordinal)))
                .ToList();
        }

        return kept.Count == 0 ? " " : " " + string.Join(" ", kept) + " ";
    }

    /// <summary>
    /// «имя=значение» → значение. Именем считаем только то, что на имя
    /// похоже: без пробелов и косых черт. Иначе первое же «=» внутри
    /// значения разрезало бы его пополам.
    /// </summary>
    private static string NamedArgument(string value)
    {
        var eq = value.IndexOf('=');
        if (eq <= 0) return value;

        var name = value[..eq];
        if (name.Contains(' ') || name.Contains('/') || name.Contains(':')) return value;

        return value[(eq + 1)..].Trim();
    }

    /// <summary>
    /// Убирает удвоение подряд идущих одинаковых фрагментов.
    ///
    /// Шаблон ссылки на статью хранит и адрес, и подпись — «סעיף 1|סעיף 1», —
    /// и после разворачивания текст читается дважды. Сравниваем соседние
    /// слова окном: дешевле и надёжнее, чем разбирать каждый шаблон отдельно.
    /// </summary>
    private static string CollapseRepeats(string text)
    {
        var words = text.Split(' ');
        var result = new List<string>(words.Length);

        for (var i = 0; i < words.Length; i++)
        {
            var matched = false;

            // Окно от четырёх слов к двум: длинные повторы важнее коротких,
            // иначе «אין אין» съест законное повторение слова.
            for (var size = 4; size >= 2 && !matched; size--)
            {
                if (i + size * 2 > words.Length) continue;

                var first = words[i..(i + size)];
                var second = words[(i + size)..(i + size * 2)];
                if (!first.SequenceEqual(second)) continue;

                result.AddRange(first);
                i += size * 2 - 1;
                matched = true;
            }

            if (!matched) result.Add(words[i]);
        }

        return string.Join(' ', result);
    }
}
