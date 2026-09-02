using Kneset.Core.Models;

namespace Kneset.Infrastructure.Ai;

/// <summary>
/// Структурный отпечаток разбора и его сверка между языками.
///
/// Политика (§6) требует, чтобы содержание анализа было одинаковым на всех
/// языках: недопустимо смягчать или усиливать выводы в зависимости от языка
/// аудитории. Пока переводом занималась одна модель, это можно было принимать
/// на веру. Когда переводят разные модели разных вендоров — уже нет.
///
/// Сверяется то, что перевод менять не вправе: сколько пунктов в каждом
/// разделе, какие у них типы утверждений, сколько затронутых прав и с какими
/// эффектами. Сам текст, естественно, различается — его и переводят.
/// </summary>
public static class AnalysisShape
{
    public record Fingerprint(
        int Benefits, int Risks, int For, int Against,
        int Groups, int Questions, int Rights,
        string Kinds, string Effects);

    public static Fingerprint Of(BillAnalysisResult a) => new(
        a.PotentialBenefits.Count,
        a.PotentialRisks.Count,
        a.ArgumentsFor.Count,
        a.ArgumentsAgainst.Count,
        a.AffectedGroups.Count,
        a.OpenQuestions.Count,
        a.RightsImpact?.AffectedRights.Count ?? 0,
        // Порядок пунктов перевод сохраняет, поэтому последовательность типов
        // сравнивается как есть: расхождение означает, что пункт подменён,
        // добавлен или потерян.
        string.Join(",", AllPoints(a).Select(p => p.Kind)),
        string.Join(",", a.RightsImpact?.AffectedRights.Select(r => r.Effect) ?? []));

    private static IEnumerable<AnalysisPoint> AllPoints(BillAnalysisResult a) =>
        a.PotentialBenefits
            .Concat(a.PotentialRisks)
            .Concat(a.ArgumentsFor)
            .Concat(a.ArgumentsAgainst);

    /// <summary>
    /// Чем перевод расходится с оригиналом по структуре. Пустая строка —
    /// расхождений нет.
    /// </summary>
    public static string Diff(BillAnalysisResult source, BillAnalysisResult translation)
    {
        var a = Of(source);
        var b = Of(translation);
        if (a == b) return "";

        var parts = new List<string>();
        void Check(string name, object x, object y)
        {
            if (!Equals(x, y)) parts.Add($"{name}: {x} → {y}");
        }

        Check("выгоды", a.Benefits, b.Benefits);
        Check("риски", a.Risks, b.Risks);
        Check("за", a.For, b.For);
        Check("против", a.Against, b.Against);
        Check("группы", a.Groups, b.Groups);
        Check("вопросы", a.Questions, b.Questions);
        Check("права", a.Rights, b.Rights);
        if (a.Kinds != b.Kinds) parts.Add("изменились типы утверждений");
        if (a.Effects != b.Effects) parts.Add("изменились эффекты для прав");

        return string.Join("; ", parts);
    }
}
