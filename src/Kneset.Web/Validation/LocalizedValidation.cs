using System.ComponentModel.DataAnnotations;
using Microsoft.Extensions.Localization;

namespace Kneset.Web.Validation;

/// <summary>
/// Доступ к локализатору из атрибутов валидации.
///
/// Внедрить зависимость в атрибут нельзя — их создаёт среда исполнения по
/// метаданным. Обычный для ASP.NET путь, AddDataAnnotationsLocalization, тоже
/// не подходит: он обслуживает MVC и Razor Pages, а формы здесь блейзоровские,
/// и DataAnnotationsValidator читает атрибуты напрямую. Путь через
/// ErrorMessageResourceType требует сгенерированного designer-класса ресурсов,
/// которого в проекте нет.
///
/// Поэтому статическая ссылка. Она безопасна: локализатор зарегистрирован
/// синглтоном, своего состояния не имеет, а язык берёт из CurrentUICulture —
/// то есть из текущего запроса, который выставила локализующая прослойка.
/// </summary>
public static class ValidationLocalizer
{
    private static IStringLocalizer<SharedResource>? _localizer;

    /// <summary>Вызывается один раз при старте приложения.</summary>
    public static void Use(IStringLocalizer<SharedResource> localizer) => _localizer = localizer;

    /// <summary>
    /// Если локализатор ещё не подключён (например, в тесте модели без хоста),
    /// возвращаем сам ключ: пустое сообщение хуже некрасивого.
    /// </summary>
    public static string Get(string key) => _localizer is null ? key : _localizer[key].Value;
}

/// <summary>Обязательное поле.</summary>
public sealed class LocalizedRequiredAttribute : RequiredAttribute
{
    public override string FormatErrorMessage(string name) => ValidationLocalizer.Get("Val_Required");
}

/// <summary>
/// Адрес электронной почты. Не наследник EmailAddressAttribute — тот sealed,
/// поэтому саму проверку делегируем ему же, а сообщение подменяем своим.
/// </summary>
public sealed class LocalizedEmailAddressAttribute : ValidationAttribute
{
    private static readonly EmailAddressAttribute Inner = new();

    public override bool IsValid(object? value) => Inner.IsValid(value);

    public override string FormatErrorMessage(string name) => ValidationLocalizer.Get("Val_Email");
}

/// <summary>
/// Длина строки. Сообщение получает границы подстановкой, а не именем поля:
/// у поля рядом есть подпись, повторять её в ошибке незачем.
/// </summary>
public sealed class LocalizedLengthAttribute(int maximumLength) : StringLengthAttribute(maximumLength)
{
    public override string FormatErrorMessage(string name) =>
        string.Format(ValidationLocalizer.Get("Val_Length"), MinimumLength, MaximumLength);
}

/// <summary>Совпадение с другим полем — здесь это подтверждение пароля.</summary>
public sealed class LocalizedCompareAttribute(string otherProperty) : CompareAttribute(otherProperty)
{
    /// <summary>Ключ ресурса с текстом ошибки.</summary>
    public string MessageKey { get; init; } = "Val_Mismatch";

    public override string FormatErrorMessage(string name) => ValidationLocalizer.Get(MessageKey);
}
