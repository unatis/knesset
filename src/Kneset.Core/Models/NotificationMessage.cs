namespace Kneset.Core.Models;

/// <summary>
/// Готовое к отправке сообщение. Текст уже собран на языке получателя —
/// каналы ничего не переводят и не локализуют.
/// </summary>
public record NotificationMessage
{
    /// <summary>Куда слать: email, chat_id, номер телефона.</summary>
    public string Address { get; init; } = "";

    /// <summary>Язык, на котором собран текст (he/ar/ru/en) — для заголовков письма.</summary>
    public string LanguageCode { get; init; } = "ru";

    /// <summary>Тема — используется каналами, где она есть (почта).</summary>
    public string Subject { get; init; } = "";

    public string Body { get; init; } = "";

    /// <summary>Абсолютная ссылка на страницу законопроекта.</summary>
    public string Url { get; init; } = "";
}
