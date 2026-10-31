using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Rabbictyl.Core.Abstractions;

namespace Rabbictyl.Web.Controllers;

[AllowAnonymous]
[Route("panel")]
public sealed class PanelController : Controller
{
    private readonly IPanelBrandingStore _branding;

    public PanelController(IPanelBrandingStore branding)
    {
        _branding = branding;
    }

    [HttpGet("logo")]
    public IActionResult Logo()
    {
        var path = _branding.LogoPath;

        if (path is null)
        {
            return NotFound();
        }

        var contentType = Path.GetExtension(path).ToLowerInvariant() switch
        {
            ".svg" => "image/svg+xml",
            ".png" => "image/png",
            ".jpg" or ".jpeg" => "image/jpeg",
            ".webp" => "image/webp",
            ".gif" => "image/gif",
            ".avif" => "image/avif",
            ".jxl" => "image/jxl",
            _ => "application/octet-stream",
        };

        return PhysicalFile(path, contentType);
    }
}
