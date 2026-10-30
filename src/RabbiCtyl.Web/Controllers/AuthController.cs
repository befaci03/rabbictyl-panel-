using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
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
    private readonly ICaptchaVerifier _captcha;
    private readonly BootstrapOptions _bootstrap;
    private readonly SecurityOptions _security;
    private readonly PanelOptions _panel;
    private readonly IEmailSender _mail;
    private readonly IOAuthProviderStore _oauthProviders;
    private readonly IAddonCatalog _addons;
    private readonly ILogger<AuthController> _logger;

    public AuthController(
        IUserStore users,
        IPasswordHasher passwordHasher,
        ICaptchaVerifier captcha,
        BootstrapOptions bootstrap,
        SecurityOptions security,
        PanelOptions panel,
        IEmailSender mail,
        IOAuthProviderStore oauthProviders,
        IAddonCatalog addons,
        ILogger<AuthController> logger)
    {
        _users = users;
        _passwordHasher = passwordHasher;
        _captcha = captcha;
        _bootstrap = bootstrap;
        _security = security;
        _panel = panel;
        _mail = mail;
        _oauthProviders = oauthProviders;
        _addons = addons;
        _logger = logger;
    }

    [AllowAnonymous]
    [HttpGet("login")]
    public async Task<IActionResult> Login(
        string? returnUrl = null,
        CancellationToken cancellationToken = default)
    {
        if (User.Identity?.IsAuthenticated == true)
        {
            return User.IsBootstrapAdmin()
                ? RedirectToAction(nameof(Setup))
                : RedirectToAction("Index", "Home");
        }

        ViewData["ReturnUrl"] = returnUrl;
        ViewData["OAuthProviders"] = await OAuthProvidersAsync(cancellationToken);

        return View(new LoginViewModel());
    }

    private async Task<IReadOnlyList<OAuthProvider>> OAuthProvidersAsync(CancellationToken cancellationToken)
    {
        var enabledAddons = await _addons.ListAsync(enabledOnly: true, cancellationToken: cancellationToken);

        if (!enabledAddons.Any(addon =>
                string.Equals(addon.Id, "oauth2", StringComparison.OrdinalIgnoreCase)))
        {
            return [];
        }

        return (await _oauthProviders.ListAsync(cancellationToken))
            .Where(provider => provider.Enabled && provider.IsConfigured)
            .ToList();
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

        if (!await _captcha.VerifyAsync(model.CaptchaToken, HttpContext.Connection.RemoteIpAddress?.ToString(), cancellationToken))
        {
            ModelState.AddModelError(string.Empty, "Complete the human check and try again.");
            _logger.LogWarning("Sign-in blocked by the {Provider} captcha check.", _captcha.Provider);

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

        await SignInAsync(user, model.RememberMe, isBootstrapAdmin);

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

    [AllowAnonymous]
    [HttpGet("forgot")]
    public IActionResult Forgot()
    {
        ViewData["MailEnabled"] = _mail.Enabled;

        return View(new ForgotPasswordViewModel());
    }

    [AllowAnonymous]
    [HttpPost("forgot")]
    public async Task<IActionResult> Forgot(
        ForgotPasswordViewModel model,
        CancellationToken cancellationToken = default)
    {
        ViewData["MailEnabled"] = _mail.Enabled;

        if (!ModelState.IsValid)
        {
            return View(model);
        }

        var email = model.Email.Trim();
        var user = await _users.FindByLoginAsync(email, cancellationToken);

        if (user is not null
            && string.Equals(user.Email, email, StringComparison.OrdinalIgnoreCase)
            && _mail.Enabled)
        {
            var token = Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();

            user.PasswordResetTokenHash = HashToken(token);
            user.PasswordResetExpiresAt = DateTimeOffset.UtcNow.AddHours(1);
            await _users.UpdateAsync(user, cancellationToken);

            var resetUrl = $"{_panel.App.Url.TrimEnd('/')}{Url.Action(nameof(ResetPassword), "Auth", new { email = user.Email, token })}";

            try
            {
                await _mail.SendAsync(
                    user.Email,
                    $"Reset your {_panel.App.Name} password",
                    $"<p>Hello {user.DisplayName},</p><p>Use the link below within one hour to choose a new password.</p><p><a href=\"{resetUrl}\">Reset my password</a></p><p>If you did not request this, you can ignore this email.</p>",
                    $"Hello {user.DisplayName},\n\nReset your password within one hour: {resetUrl}\n\nIf you did not request this, ignore this email.",
                    cancellationToken);
            }
            catch (Exception exception)
            {
                _logger.LogError(exception, "Could not send the password reset email to {Email}.", user.Email);
            }
        }

        TempData["Success"] = "If that address belongs to an account, a reset link is on its way.";

        return RedirectToAction(nameof(Forgot));
    }

    [AllowAnonymous]
    [HttpGet("reset")]
    public IActionResult ResetPassword(string email, string token)
    {
        ViewData["MailEnabled"] = _mail.Enabled;

        return View(new ResetPasswordViewModel { Email = email, Token = token });
    }

    [AllowAnonymous]
    [HttpPost("reset")]
    public async Task<IActionResult> ResetPassword(
        ResetPasswordViewModel model,
        CancellationToken cancellationToken = default)
    {
        ViewData["MailEnabled"] = _mail.Enabled;

        if (!ModelState.IsValid)
        {
            return View(model);
        }

        var user = await _users.FindByLoginAsync(model.Email.Trim(), cancellationToken);
        var hash = HashToken(model.Token.Trim());

        if (user is null
            || string.IsNullOrWhiteSpace(user.PasswordResetTokenHash)
            || !string.Equals(user.PasswordResetTokenHash, hash, StringComparison.OrdinalIgnoreCase)
            || user.PasswordResetExpiresAt is null
            || user.PasswordResetExpiresAt < DateTimeOffset.UtcNow)
        {
            ModelState.AddModelError(string.Empty, "This reset link is invalid or has expired.");

            return View(model);
        }

        user.PasswordHash = _passwordHasher.Hash(model.Password);
        user.PasswordResetTokenHash = null;
        user.PasswordResetExpiresAt = null;
        await _users.UpdateAsync(user, cancellationToken);

        _logger.LogInformation("Password reset completed for '{Username}'.", user.Username);

        TempData["Success"] = "Your password was reset. Sign in with the new one.";

        return RedirectToAction(nameof(Login));
    }

    [AllowAnonymous]
    [HttpGet("register")]
    public IActionResult Register()
    {
        if (!_security.AllowRegistration)
        {
            return RedirectToAction(nameof(Login));
        }

        if (User.Identity?.IsAuthenticated == true)
        {
            return RedirectToAction("Index", "Home");
        }

        return View(new RegisterViewModel());
    }

    [AllowAnonymous]
    [HttpPost("register")]
    public async Task<IActionResult> Register(
        RegisterViewModel model,
        CancellationToken cancellationToken = default)
    {
        if (!_security.AllowRegistration)
        {
            return RedirectToAction(nameof(Login));
        }

        if (!ModelState.IsValid)
        {
            return View(model);
        }

        if (!await _captcha.VerifyAsync(model.CaptchaToken, HttpContext.Connection.RemoteIpAddress?.ToString(), cancellationToken))
        {
            ModelState.AddModelError(string.Empty, "Complete the human check and try again.");

            return View(model);
        }

        var username = model.Username.Trim();
        var email = model.Email.Trim();

        if (await _users.FindByLoginAsync(username, cancellationToken) is not null)
        {
            ModelState.AddModelError(nameof(model.Username), "That username is already in use.");
        }

        if (await _users.FindByLoginAsync(email, cancellationToken) is not null)
        {
            ModelState.AddModelError(nameof(model.Email), "That email is already in use.");
        }

        if (!ModelState.IsValid)
        {
            return View(model);
        }

        var user = new User
        {
            Username = username,
            Email = email,
            NameFirst = model.FirstName.Trim(),
            NameLast = model.LastName.Trim(),
            PasswordHash = _passwordHasher.Hash(model.Password),
            IsRootAdmin = false,
        };

        await _users.AddAsync(user, cancellationToken);
        await SignInAsync(user, isPersistent: false, isBootstrapAdmin: false);

        _logger.LogInformation("Registered account '{Username}'.", user.Username);

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

    private static string HashToken(string token) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token))).ToLowerInvariant();

    private async Task SignInAsync(User user, bool isPersistent, bool isBootstrapAdmin)
    {
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
                IsPersistent = isPersistent,
                ExpiresUtc = isPersistent
                    ? DateTimeOffset.UtcNow.AddDays(30)
                    : null,
            });

        _logger.LogInformation("User '{Username}' signed in.", user.Username);
    }

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
