using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using RabbiCtyl.Core.Abstractions;

namespace RabbiCtyl.Web;

public static class ThemeLayouts
{
    public static async Task<string> ResolveAsync(
        HttpContext context,
        bool auth,
        CancellationToken cancellationToken = default)
    {
        var themeState = context.RequestServices.GetRequiredService<IThemeState>();
        var themes = context.RequestServices.GetRequiredService<IThemeCatalog>();
        var theme = await themes.FindAsync(themeState.ActiveThemeId, cancellationToken);

        return (theme?.Manifest.Layout?.Trim().ToLowerInvariant()) switch
        {
            "pterodactyl" => auth ? "_PterodactylAuthLayout" : "_PterodactylLayout",
            _ => auth ? "_AuthLayout" : "_Layout",
        };
    }
}
