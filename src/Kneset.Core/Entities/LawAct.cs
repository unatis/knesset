namespace Kneset.Core.Entities;

/// <summary>
/// Принятый законодательный акт — то, что публикуется в «Реумот» (источник: KNS_Law).
/// Хранится отдельной таблицей ради инкрементальной синхронизации: актов больше
/// шестидесяти тысяч, тянуть их целиком при каждом прогоне недопустимо.
/// </summary>
public class LawAct
{
    public int Id { get; set; }

    /// <summary>Внешний идентификатор акта (LawID).</summary>
    public int KnessetLawId { get; set; }

    public string Name { get; set; } = "";

    public DateTime? PublicationDate { get; set; }

    public DateTime LastUpdatedDate { get; set; }
}
