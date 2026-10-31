using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Rabbictyl.Configuration.Options;
using Rabbictyl.Infrastructure.Extensions;

namespace Rabbictyl.Web.Controllers;

/// <summary>
/// Serves the endpoints addons own at <c>/addons/{id}/api/...</c>. The panel is
/// deliberately anonymous here: an addon may need to answer before a session
/// exists (sign-in) and is responsible for its own access checks. Addons are
/// administrator-installed code.
/// </summary>
[AllowAnonymous]
[Route("addons/{id}/api")]
public sealed class AddonEndpointController : Controller
{
    private readonly AddonHost _addons;
    private readonly PanelOptions _options;

    public AddonEndpointController(AddonHost addons, PanelOptions options)
    {
        _addons = addons;
        _options = options;
    }

    [HttpGet("{**path}")]
    public Task<IActionResult> Get(string id, string? path, CancellationToken cancellationToken) =>
        HandleAsync(id, path, cancellationToken);

    [HttpPost("{**path}")]
    public Task<IActionResult> Post(string id, string? path, CancellationToken cancellationToken) =>
        HandleAsync(id, path, cancellationToken);

    private async Task<IActionResult> HandleAsync(string id, string? path, CancellationToken cancellationToken)
    {
        var request = await AddonRequestFactory.CreateAsync(
            HttpContext,
            path ?? string.Empty,
            null,
            _options,
            cancellationToken);

        var response = await _addons.HandleEndpointAsync(id, request, cancellationToken);

        if (response is null)
        {
            return NotFound();
        }

        if (response.Redirect is { Length: > 0 } redirect)
        {
            return redirect.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                   redirect.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
                ? Redirect(redirect)
                : LocalRedirect(redirect);
        }

        if (response.Body is { } body)
        {
            return File(body, response.ContentType ?? "application/octet-stream");
        }

        if (response.Html is { } html)
        {
            return Content(html, response.ContentType ?? "text/html; charset=utf-8");
        }

        return StatusCode(response.StatusCode);
    }
}
