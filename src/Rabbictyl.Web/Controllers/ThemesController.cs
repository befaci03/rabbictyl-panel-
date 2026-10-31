using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Rabbictyl.Core.Abstractions;
using Rabbictyl.Web.Models;

namespace Rabbictyl.Web.Controllers;

[Authorize]
[Route("themes")]
public sealed class ThemesController : Controller
{
    private readonly IThemeCatalog _themes;
    private readonly IThemeState _themeState;

    public ThemesController(IThemeCatalog themes, IThemeState themeState)
    {
        _themes = themes;
        _themeState = themeState;
    }

    [HttpGet("")]
    public async Task<IActionResult> Index(CancellationToken cancellationToken)
    {
        var model = new ThemesViewModel
        {
            Themes = await _themes.ListAsync(cancellationToken),
            ActiveThemeId = _themeState.ActiveThemeId,
        };

        return View(model);
    }

    [Authorize(Roles = "admin")]
    [HttpPost("{id}/activate")]
    public async Task<IActionResult> Activate(string id, CancellationToken cancellationToken)
    {
        var theme = await _themes.FindAsync(id, cancellationToken);

        if (theme is null)
        {
            return NotFound();
        }

        await _themeState.ActivateAsync(theme.Id, cancellationToken);

        TempData["Success"] = $"Activated {theme.Name}.";

        return RedirectToAction(nameof(Index));
    }
}
