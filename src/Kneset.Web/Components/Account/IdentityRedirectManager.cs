using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Identity;
using Kneset.Core.Entities;
using Kneset.Infrastructure.Data;

namespace Kneset.Web.Components.Account;

internal sealed class IdentityRedirectManager(NavigationManager navigationManager)
{
    public const string StatusCookieName = "Identity.StatusMessage";

    private static readonly CookieBuilder StatusCookieBuilder = new()
    {
        SameSite = SameSiteMode.Strict,
        HttpOnly = true,
        IsEssential = true,
        MaxAge = TimeSpan.FromSeconds(5),
    };

    public void RedirectTo(string? uri)
    {
        uri ??= "";

        // Prevent open redirects.
        if (!Uri.IsWellFormedUriString(uri, UriKind.Relative))
        {
            uri = navigationManager.ToBaseRelativePath(uri);
        }

        navigationManager.NavigateTo(uri);
    }

    public void RedirectTo(string uri, Dictionary<string, object?> queryParameters)
    {
        var uriWithoutQuery = navigationManager.ToAbsoluteUri(uri).GetLeftPart(UriPartial.Path);
        var newUri = navigationManager.GetUriWithQueryParameters(uriWithoutQuery, queryParameters);
        RedirectTo(newUri);
    }

    public void RedirectToWithStatus(string uri, string message, HttpContext context)
    {
        context.Response.Cookies.Append(StatusCookieName, message, StatusCookieBuilder.Build(context));
        RedirectTo(uri);
    }

    private string CurrentPath => navigationManager.ToAbsoluteUri(navigationManager.Uri).GetLeftPart(UriPartial.Path);

    public void RedirectToCurrentPage() => RedirectTo(CurrentPath);

    public void RedirectToCurrentPageWithStatus(string message, HttpContext context)
        => RedirectToWithStatus(CurrentPath, message, context);

    public void RedirectToInvalidUser(UserManager<AppUser> userManager, HttpContext context)
        // Идентификатор пользователя в сообщении не нужен: посетителю он
        // ничего не говорит, а на экран попадал как есть. Текст — из ресурсов,
        // метка неудачи — чтобы плашка была красной на любом языке.
        => RedirectToWithStatus(
            "Account/InvalidUser",
            Shared.StatusMessage.Error(Validation.ValidationLocalizer.Get("Auth_InvalidUserNote")),
            context);
}
