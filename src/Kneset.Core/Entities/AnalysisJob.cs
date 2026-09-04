namespace Kneset.Core.Entities;

/// <summary>
/// Захват работы по разбору: одна строка на пару (законопроект, шаг).
///
/// Зачем нужна отдельная таблица, а не уникальный индекс на BillAnalyses:
/// индекс запрещает вторую запись, но к моменту вставки за оба обращения
/// к модели уже заплачено. Деньги тратятся до записи, поэтому и защита
/// должна стоять до вызова модели.
///
/// Случай, который это лечит, наблюдался на законопроекте 42: дев и прод
/// работают с одной базой, оба экземпляра независимо проверили «свежего
/// перевода нет», оба увидели правду и оба перевели. В базе оказались две
/// свежие русские записи, оплаченные по отдельности.
/// </summary>
public class AnalysisJob
{
    public int Id { get; set; }

    public int BillId { get; set; }

    /// <summary>
    /// Шаг работы: язык перевода либо <see cref="MasterStep"/> для самого разбора.
    /// Разбор считается один раз на все языки, поэтому у него свой ключ:
    /// иначе запрос на русский и на арабский заказали бы два разбора.
    /// </summary>
    public string Step { get; set; } = "";

    /// <summary>running | done | failed</summary>
    public string State { get; set; } = "";

    public DateTime ClaimedAt { get; set; }

    /// <summary>Кто держит захват — чтобы в журнале было видно, какой экземпляр.</summary>
    public string ClaimedBy { get; set; } = "";

    public DateTime? FinishedAt { get; set; }

    /// <summary>Причина неудачи. Нужна не только для журнала: по ней страница
    /// может сказать, что именно не вышло, вместо глухого «недоступен».</summary>
    public string? Error { get; set; }

    public const string MasterStep = "master";
    public const string Running = "running";
    public const string Done = "done";
    public const string Failed = "failed";
}
