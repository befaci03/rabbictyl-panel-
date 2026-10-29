using Microsoft.Extensions.Logging.Abstractions;
using RabbiCtyl.Infrastructure.Extensions;

namespace RabbiCtyl.Tests;

public sealed class ExtensionCatalogTests : IDisposable
{
    private readonly string _root;
    private readonly FileSystemThemeCatalog _themes;
    private readonly FileSystemAddonState _addonState;
    private readonly FileSystemAddonCatalog _addons;

    public ExtensionCatalogTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "rabbictyl-tests", Guid.NewGuid().ToString("N"));

        _themes = new FileSystemThemeCatalog(
            Path.Combine(_root, "themes"),
            NullLogger<FileSystemThemeCatalog>.Instance);

        _addonState = new FileSystemAddonState(
            Path.Combine(_root, "storage", "addons.json"),
            NullLogger<FileSystemAddonState>.Instance);

        _addons = new FileSystemAddonCatalog(
            Path.Combine(_root, "addons"),
            _addonState,
            NullLogger<FileSystemAddonCatalog>.Instance);
    }

    [Fact]
    public async Task Theme_builds_stylesheet_script_and_asset_urls()
    {
        WriteFile("themes/acme/manifest.json", """
            {
              "id": "acme",
              "name": "Acme",
              "styles": ["styles/theme.scss", "styles/animations.css"],
              "scripts": ["scripts/theme.ts"],
              "assets": { "logo": "assets/logo.svg" }
            }
            """);
        WriteFile("themes/acme/styles/theme.scss", "$accent: #fff;");
        WriteFile("themes/acme/styles/animations.css", "@keyframes fade { from { opacity: 0; } }");
        WriteFile("themes/acme/scripts/theme.ts", "export {};");
        WriteFile("themes/acme/assets/logo.svg", "<svg xmlns=\"http://www.w3.org/2000/svg\"></svg>");

        var theme = Assert.Single(await _themes.ListAsync());

        Assert.Equal("acme", theme.Id);
        Assert.Equal(
            new[] { "/themes/acme/styles/theme.css", "/themes/acme/styles/animations.css" },
            theme.StylesheetUrls);
        Assert.Equal(new[] { "/themes/acme/scripts/theme.js" }, theme.ScriptUrls);
        Assert.Equal("/api/themes/acme/assets/logo", theme.AssetUrls["logo"]);
    }

    [Fact]
    public async Task Theme_rejects_plain_javascript_scripts()
    {
        WriteFile("themes/js-theme/manifest.json", """
            {
              "id": "js-theme",
              "name": "JavaScript theme",
              "scripts": ["scripts/theme.js"]
            }
            """);
        WriteFile("themes/js-theme/scripts/theme.js", "console.log('nope');");

        Assert.Empty(await _themes.ListAsync());
    }

    [Fact]
    public async Task Theme_rejects_paths_that_escape_the_folder()
    {
        WriteFile("themes/escape/manifest.json", """
            {
              "id": "escape",
              "name": "Escape",
              "styles": ["../outside.css"]
            }
            """);
        WriteFile("outside.css", "body { color: red; }");

        Assert.Empty(await _themes.ListAsync());
    }

    [Fact]
    public async Task Theme_rejects_missing_asset_files()
    {
        WriteFile("themes/broken/manifest.json", """
            {
              "id": "broken",
              "name": "Broken",
              "assets": { "logo": "assets/missing.svg" }
            }
            """);

        Assert.Empty(await _themes.ListAsync());
    }

    [Fact]
    public async Task Addon_builds_script_and_asset_urls()
    {
        WriteFile("addons/greeter/manifest.json", """
            {
              "id": "greeter",
              "name": "Greeter",
              "scripts": ["scripts/main.ts"],
              "assets": { "icon": "assets/icon.svg" }
            }
            """);
        WriteFile("addons/greeter/scripts/main.ts", "export {};");
        WriteFile("addons/greeter/assets/icon.svg", "<svg xmlns=\"http://www.w3.org/2000/svg\"></svg>");

        var addon = Assert.Single(await _addons.ListAsync());

        Assert.Equal("greeter", addon.Id);
        Assert.True(addon.IsEnabled);
        Assert.Null(addon.AssemblyPath);
        Assert.Equal(new[] { "/addons/greeter/scripts/main.js" }, addon.ScriptUrls);
        Assert.Equal("/api/addons/greeter/assets/icon", addon.AssetUrls["icon"]);
    }

    [Fact]
    public async Task Addon_enabled_state_can_be_overridden_by_the_panel()
    {
        WriteFile("addons/greeter/manifest.json", """
            {
              "id": "greeter",
              "name": "Greeter"
            }
            """);

        await _addonState.SetEnabledAsync("greeter", false);

        var addon = Assert.Single(await _addons.ListAsync());
        Assert.False(addon.IsEnabled);
        Assert.Empty(await _addons.ListAsync(enabledOnly: true));

        await _addonState.SetEnabledAsync("greeter", true);

        Assert.True(Assert.Single(await _addons.ListAsync(enabledOnly: true)).IsEnabled);
    }

    [Fact]
    public async Task Addon_rejects_plain_javascript_scripts()
    {
        WriteFile("addons/js-addon/manifest.json", """
            {
              "id": "js-addon",
              "name": "JavaScript addon",
              "scripts": ["scripts/main.js"]
            }
            """);
        WriteFile("addons/js-addon/scripts/main.js", "console.log('nope');");

        Assert.Empty(await _addons.ListAsync());
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private void WriteFile(string relativePath, string contents)
    {
        var path = Path.Combine(_root, relativePath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, contents);
    }
}
