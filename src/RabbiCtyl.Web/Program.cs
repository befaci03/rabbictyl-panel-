using System.Globalization;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Localization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using RabbiCtyl.Configuration;
using RabbiCtyl.Configuration.Options;
using RabbiCtyl.Infrastructure;
using RabbiCtyl.Web;
using RabbiCtyl.Web.Hubs;
using RabbiCtyl.Web.Localization;
using RabbiCtyl.Web.Middleware;

var builder = WebApplication.CreateBuilder(args);

var configPath = ResolveConfigPath(builder.Environment.ContentRootPath);
var options = TomlConfigLoader.Load(configPath);

NormalizePaths(builder.Environment.ContentRootPath, options);
ConfigureLogging(builder, options);

builder.WebHost.UseUrls($"http://{options.Server.Host}:{options.Server.Port}");

builder.Services.AddRabbiCtylInfrastructure(options);
builder.Services.AddAntiforgery(antiforgery => antiforgery.HeaderName = "X-CSRF-TOKEN");
builder.Services.AddControllersWithViews(mvc => mvc.Filters.Add(new AutoValidateAntiforgeryTokenAttribute()));
builder.Services.AddSignalR().AddMessagePackProtocol();
builder.Services.AddLocalization();
builder.Services.AddScoped<IPanelLocalizer, PanelLocalizer>();
builder.Services.AddHttpContextAccessor();
// Generic bridge add-ons use to sign a resolved account in; the panel does not
// know which add-on is calling.
builder.Services.AddScoped<RabbiCtyl.Addons.Abstractions.IAddonAuthentication, RabbiCtyl.Web.Security.AddonAuthentication>();

builder.Services
    .AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(cookie =>
    {
        cookie.LoginPath = "/auth/login";
        cookie.LogoutPath = "/auth/logout";
        cookie.AccessDeniedPath = "/auth/denied";
        cookie.Cookie.Name = options.Security.CookieName;
        cookie.Cookie.HttpOnly = true;
        cookie.Cookie.SameSite = SameSiteMode.Lax;
        cookie.Cookie.SecurePolicy = options.Security.RequireHttps
            ? CookieSecurePolicy.Always
            : CookieSecurePolicy.SameAsRequest;
        cookie.ExpireTimeSpan = TimeSpan.FromMinutes(options.Security.SessionLifetimeMinutes);
        cookie.SlidingExpiration = true;
    });

builder.Services.AddAuthorization();

if (options.Server.BehindProxy)
{
    builder.Services.Configure<ForwardedHeadersOptions>(forwarded =>
    {
        forwarded.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
        forwarded.KnownIPNetworks.Clear();
        forwarded.KnownProxies.Clear();
    });
}

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseDeveloperExceptionPage();
}
else
{
    app.UseExceptionHandler("/error");
    app.UseHsts();
}

if (options.Server.BehindProxy)
{
    app.UseForwardedHeaders();
}

// Serves the migration/maintenance page while the schema is being prepared.
app.UseMiddleware<MaintenanceMiddleware>();

app.UseStaticFiles();

var supportedCultures = new[] { new CultureInfo("en"), new CultureInfo("fr") };
app.UseRequestLocalization(new RequestLocalizationOptions
{
    DefaultRequestCulture = new RequestCulture("en"),
    SupportedCultures = supportedCultures,
    SupportedUICultures = supportedCultures,
});

app.UseRouting();
app.UseMiddleware<DaemonAuthenticationMiddleware>();
app.UseAntiforgery();
app.UseAuthentication();
// Serves the operator maintenance page to non-admins when enabled in General.
app.UseMiddleware<OperatorMaintenanceMiddleware>();
// RAPI keys let bots and external tools call /api/application without a cookie.
app.UseMiddleware<ApplicationApiKeyMiddleware>();
app.UseAuthorization();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");
app.MapControllers();
app.MapHub<RealtimeHub>("/hubs/realtime");
app.MapGet("/health", () => Results.Ok(new
{
    status = "ok",
    name = options.App.Name,
    environment = options.App.Environment,
}));

app.Run();

static string ResolveConfigPath(string contentRoot)
{
    var configured = Environment.GetEnvironmentVariable("RABBICTYL_CONFIG");

    if (!string.IsNullOrWhiteSpace(configured))
    {
        return Path.GetFullPath(configured);
    }

    // `dotnet run` sets the content root to the Web project, while the config
    // usually lives at the repository root. Check the content root first, then
    // walk up so both development and published layouts work.
    var current = new DirectoryInfo(contentRoot);

    for (var depth = 0; depth < 6 && current is not null; depth++, current = current.Parent)
    {
        var candidates = new[]
        {
            Path.Combine(current.FullName, "config", TomlConfigLoader.DefaultFileName),
            Path.Combine(current.FullName, TomlConfigLoader.DefaultFileName),
        };

        var found = candidates.FirstOrDefault(File.Exists);

        if (found is not null)
        {
            return found;
        }
    }

    return Path.Combine(contentRoot, "config", TomlConfigLoader.DefaultFileName);
}

static void NormalizePaths(string contentRoot, PanelOptions options)
{
    options.Themes.Directory = ResolveDirectory(contentRoot, options.Themes.Directory);
    options.Addons.Directory = ResolveDirectory(contentRoot, options.Addons.Directory);
    options.Nests.Directory = ResolveDirectory(contentRoot, options.Nests.Directory);
    options.Storage.Directory = ResolvePath(contentRoot, options.Storage.Directory);

    static string ResolvePath(string root, string path) =>
        Path.IsPathRooted(path)
            ? Path.GetFullPath(path)
            : Path.GetFullPath(Path.Combine(root, path));

    // During `dotnet run` the content root is the Web project, while the
    // bundled themes/addons live at the repository root (and are copied next
    // to the published app). Prefer the content root, then walk up.
    static string ResolveDirectory(string root, string path)
    {
        if (Path.IsPathRooted(path))
        {
            return Path.GetFullPath(path);
        }

        var candidate = Path.GetFullPath(Path.Combine(root, path));

        if (Directory.Exists(candidate))
        {
            return candidate;
        }

        var current = new DirectoryInfo(root);

        for (var depth = 0; depth < 6 && current is not null; depth++, current = current.Parent)
        {
            var probe = Path.Combine(current.FullName, path);

            if (Directory.Exists(probe))
            {
                return Path.GetFullPath(probe);
            }
        }

        return candidate;
    }
}

static void ConfigureLogging(WebApplicationBuilder builder, PanelOptions options)
{
    if (!Enum.TryParse<LogLevel>(options.Logging.Level, ignoreCase: true, out var level))
    {
        throw new ConfigurationException(
            $"logging.level '{options.Logging.Level}' is not a valid log level.");
    }

    builder.Logging.SetMinimumLevel(level);
}
