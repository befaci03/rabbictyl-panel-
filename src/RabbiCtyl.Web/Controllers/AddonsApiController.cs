using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RabbiCtyl.Core.Abstractions;
using RabbiCtyl.Core.Extensions;
using RabbiCtyl.Web.Models;

namespace RabbiCtyl.Web.Controllers;

[ApiController]
[Authorize(Roles = "admin")]
[Route("api/addons")]
public sealed class AddonsApiController : ControllerBase
{
    private readonly IAddonCatalog _addons;

    public AddonsApiController(IAddonCatalog addons)
    {
        _addons = addons;
    }

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<AddonDto>>> List(
        [FromQuery] bool enabledOnly = false,
        CancellationToken cancellationToken = default)
    {
        var addons = await _addons.ListAsync(enabledOnly, cancellationToken);

        return Ok(addons.Select(ToDto).ToList());
    }

    [HttpGet("{id}")]
    public async Task<ActionResult<AddonDto>> Get(string id, CancellationToken cancellationToken)
    {
        var addon = await _addons.FindAsync(id, cancellationToken);

        return addon is null ? NotFound() : Ok(ToDto(addon));
    }

    [HttpGet("{id}/assets/{assetId}")]
    public async Task<IActionResult> Asset(string id, string assetId, CancellationToken cancellationToken)
    {
        var asset = await _addons.FindAssetAsync(id, assetId, cancellationToken);

        return asset is null
            ? NotFound()
            : PhysicalFile(asset.FullPath, asset.ContentType, asset.FileName);
    }

    private static AddonDto ToDto(AddonDescriptor addon) => new(
        addon.Id,
        addon.Name,
        addon.Manifest.Version,
        addon.Manifest.Description,
        addon.Manifest.Author,
        addon.IsEnabled,
        addon.AssemblyPath is not null,
        addon.ScriptUrls,
        addon.AssetUrls);
}
