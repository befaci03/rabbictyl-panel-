using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RabbiCtyl.Configuration.Options;
using RabbiCtyl.Infrastructure.Extensions;
using RabbiCtyl.Web.Models;

namespace RabbiCtyl.Web.Controllers;

/// <summary>
/// Hosts the administration pages addons contribute. The addon owns the page
/// markup and behaviour; the panel only provides the tab and the shell.
/// </summary>
[Authorize(Roles = "admin")]
[Route("admin/addons")]
public sealed class AdminAddonController : Controller
{
    private readonly AddonHost _addons;
    private readonly IAntiforgery _antiforgery;
    private readonly PanelOptions _options;

    public AdminAddonController(AddonHost addons, IAntiforgery antiforgery, PanelOptions options)
    {
        _addons = addons;
        _antiforgery = antiforgery;
        _options = options;
    }

    [HttpGet("{slug}")]
    public Task<IActionResult> Page(string slug, CancellationToken cancellationToken) =>
        HandleAsync(slug, cancellationToken);

    [HttpPost("{slug}")]
    public Task<IActionResult> Submit(string slug, CancellationToken cancellationToken) =>
        HandleAsync(slug, cancellationToken);

    private async Task<IActionResult> HandleAsync(string slug, CancellationToken cancellationToken)
    {
        var tab = _addons.FindAdminTab(slug);

        if (tab is null)
        {
            return NotFound();
        }

        var tokens = _antiforgery.GetAndStoreTokens(HttpContext);
        var request = await AddonRequestFactory.CreateAsync(
            HttpContext,
            string.Empty,
            tokens.RequestToken,
            _options,
            cancellationToken);

        var response = await _addons.HandleAdminAsync(slug, request, cancellationToken);

        if (response is null)
        {
            return NotFound();
        }

        if (response.Redirect is { Length: > 0 } redirect)
        {
            if (!string.IsNullOrWhiteSpace(response.Flash))
            {
                TempData["Success"] = response.Flash;
            }

            return LocalRedirect(redirect);
        }

        if (response.StatusCode >= 400)
        {
            return StatusCode(response.StatusCode);
        }

        ViewData["Title"] = tab.Label;
        ViewData["AddonSlug"] = slug;

        return View("~/Views/Admin/AddonPage.cshtml", new AdminAddonPageViewModel
        {
            Slug = slug,
            Label = tab.Label,
            Html = response.Html ?? string.Empty,
        });
    }
}
