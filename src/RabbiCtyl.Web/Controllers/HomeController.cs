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
    private readonly IUserStore _users;
    private readonly IServerStore _servers;
    private readonly INodeStore _nodes;

    public HomeController(IUserStore users, IServerStore servers, INodeStore nodes)
    {
        _users = users;
        _servers = servers;
        _nodes = nodes;
    }

    [HttpGet("/")]
    public async Task<IActionResult> Index(CancellationToken cancellationToken)
    {
        var userId = User.GetUserId();
        var servers = userId is null
            ? Array.Empty<Server>()
            : await _servers.ListForUserAsync(userId.Value, cancellationToken);

        var model = new DashboardViewModel
        {
            ServerCount = servers.Count,
            NodeCount = await _nodes.CountAsync(cancellationToken),
            UserCount = await _users.CountAsync(cancellationToken),
            Servers = servers,
        };

        return View(model);
    }

    [AllowAnonymous]
    [HttpGet("/error")]
    public IActionResult Error() => View();
}
