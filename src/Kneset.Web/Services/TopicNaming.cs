using Microsoft.Extensions.Localization;

namespace Kneset.Web.Services;

/// <summary>
/// Название темы рубрикатора Кнессета на языке интерфейса.
///
/// Переводы лежат в ресурсах ключами LawTopic_{id}, а не в базе: тем
/// пятьдесят одна, они меняются раз в годы, и переводчику полезно видеть
/// их рядом с остальным интерфейсом. Если темы в ресурсах нет — значит,
/// Кнессет добавил новую, и до перевода показываем иврит из источника,
/// а не пустое место.
/// </summary>
public static class TopicNaming
{
    public static string Localized(IStringLocalizer<SharedResource> localizer, int topicId, string nameHe)
    {
        var value = localizer[$"LawTopic_{topicId}"];
        return value.ResourceNotFound ? nameHe : value.Value;
    }
}
