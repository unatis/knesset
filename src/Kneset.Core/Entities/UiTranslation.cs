namespace Kneset.Core.Entities;

/// <summary>
/// Перевод UI-строки. Основной источник локализации: DbBackedLocalizer читает отсюда
/// (с кэшем), а .resx-файлы служат значениями по умолчанию — сидер импортирует их
/// при старте, недостающие ключи берутся из файлов. Правка перевода в базе
/// не требует пересборки приложения.
/// </summary>
public class UiTranslation
{
    public int Id { get; set; }

    /// <summary>Ключ ресурса (как в .resx), напр. "Nav_Bills".</summary>
    public string Key { get; set; } = "";

    /// <summary>Двухбуквенный код языка: ru / en / he / ar.</summary>
    public string LanguageCode { get; set; } = "";

    public string Value { get; set; } = "";

    public DateTime UpdatedAt { get; set; }
}
