using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Localization;

namespace Kneset.Web.Validation;

/// <summary>
/// Тексты ошибок ASP.NET Identity на языке посетителя.
///
/// Identity создаёт эти сообщения сам, глубоко внутри UserManager, и по
/// умолчанию они английские при любой культуре. Видно их в самых обычных
/// местах: правила пароля при регистрации и смене, занятая почта, протухшая
/// ссылка подтверждения. До этого класса человек, читающий сайт на иврите,
/// на этом месте упирался в английскую фразу.
///
/// Переопределены не все методы базового класса, а те, до которых посетитель
/// реально доходит. Остальные достаются от него и остаются английскими: это
/// внутренние сбои вроде рассинхронизации токенов, и придумывать им перевод
/// вслепую хуже, чем оставить как есть.
/// </summary>
public sealed class LocalizedIdentityErrorDescriber(IStringLocalizer<SharedResource> localizer)
    : IdentityErrorDescriber
{
    private IdentityError Error(string code, string key, params object[] args) => new()
    {
        Code = code,
        Description = args.Length == 0
            ? localizer[key].Value
            : string.Format(localizer[key].Value, args),
    };

    // Пароль
    public override IdentityError PasswordTooShort(int length) =>
        Error(nameof(PasswordTooShort), "IdErr_PasswordTooShort", length);

    public override IdentityError PasswordRequiresDigit() =>
        Error(nameof(PasswordRequiresDigit), "IdErr_PasswordRequiresDigit");

    public override IdentityError PasswordRequiresLower() =>
        Error(nameof(PasswordRequiresLower), "IdErr_PasswordRequiresLower");

    public override IdentityError PasswordRequiresUpper() =>
        Error(nameof(PasswordRequiresUpper), "IdErr_PasswordRequiresUpper");

    public override IdentityError PasswordRequiresNonAlphanumeric() =>
        Error(nameof(PasswordRequiresNonAlphanumeric), "IdErr_PasswordRequiresSymbol");

    public override IdentityError PasswordRequiresUniqueChars(int uniqueChars) =>
        Error(nameof(PasswordRequiresUniqueChars), "IdErr_PasswordRequiresUnique", uniqueChars);

    public override IdentityError PasswordMismatch() =>
        Error(nameof(PasswordMismatch), "IdErr_PasswordMismatch");

    // Почта и имя
    public override IdentityError DuplicateEmail(string email) =>
        Error(nameof(DuplicateEmail), "IdErr_DuplicateEmail", email);

    public override IdentityError InvalidEmail(string? email) =>
        Error(nameof(InvalidEmail), "IdErr_InvalidEmail");

    public override IdentityError DuplicateUserName(string userName) =>
        Error(nameof(DuplicateUserName), "IdErr_DuplicateUserName", userName);

    public override IdentityError InvalidUserName(string? userName) =>
        Error(nameof(InvalidUserName), "IdErr_InvalidUserName");

    // Ссылки из писем
    public override IdentityError InvalidToken() =>
        Error(nameof(InvalidToken), "IdErr_InvalidToken");

    // Состояние учётной записи
    public override IdentityError UserAlreadyHasPassword() =>
        Error(nameof(UserAlreadyHasPassword), "IdErr_AlreadyHasPassword");

    public override IdentityError LoginAlreadyAssociated() =>
        Error(nameof(LoginAlreadyAssociated), "IdErr_LoginAlreadyAssociated");

    public override IdentityError ConcurrencyFailure() =>
        Error(nameof(ConcurrencyFailure), "IdErr_Concurrency");
}
