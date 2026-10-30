using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc;
using RabbiCtyl.Core.Abstractions;
using RabbiCtyl.Core.Entities;
using RabbiCtyl.Core.Extensions;
using RabbiCtyl.Web.Models;
using RabbiCtyl.Web.Security;

namespace RabbiCtyl.Web.Controllers;

[Authorize]
[Route("account")]
public sealed class AccountController : Controller
{
    private readonly IUserStore _users;
    private readonly IPasswordHasher _passwordHasher;
    private readonly ILogger<AccountController> _logger;

    public AccountController(
        IUserStore users,
        IPasswordHasher passwordHasher,
        ILogger<AccountController> logger)
    {
        _users = users;
        _passwordHasher = passwordHasher;
        _logger = logger;
    }

    [HttpGet("")]
    public async Task<IActionResult> Index(CancellationToken cancellationToken)
    {
        if (User.IsBootstrapAdmin())
        {
            return RedirectToAction("Setup", "Auth");
        }

        var user = await CurrentUserAsync(cancellationToken);

        if (user is null)
        {
            return Challenge();
        }

        return View(new AccountViewModel
        {
            User = user,
            SshKeys = SshKeys.Parse(user),
        });
    }

    [HttpPost("password")]
    public async Task<IActionResult> ChangePassword(
        ChangePasswordForm form,
        CancellationToken cancellationToken)
    {
        var user = await CurrentUserAsync(cancellationToken);

        if (user is null)
        {
            return Challenge();
        }

        if (!ModelState.IsValid)
        {
            TempData["Error"] = "Check the password fields and try again.";

            return RedirectToAction(nameof(Index));
        }

        if (!_passwordHasher.Verify(form.CurrentPassword, user.PasswordHash))
        {
            TempData["Error"] = "Your current password is not correct.";

            return RedirectToAction(nameof(Index));
        }

        user.PasswordHash = _passwordHasher.Hash(form.NewPassword);
        await _users.UpdateAsync(user, cancellationToken);

        _logger.LogInformation("User '{Username}' changed their panel password.", user.Username);

        TempData["Success"] = "Your password was updated.";

        return RedirectToAction(nameof(Index));
    }

    [HttpPost("ssh-keys")]
    public async Task<IActionResult> AddSshKey(
        AddSshKeyForm form,
        CancellationToken cancellationToken)
    {
        var user = await CurrentUserAsync(cancellationToken);

        if (user is null)
        {
            return Challenge();
        }

        if (!ModelState.IsValid || SshKeys.Normalize(form.Key).Length == 0)
        {
            TempData["Error"] = "Paste a full SSH public key, for example ssh-ed25519 AAAA... user@host.";

            return RedirectToAction(nameof(Index));
        }

        user.SshKeysJson = SshKeys.Add(user, form.Key);
        await _users.UpdateAsync(user, cancellationToken);

        TempData["Success"] = "Authorized key added.";

        return RedirectToAction(nameof(Index));
    }

    [HttpPost("ssh-keys/remove")]
    public async Task<IActionResult> RemoveSshKey(
        string key,
        CancellationToken cancellationToken)
    {
        var user = await CurrentUserAsync(cancellationToken);

        if (user is null)
        {
            return Challenge();
        }

        user.SshKeysJson = SshKeys.Remove(user, key);
        await _users.UpdateAsync(user, cancellationToken);

        TempData["Success"] = "Authorized key removed.";

        return RedirectToAction(nameof(Index));
    }

    private async Task<User?> CurrentUserAsync(CancellationToken cancellationToken) =>
        User.GetUserId() is { } userId
            ? await _users.FindByIdAsync(userId, cancellationToken)
            : null;
}
