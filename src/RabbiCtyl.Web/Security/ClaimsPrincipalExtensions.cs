using System.Security.Claims;

namespace RabbiCtyl.Web.Security;

public static class ClaimsPrincipalExtensions
{
    public const string BootstrapAdminClaim = "bootstrap_admin";

    public static bool IsBootstrapAdmin(this ClaimsPrincipal principal) =>
        principal.HasClaim(BootstrapAdminClaim, "true");

    public static Guid? GetUserId(this ClaimsPrincipal principal)
    {
        var value = principal.FindFirstValue(ClaimTypes.NameIdentifier);

        return Guid.TryParse(value, out var id) ? id : null;
    }

    public static string GetDisplayName(this ClaimsPrincipal principal) =>
        principal.FindFirstValue("display_name")
        ?? principal.Identity?.Name
        ?? "User";
}
