using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using RabbiCtyl.Core.Abstractions;

namespace RabbiCtyl.Web.Middleware;

/// <summary>
/// Authenticates Rabbit API (RAPI) keys on <c>/api/application</c> requests.
/// Keys are sent as <c>Authorization: Bearer rbtl_{identifier}.{secret}</c>, so
/// bots and external tooling can call the application API without a cookie.
/// Cookie sessions keep working when no key is present.
/// </summary>
public sealed class ApplicationApiKeyMiddleware
{
    public const string TokenPrefix = "rbtl_";
    private const string BearerPrefix = "Bearer ";

    private readonly RequestDelegate _next;

    public ApplicationApiKeyMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext context, IApiKeyStore keys)
    {
        if (!context.Request.Path.StartsWithSegments("/api/application"))
        {
            await _next(context);
            return;
        }

        var header = context.Request.Headers.Authorization.ToString();

        if (!header.StartsWith(BearerPrefix + TokenPrefix, StringComparison.OrdinalIgnoreCase))
        {
            await _next(context);
            return;
        }

        var credential = header[(BearerPrefix.Length + TokenPrefix.Length)..].Trim();
        var separator = credential.IndexOf('.');

        if (separator <= 0 || separator == credential.Length - 1)
        {
            await RejectAsync(context, StatusCodes.Status401Unauthorized, "The API key is malformed.");
            return;
        }

        var identifier = credential[..separator];
        var secret = credential[(separator + 1)..];
        var key = await keys.FindAsync(identifier, context.RequestAborted);

        if (key is null || key.ExpiresAt is { } expiresAt && expiresAt <= DateTimeOffset.UtcNow)
        {
            await RejectAsync(context, StatusCodes.Status401Unauthorized, "The API key is not valid.");
            return;
        }

        var hash = Hash(secret);

        if (!CryptographicOperations.FixedTimeEquals(
                Encoding.ASCII.GetBytes(hash),
                Encoding.ASCII.GetBytes(key.TokenHash)))
        {
            await RejectAsync(context, StatusCodes.Status403Forbidden, "You are not authorized to access this resource.");
            return;
        }

        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, key.UserId.ToString()),
            new Claim(ClaimTypes.Name, key.Identifier),
            new Claim(ClaimTypes.Role, "admin"),
            new Claim("display_name", key.Memo ?? key.Identifier),
        };

        context.User = new ClaimsPrincipal(
            new ClaimsIdentity(claims, authenticationType: "RapiKey"));

        await _next(context);
    }

    public static string Hash(string secret) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(secret))).ToLowerInvariant();

    private static async Task RejectAsync(HttpContext context, int statusCode, string message)
    {
        context.Response.StatusCode = statusCode;
        context.Response.ContentType = "application/json";

        await context.Response.WriteAsJsonAsync(new { error = message });
    }
}
