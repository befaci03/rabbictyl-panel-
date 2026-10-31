using System.Security.Cryptography;
using System.Text;
using Rabbictyl.Core.Abstractions;

namespace Rabbictyl.Web.Middleware;

/// <summary>
/// Authenticates pats' callbacks using the Pterodactyl format
/// <c>Authorization: Bearer &lt;daemon_token_id&gt;.&lt;daemon_token&gt;</c>.
/// </summary>
public sealed class DaemonAuthenticationMiddleware
{
    public const string NodeItemKey = "Rabbictyl.DaemonNode";

    private readonly RequestDelegate _next;

    public DaemonAuthenticationMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext context, INodeStore nodes)
    {
        if (!context.Request.Path.StartsWithSegments("/api/remote"))
        {
            await _next(context);
            return;
        }

        var header = context.Request.Headers.Authorization.ToString();

        if (!header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            await RejectAsync(context, StatusCodes.Status401Unauthorized, "The required authorization headers were not present in the request.");
            return;
        }

        var credential = header["Bearer ".Length..].Trim();
        var separator = credential.IndexOf('.');

        if (separator <= 0 || separator == credential.Length - 1)
        {
            await RejectAsync(context, StatusCodes.Status400BadRequest, "The Authorization header provided was not in a valid format.");
            return;
        }

        var tokenId = credential[..separator];
        var token = credential[(separator + 1)..];
        var node = await nodes.FindByDaemonTokenIdAsync(tokenId, context.RequestAborted);

        if (node is null || !Matches(node.DaemonToken, token))
        {
            await RejectAsync(context, StatusCodes.Status403Forbidden, "You are not authorized to access this resource.");
            return;
        }

        context.Items[NodeItemKey] = node;
        await _next(context);
    }

    private static bool Matches(string expected, string provided) =>
        CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(expected),
            Encoding.UTF8.GetBytes(provided));

    private static async Task RejectAsync(HttpContext context, int statusCode, string message)
    {
        context.Response.StatusCode = statusCode;
        context.Response.ContentType = "application/json";

        await context.Response.WriteAsJsonAsync(new { error = message });
    }
}
