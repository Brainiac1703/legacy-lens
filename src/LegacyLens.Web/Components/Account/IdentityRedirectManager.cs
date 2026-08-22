using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Identity;
using LegacyLens.Persistence.EF.Entities;

namespace LegacyLens.Web.Components.Account;

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

    /// <summary>
    /// La severidad viaja como una letra al principio de la cookie —E de error,
    /// S de correcto— y no se deduce del texto.
    ///
    /// La plantilla la deducía comprobando si el mensaje empezaba por «Error».
    /// Eso deja de funcionar en cuanto los mensajes se traducen: «No se pudo
    /// guardar el teléfono» no empieza por Error, así que un fallo se pintaba en
    /// verde. La letra es invariante y no la ve nadie: se quita al leerla.
    /// </summary>
    public void RedirectToWithStatus(string uri, string message, HttpContext context, bool isError = false)
    {
        var conMarca = (isError ? 'E' : 'S') + message;
        context.Response.Cookies.Append(StatusCookieName, conMarca, StatusCookieBuilder.Build(context));
        RedirectTo(uri);
    }

    private string CurrentPath => navigationManager.ToAbsoluteUri(navigationManager.Uri).GetLeftPart(UriPartial.Path);

    public void RedirectToCurrentPage() => RedirectTo(CurrentPath);

    public void RedirectToCurrentPageWithStatus(string message, HttpContext context, bool isError = false)
        => RedirectToWithStatus(CurrentPath, message, context, isError);

    public void RedirectToInvalidUser(UserManager<ApplicationUser> userManager, HttpContext context)
        => RedirectToWithStatus("Account/InvalidUser", $"No se pudo cargar el usuario '{userManager.GetUserId(context.User)}'.", context, isError: true);
}
