namespace Kneset.Core.Entities;

/// <summary>
/// Курируемый слой поверх фракции: партии, из которых она составлена,
/// и её происхождение.
///
/// Почему это отдельная таблица, а не поля у депутата: партий в API
/// Кнессета нет вообще. Проверено по метаданным `ParliamentInfo.svc` —
/// у `KNS_Faction` только идентификатор, имя, созыв и даты, а сущности
/// партии не существует. Этот слой ведётся вручную из файла-источника
/// `Seed/faction-parties.json`, поэтому у каждой записи стоит источник
/// и дата сверки: он заведомо неполон, и честнее это показать.
///
/// Чего здесь нет нарочно: названия фракции (оно уже переведено на четыре
/// языка ключами `Faction_&lt;id&gt;`) и числа мандатов (оно меняется при
/// каждом расколе и считается из состава депутатов).
/// </summary>
public class Faction
{
    /// <summary>FactionID Кнессета. Свой ключ не заводим — этот стабилен.</summary>
    public int Id { get; set; }

    /// <summary>Название на иврите: для сверки файла с данными Кнессета.</summary>
    public string NameHe { get; set; } = "";

    /// <summary>single | joint | split — как устроен состав фракции.</summary>
    public string OriginKind { get; set; } = "single";

    /// <summary>
    /// Ключ группы связанных фракций (например Split2024). Фракции с одним
    /// ключом рисуются на карте рядом и соединяются скобой; подпись группы
    /// берётся из ресурсов по ключу `FactionGroup_&lt;ключ&gt;`.
    /// </summary>
    public string? GroupKey { get; set; }

    /// <summary>Дата события, породившего группу.</summary>
    public DateOnly? GroupDate { get; set; }

    /// <summary>Порядок внутри группы; вне группы не используется.</summary>
    public int GroupOrdinal { get; set; }

    /// <summary>Откуда взяты сведения о партийном составе.</summary>
    public string? Source { get; set; }

    public DateOnly? VerifiedAt { get; set; }

    public List<FactionParty> Parties { get; set; } = [];
}

/// <summary>Партия в составе фракции. Мандатов по партиям в данных нет.</summary>
public class FactionParty
{
    public int Id { get; set; }

    public int FactionId { get; set; }
    public Faction Faction { get; set; } = null!;

    public int Ordinal { get; set; }

    public string NameHe { get; set; } = "";

    /// <summary>
    /// Русское написание. Остальные языки появятся тем же путём, что
    /// и переводы названий законопроектов; пока их нет, показывается иврит.
    /// </summary>
    public string? NameRu { get; set; }
}
