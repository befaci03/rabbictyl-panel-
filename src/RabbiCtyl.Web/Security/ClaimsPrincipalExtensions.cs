using System.Security.Claims;

namespace RabbiCtyl.Web.Security;

public static class ClaimsPrincipalExtensions
{
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
