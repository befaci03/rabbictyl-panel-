using Microsoft.AspNetCore.Http;

namespace RabbiCtyl.Web;

public static class ThemeLayouts
{
    public static async Task<string> ResolveAsync(
        HttpContext context,
        bool auth,
        CancellationToken cancellationToken = default)
    {
        var theme = await ThemeSelection.ResolveAsync(context, cancellationToken);

        return (theme?.Manifest.Layout?.Trim().ToLowerInvariant()) switch
        {
            "pterodactyl" => auth ? "_PterodactylAuthLayout" : "_PterodactylLayout",
            _ => auth ? "_AuthLayout" : "_Layout",
        };
    }
}
