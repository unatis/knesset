namespace Kneset.Web.Services;

/// <summary>
/// Склонение существительного при числе. Формы лежат в одной строке ресурса
/// через «|», потому что держать их отдельными ключами на четыре языка —
/// это вчетверо больше строк и вчетверо больше шансов забыть одну из них.
///
/// Одна форма — язык без согласования: «יום». Две — единственное и множественное:
/// «day|days». Три — русское правило: «день|дня|дней».
/// </summary>
public static class Plural
{
    public static string Of(int count, string forms)
    {
        var parts = forms.Split('|');

        return parts.Length switch
        {
            0 => forms,
            1 => parts[0],
            2 => parts[count == 1 ? 0 : 1],
            _ => parts[RussianIndex(count)]
        };
    }

    /// <summary>«1 день», «2 дня», «5 дней», «11 дней», «21 день».</summary>
    private static int RussianIndex(int count)
    {
        var n = Math.Abs(count);
        var mod100 = n % 100;
        var mod10 = n % 10;

        if (mod100 is >= 11 and <= 14) return 2;
        if (mod10 == 1) return 0;
        if (mod10 is >= 2 and <= 4) return 1;
        return 2;
    }
}
