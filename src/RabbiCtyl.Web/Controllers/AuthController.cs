using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RabbiCtyl.Configuration.Options;
using RabbiCtyl.Core.Abstractions;
using RabbiCtyl.Core.Entities;
using RabbiCtyl.Web.Models;
using RabbiCtyl.Web.Security;

namespace RabbiCtyl.Web.Controllers;

[Route("auth")]
public sealed class AuthController : Controller
{
    private readonly IUserStore _users;
    private readonly IPasswordHasher _passwordHasher;
    private readonly BootstrapOptions _bootstrap;
    private readonly ILogger<AuthController> _logger;

    public AuthController(
        IUserStore users,
        IPasswordHasher passwordHasher,
        BootstrapOptions bootstrap,
        ILogger<AuthController> logger)
    {
        _users = users;
        _passwordHasher = passwordHasher;
        _bootstrap = bootstrap;
        _logger = logger;
    }

    [AllowAnonymous]
    [HttpGet("login")]
    public IActionResult Login(string? returnUrl = null)
    {
        if (User.Identity?.IsAuthenticated == true)
        {
            return User.IsBootstrapAdmin()
                ? RedirectToAction(nameof(Setup))
                : RedirectToAction("Index", "Home");
        }

        ViewData["ReturnUrl"] = returnUrl;

        return View(new LoginViewModel());
    }

    [AllowAnonymous]
    [HttpPost("login")]
    public async Task<IActionResult> Login(
        LoginViewModel model,
        string? returnUrl = null,
        CancellationToken cancellationToken = default)
    {
        ViewData["ReturnUrl"] = returnUrl;

        if (!ModelState.IsValid)
        {
            return View(model);
        }

        var user = await _users.FindByLoginAsync(model.Login, cancellationToken);

        if (user is null || !_passwordHasher.Verify(model.Password, user.PasswordHash))
        {
            ModelState.AddModelError(string.Empty, "Those credentials do not match our records.");
            _logger.LogWarning("Failed sign-in attempt for '{Login}'.", model.Login);

            return View(model);
        }

        var isBootstrapAdmin = IsBootstrapAccount(user);

        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new(ClaimTypes.Name, user.Username),
            new(ClaimTypes.Email, user.Email),
            new(ClaimTypes.Role, user.IsRootAdmin ? "admin" : "user"),
            new("display_name", user.DisplayName),
        };

        if (isBootstrapAdmin)
        {
            claims.Add(new Claim(ClaimsPrincipalExtensions.BootstrapAdminClaim, "true"));
        }

        var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);

        await HttpContext.SignInAsync(
            CookieAuthenticationDefaults.AuthenticationScheme,
            new ClaimsPrincipal(identity),
            new AuthenticationProperties
            {
                IsPersistent = model.RememberMe,
                ExpiresUtc = model.RememberMe
                    ? DateTimeOffset.UtcNow.AddDays(30)
                    : null,
            });

        _logger.LogInformation("User '{Username}' signed in.", user.Username);

        if (isBootstrapAdmin)
        {
            return RedirectToAction(nameof(Setup));
        }

        if (!string.IsNullOrWhiteSpace(returnUrl) && Url.IsLocalUrl(returnUrl))
        {
            return LocalRedirect(returnUrl);
        }

        return RedirectToAction("Index", "Home");
    }

    [Authorize]
    [HttpGet("setup")]
    public async Task<IActionResult> Setup(CancellationToken cancellationToken = default)
    {
        var current = await GetBootstrapAccountAsync(cancellationToken);

        if (current is null)
        {
            return await LeaveSetupAsync();
        }

        return View(new SetupViewModel());
    }

    [Authorize]
    [HttpPost("setup")]
    public async Task<IActionResult> Setup(
        SetupViewModel model,
        CancellationToken cancellationToken = default)
    {
        var current = await GetBootstrapAccountAsync(cancellationToken);

        if (current is null)
        {
            return await LeaveSetupAsync();
        }

        var username = model.Username.Trim();
        var email = model.Email.Trim();

        if (!string.IsNullOrWhiteSpace(_bootstrap.AdminUsername)
            && string.Equals(username, _bootstrap.AdminUsername.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            ModelState.AddModelError(
                nameof(model.Username),
                "Choose a username that is different from the bootstrap username.");
        }

        if (!string.IsNullOrWhiteSpace(_bootstrap.AdminEmail)
            && string.Equals(email, _bootstrap.AdminEmail.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            ModelState.AddModelError(
                nameof(model.Email),
                "Choose an email that is different from the bootstrap email.");
        }

        if (await _users.FindByLoginAsync(username, cancellationToken) is not null)
        {
            ModelState.AddModelError(nameof(model.Username), "That username or email is already in use.");
        }

        if (await _users.FindByLoginAsync(email, cancellationToken) is not null)
        {
            ModelState.AddModelError(nameof(model.Email), "That username or email is already in use.");
        }

        if (!ModelState.IsValid)
        {
            return View(model);
        }

        var admin = new User
        {
            Username = username,
            Email = email,
            NameFirst = model.FirstName.Trim(),
            NameLast = model.LastName.Trim(),
            PasswordHash = _passwordHasher.Hash(model.Password),
            IsRootAdmin = true,
        };

        await _users.AddAsync(admin, cancellationToken);

        try
        {
            await _users.DeleteAsync(current, cancellationToken);
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Could not remove the bootstrap administrator account.");
            await _users.DeleteAsync(admin, CancellationToken.None);
            throw;
        }

        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);

        _logger.LogInformation(
            "Created administrator '{Username}' and removed the bootstrap account.",
            admin.Username);

        TempData["Success"] = "Your administrator account is ready. Sign in with your new credentials.";

        return RedirectToAction(nameof(Login));
    }

    [Authorize]
    [HttpPost("logout")]
    public async Task<IActionResult> Logout()
    {
        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);

        return RedirectToAction(nameof(Login));
    }

    [AllowAnonymous]
    [HttpGet("denied")]
    public IActionResult Denied() => View();

    private bool IsBootstrapAccount(User user) =>
        user.IsRootAdmin
        && (!string.IsNullOrWhiteSpace(_bootstrap.AdminUsername)
            && string.Equals(user.Username, _bootstrap.AdminUsername.Trim(), StringComparison.OrdinalIgnoreCase)
            || !string.IsNullOrWhiteSpace(_bootstrap.AdminEmail)
            && string.Equals(user.Email, _bootstrap.AdminEmail.Trim(), StringComparison.OrdinalIgnoreCase));

    private async Task<User?> GetBootstrapAccountAsync(CancellationToken cancellationToken)
    {
        if (!User.IsBootstrapAdmin() || User.GetUserId() is not { } userId)
        {
            return null;
        }

        var user = await _users.FindByIdAsync(userId, cancellationToken);

        return user is not null && IsBootstrapAccount(user) ? user : null;
    }

    private async Task<IActionResult> LeaveSetupAsync()
    {
        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);

        return RedirectToAction(nameof(Login));
    }
}
