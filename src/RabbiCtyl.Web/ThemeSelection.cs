using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using RabbiCtyl.Core.Abstractions;
using RabbiCtyl.Core.Extensions;

namespace RabbiCtyl.Web;

/// <summary>
/// Theme choice belongs to the browser, not the panel. The selected id lives in
/// <c>localStorage</c>; a copy in a cookie lets the server render the matching
/// layout and logo on the first request. The panel default (administration →
/// <c>theme.json</c>) is only a fallback.
/// </summary>
public static class ThemeSelection
{
    public const string CookieName = "rabbictyl_theme";

    /// <summary>localStorage key the browser script reads and writes.</summary>
    public const string StorageKey = "rabbictyl.theme";

    public static async Task<ThemeDescriptor?> ResolveAsync(
        HttpContext context,
        CancellationToken cancellationToken = default)
    {
        var catalog = context.RequestServices.GetRequiredService<IThemeCatalog>();

        if (context.Request.Cookies[CookieName] is { Length: > 0 } chosen &&
            await catalog.FindAsync(chosen, cancellationToken) is { } selected)
        {
            return selected;
        }

        var state = context.RequestServices.GetRequiredService<IThemeState>();

        return await catalog.FindAsync(state.ActiveThemeId, cancellationToken);
    }
}
