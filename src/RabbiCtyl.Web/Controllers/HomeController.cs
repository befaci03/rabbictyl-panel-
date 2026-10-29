using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RabbiCtyl.Core.Abstractions;
using RabbiCtyl.Core.Entities;
using RabbiCtyl.Web.Models;
using RabbiCtyl.Web.Security;

namespace RabbiCtyl.Web.Controllers;

[Authorize]
public sealed class HomeController : Controller
{
    private readonly IServerStore _servers;
    private readonly IUserStore _users;

    public HomeController(IServerStore servers, IUserStore users)
    {
        _servers = servers;
        _users = users;
    }

    [HttpGet("/")]
    public async Task<IActionResult> Index(
        string? scope = null,
        CancellationToken cancellationToken = default)
    {
        if (User.IsBootstrapAdmin())
        {
            return RedirectToAction("Setup", "Auth");
        }

        var showAllServers = User.IsInRole("admin")
            && string.Equals(scope, "all", StringComparison.OrdinalIgnoreCase);

        IReadOnlyList<Server> servers;
        var ownerNames = new Dictionary<Guid, string>();

        if (showAllServers)
        {
            servers = await _servers.ListAsync(cancellationToken);
            ownerNames = (await _users.ListAsync(cancellationToken))
                .ToDictionary(user => user.Id, user => user.DisplayName);
        }
        else
        {
            var userId = User.GetUserId();
            servers = userId is null
                ? Array.Empty<Server>()
                : await _servers.ListForUserAsync(userId.Value, cancellationToken);
        }

        var model = new DashboardViewModel
        {
            Servers = servers,
            ShowAllServers = showAllServers,
            OwnerNames = ownerNames,
        };

        return View(model);
    }

    [AllowAnonymous]
    [HttpGet("/error")]
    public IActionResult Error() => View();
}
