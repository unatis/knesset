using System.Text.RegularExpressions;

namespace Kneset.Core.Legislation;

/// <summary>
/// Определяет, какой действующий закон правит законопроект.
///
/// Зачем это здесь вообще: Кнессет такой связи не публикует. `KNS_LawBinding`
/// связывает закон с уже **принятым** актом, а законопроект в работе актом
/// ещё не стал — то есть именно для тех законопроектов, на которые ещё можно
/// повлиять, авторитетной связи не существует.
///
/// Зато ивритское название законопроекта её содержит буквально:
/// «הצעת חוק הירושה (תיקון מס' 23), התשפ"ו-2026» — это поправка
/// к «חוק הירושה, התשכ"ה-1965». Отсюда разбор названия.
///
/// Замер по 1 158 законопроектам окна влияния: сопоставилось 765 (66 %),
/// затронуто 245 разных законов. Промахи в основном честные — это новые
/// законы, которые ничего не правят, и закона-предшественника у них нет
/// по определению.
///
/// Результат помечается как выведенный (`Bill.LawMatch`), чтобы его нигде
/// не выдавали за данные Кнессета.
/// </summary>
public static class BillLawMatcher
{
    /// <summary>Версия правил. Меняется вместе с ними — тогда видно, что пора пересчитать.</summary>
    public const string Version = "name-v2";

    private static readonly Regex Nikkud = new(@"[֑-ׇ]", RegexOptions.Compiled);
    private static readonly Regex Spaces = new(@"\s+", RegexOptions.Compiled);

    /// <summary>
    /// Год в конце названия: «, התשפ"ו-2026», «, תשכ"ה-1965», «, 1942».
    /// Убирается с обоих концов сопоставления — у закона год в названии есть,
    /// у законопроекта свой, и они разные.
    /// </summary>
    private static readonly Regex TrailingYear = new(
        @"[,\s]*(הת?ש[^\s,]*)?\s*-?\s*\d{4}\s*$", RegexOptions.Compiled);

    /// <summary>Хвостовая группа в скобках: «(תיקון מס' 23)», «(הוראת שעה)».</summary>
    private static readonly Regex TrailingParens = new(@"\s*\([^()]*\)\s*$", RegexOptions.Compiled);

    private static readonly Regex BillPrefix = new(@"^הצעת\s+", RegexOptions.Compiled);

    /// <summary>
    /// Квалификатор редакции в квадратных скобках: «[נוסח משולב]» (сводная
    /// редакция), «[התשס"א]» (год редакции). Убирается из ключа сопоставления,
    /// потому что законопроект его не пишет: правку сводного «חוק הביטוח
    /// הלאומי [נוסח משולב], התשנ"ה-1995» вносят как «הצעת חוק הביטוח הלאומי
    /// (תיקון מס' N)». Без этого ключ сходился с отменённой редакцией 1953
    /// года, и 57 законопроектов окна влияния указывали на недействующий
    /// закон. Какую из редакций выбрать, решает вызывающий: действующая
    /// вперёд, при равенстве — более поздняя.
    /// </summary>
    private static readonly Regex EditionQualifier = new(@"\s*\[[^\]]*\]\s*", RegexOptions.Compiled);

    /// <summary>«חוק לתיקון פקודת מס הכנסה» — законопроект правит ордонанс.</summary>
    private static readonly Regex AmendingLaw = new(@"^חוק לתיקון\s+(?<target>.+)$", RegexOptions.Compiled);

    /// <summary>Ключ сопоставления для названия закона.</summary>
    public static string LawKey(string lawName) => StripEdition(StripYear(Normalize(lawName)));

    /// <summary>
    /// Ключи-кандидаты для названия законопроекта, от самого длинного
    /// к самому короткому.
    ///
    /// Порядок важен: скобки бывают номером поправки, а бывают частью
    /// названия закона — «חוק הפיקוח על שירותים פיננסיים (שירותים
    /// פיננסיים מוסדרים)». Поэтому отбрасываем их по одной и пробуем
    /// сопоставление после каждого шага; первое совпадение и есть самое
    /// точное.
    /// </summary>
    public static IReadOnlyList<string> BillKeys(string billName)
    {
        var s = StripEdition(BillPrefix.Replace(StripYear(Normalize(billName)), ""));
        var keys = new List<string>();
        if (s.Length > 0) keys.Add(s);

        while (true)
        {
            var next = StripEdition(StripYear(TrailingParens.Replace(s, "").Trim().TrimEnd(',').Trim()));
            if (next.Length == 0 || next == s) break;
            keys.Add(next);
            s = next;
        }

        // Законопроект-«поправщик»: цель правки — то, что после «חוק לתיקון».
        foreach (var key in keys.ToList())
        {
            var m = AmendingLaw.Match(key);
            if (m.Success) keys.Add(m.Groups["target"].Value.Trim());
        }

        return keys;
    }

    private static string Normalize(string value)
    {
        var s = value
            .Replace('־', '-')   // мака́ф — ивритский дефис
            .Replace('–', '-')   // тире
            .Replace('״', '"')   // гершаим
            .Replace('׳', '\'');
        s = Nikkud.Replace(s, "");
        return Spaces.Replace(s, " ").Trim();
    }

    private static string StripEdition(string value) =>
        Spaces.Replace(EditionQualifier.Replace(value, " "), " ").Trim().TrimEnd(',').Trim();

    private static string StripYear(string value)
    {
        var s = value;
        while (true)
        {
            var next = TrailingYear.Replace(s, "").Trim().TrimEnd(',').Trim();
            if (next == s) return s;
            s = next;
        }
    }
}
