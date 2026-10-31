using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Rabbictyl.Core.Abstractions;
using Rabbictyl.Core.Extensions;
using Rabbictyl.Web.Models;

namespace Rabbictyl.Web.Controllers;

[ApiController]
[Route("api/themes")]
public sealed class ThemesApiController : ControllerBase
{
    private readonly IThemeCatalog _themes;
    private readonly IThemeState _themeState;

    public ThemesApiController(IThemeCatalog themes, IThemeState themeState)
    {
        _themes = themes;
        _themeState = themeState;
    }

    [AllowAnonymous]
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<ThemeDto>>> List(CancellationToken cancellationToken)
    {
        var themes = await _themes.ListAsync(cancellationToken);

        return Ok(themes.Select(ToDto).ToList());
    }

    [AllowAnonymous]
    [HttpGet("{id}")]
    public async Task<ActionResult<ThemeDto>> Get(string id, CancellationToken cancellationToken)
    {
        var theme = await _themes.FindAsync(id, cancellationToken);

        return theme is null ? NotFound() : Ok(ToDto(theme));
    }

    [AllowAnonymous]
    [HttpGet("{id}/assets/{assetId}")]
    public async Task<IActionResult> Asset(string id, string assetId, CancellationToken cancellationToken)
    {
        var asset = await _themes.FindAssetAsync(id, assetId, cancellationToken);

        return asset is null
            ? NotFound()
            : PhysicalFile(asset.FullPath, asset.ContentType, asset.FileName);
    }

    [Authorize(Roles = "admin")]
    [HttpPost("{id}/activate")]
    public async Task<IActionResult> Activate(string id, CancellationToken cancellationToken)
    {
        await _themeState.ActivateAsync(id, cancellationToken);

        return NoContent();
    }

    private static ThemeDto ToDto(ThemeDescriptor theme) => new(
        theme.Id,
        theme.Name,
        theme.Manifest.Version,
        theme.Manifest.Description,
        theme.Manifest.Author,
        theme.StylesheetUrls,
        theme.ScriptUrls,
        theme.AssetUrls);
}
