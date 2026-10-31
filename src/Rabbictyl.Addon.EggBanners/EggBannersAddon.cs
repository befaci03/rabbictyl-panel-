using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Rabbictyl.Addons.Abstractions;
using Rabbictyl.Core.Abstractions;
using Rabbictyl.Core.Extensions;

namespace Rabbictyl.Addon.EggBanners;

/// <summary>
/// Lets administrators attach a banner to each nest egg. The server page asks
/// this addon for the banner that matches the server's egg, so a Minecraft Java
/// server shows the Minecraft banner without the panel knowing about it.
/// </summary>
public sealed class EggBannersAddon : IAddon, IAddonAdminPage, IAddonEndpoint
{
    private const string AdminRoute = "/admin/addons/banners";
    private const long MaxBannerBytes = 4 * 1024 * 1024;

    private static readonly string[] AllowedExtensions =
        [".png", ".jpg", ".jpeg", ".webp", ".gif", ".avif", ".svg"];

    private AddonContext? _context;

    public string Id => "egg_banners";

    public string Slug => "banners";

    public string Label => "Banners";

    private string BannersDirectory => Path.Combine(_context!.StorageDirectory, "banners");

    private string IndexPath => Path.Combine(_context!.StorageDirectory, "banners.json");

    public Task InitializeAsync(AddonContext context, CancellationToken cancellationToken = default)
    {
        _context = context;
        Directory.CreateDirectory(BannersDirectory);

        return Task.CompletedTask;
    }

    public async Task<AddonResponse> HandleAsync(
        AddonRequest request,
        CancellationToken cancellationToken = default)
    {
        if (_context is null)
        {
            return AddonResponse.Page(
                "<div class=\"alert alert--danger\">The Egg Banners add-on is still starting.</div>");
        }

        if (string.Equals(request.Path, "banner", StringComparison.OrdinalIgnoreCase))
        {
            return await ServeBannerAsync(request, cancellationToken);
        }

        if (request.Method == AddonHttpMethod.Post)
        {
            return await HandlePostAsync(request, cancellationToken);
        }

        return await RenderAsync(request, error: null, cancellationToken);
    }

    private async Task<AddonResponse> ServeBannerAsync(
        AddonRequest request,
        CancellationToken cancellationToken)
    {
        var eggUuid = QueryValue(request, "egg");
        var serverUuid = QueryValue(request, "server");

        if (serverUuid.Length > 0)
        {
            if (!Guid.TryParse(serverUuid, out var uuid))
            {
                return AddonResponse.NotFound();
            }

            using var scope = _context!.Services.CreateScope();
            var servers = scope.ServiceProvider.GetRequiredService<IServerStore>();
            var server = await servers.FindByUuidAsync(uuid, cancellationToken);

            if (server is null ||
                (!request.IsAdmin && (request.UserId is null || server.OwnerId != request.UserId)))
            {
                return AddonResponse.NotFound();
            }

            eggUuid = server.EggUuid;
        }

        var index = await ReadIndexAsync(cancellationToken);

        if (eggUuid.Length == 0 || !index.TryGetValue(eggUuid.ToLowerInvariant(), out var fileName))
        {
            return AddonResponse.NotFound();
        }

        var path = Path.Combine(BannersDirectory, fileName);

        if (!File.Exists(path))
        {
            return AddonResponse.NotFound();
        }

        var content = await File.ReadAllBytesAsync(path, cancellationToken);

        return AddonResponse.File(content, ContentType(fileName));
    }

    private async Task<AddonResponse> HandlePostAsync(
        AddonRequest request,
        CancellationToken cancellationToken)
    {
        var action = FormValue(request, "action");

        if (action == "delete")
        {
            var eggUuid = FormValue(request, "egg").ToLowerInvariant();
            var index = await ReadIndexAsync(cancellationToken);

            if (index.Remove(eggUuid, out var fileName))
            {
                TryDelete(Path.Combine(BannersDirectory, fileName));
                await WriteIndexAsync(index, cancellationToken);
            }

            return Redirect("Banner removed.");
        }

        if (action != "upload")
        {
            return await RenderAsync(request, "Unknown action.", cancellationToken);
        }

        var egg = FormValue(request, "egg");

        if (!Guid.TryParse(egg, out var eggId))
        {
            return await RenderAsync(request, "Pick an egg first.", cancellationToken);
        }

        if (!request.Files.TryGetValue("banner", out var file) || file.Content.Length == 0)
        {
            return await RenderAsync(request, "Choose an image to upload.", cancellationToken);
        }

        var extension = Path.GetExtension(file.FileName).ToLowerInvariant();

        if (!AllowedExtensions.Contains(extension))
        {
            return await RenderAsync(request, "The banner must be a PNG, JPEG, WebP, GIF, AVIF or SVG image.", cancellationToken);
        }

        if (file.Content.Length > MaxBannerBytes)
        {
            return await RenderAsync(request, "The banner must be 4 MB or smaller.", cancellationToken);
        }

        var key = eggId.ToString().ToLowerInvariant();
        var bannerFileName = $"{key}{extension}";
        var bannerIndex = await ReadIndexAsync(cancellationToken);

        if (bannerIndex.TryGetValue(key, out var previous) && previous != bannerFileName)
        {
            TryDelete(Path.Combine(BannersDirectory, previous));
        }

        Directory.CreateDirectory(BannersDirectory);
        await File.WriteAllBytesAsync(
            Path.Combine(BannersDirectory, bannerFileName),
            file.Content,
            cancellationToken);

        bannerIndex[key] = bannerFileName;
        await WriteIndexAsync(bannerIndex, cancellationToken);

        return Redirect("Banner saved.");
    }

    private async Task<AddonResponse> RenderAsync(
        AddonRequest request,
        string? error,
        CancellationToken cancellationToken)
    {
        using var scope = _context!.Services.CreateScope();
        var templates = scope.ServiceProvider.GetRequiredService<IGameTemplateCatalog>();
        var eggs = (await templates.ListAsync(cancellationToken))
            .Select(template => template.Egg)
            .DistinctBy(egg => egg.Uuid)
            .OrderBy(egg => egg.Name)
            .ToList();

        var index = await ReadIndexAsync(cancellationToken);
        var csrf = E(request.CsrfToken ?? string.Empty);
        var html = new StringBuilder();

        html.AppendLine("<p class=\"form__hint\">Each banner is matched to servers by their egg. Upload one image per egg; it appears at the top of every server running that egg.</p>");

        if (!string.IsNullOrWhiteSpace(error))
        {
            html.AppendLine($"<div class=\"alert alert--danger\" role=\"alert\">{E(error)}</div>");
        }

        html.AppendLine("<details class=\"card admin-create\" open>");
        html.AppendLine("<summary class=\"admin-create__summary\">");
        html.AppendLine("<span><strong>Add a banner</strong><span class=\"admin-create__hint\">PNG, JPEG, WebP, GIF, AVIF or SVG up to 4 MB.</span></span>");
        html.AppendLine("<span class=\"button button--primary button--sm\">Upload</span>");
        html.AppendLine("</summary>");
        html.AppendLine("<div class=\"admin-create__body\">");
        html.AppendLine($"<form method=\"post\" action=\"{E(AdminRoute)}\" enctype=\"multipart/form-data\" class=\"form\">");
        html.AppendLine($"<input type=\"hidden\" name=\"__RequestVerificationToken\" value=\"{csrf}\" />");
        html.AppendLine("<input type=\"hidden\" name=\"action\" value=\"upload\" />");
        html.AppendLine("<div class=\"admin-create__grid\">");
        html.AppendLine("<div class=\"form__group\"><label class=\"form__label\" for=\"banner-egg\">Nest egg</label><select class=\"form__input\" id=\"banner-egg\" name=\"egg\" required>");

        foreach (var egg in eggs)
        {
            html.AppendLine($"<option value=\"{E(egg.Uuid.ToString())}\">{E(egg.Name)}</option>");
        }

        html.AppendLine("</select></div>");
        html.AppendLine("<div class=\"form__group\"><label class=\"form__label\" for=\"banner-file\">Banner image</label><input class=\"form__input\" id=\"banner-file\" name=\"banner\" type=\"file\" accept=\"image/*\" required /></div>");
        html.AppendLine("</div>");
        html.AppendLine("<div class=\"admin-create__actions\"><button type=\"submit\" class=\"button button--primary\">Save banner</button></div>");
        html.AppendLine("</form></div></details>");

        html.AppendLine("<section class=\"card card--flush\">");

        if (eggs.Count == 0)
        {
            html.AppendLine("<div class=\"empty\"><p class=\"empty__title\">No eggs available</p><p class=\"empty__hint\">Add a nest before attaching banners.</p></div>");
        }
        else
        {
            html.AppendLine("<div class=\"table-wrap\"><table class=\"table\"><thead><tr><th>Egg</th><th>Banner</th><th></th></tr></thead><tbody>");

            foreach (var egg in eggs)
            {
                var key = egg.Uuid.ToString().ToLowerInvariant();
                var hasBanner = index.ContainsKey(key);

                html.AppendLine("<tr>");
                html.AppendLine($"<td><span class=\"table__primary\">{E(egg.Name)}</span><span class=\"table__secondary\">{E(egg.Uuid.ToString())}</span></td>");
                html.AppendLine("<td>");

                if (hasBanner)
                {
                    html.AppendLine($"<img class=\"egg-banner-preview\" src=\"/addons/egg_banners/api/banner?egg={E(key)}\" alt=\"\" />");
                }
                else
                {
                    html.AppendLine("<span class=\"table__secondary\">No banner</span>");
                }

                html.AppendLine("</td>");
                html.AppendLine("<td>");

                if (hasBanner)
                {
                    html.AppendLine($"<form method=\"post\" action=\"{E(AdminRoute)}\" class=\"inline-form\">");
                    html.AppendLine($"<input type=\"hidden\" name=\"__RequestVerificationToken\" value=\"{csrf}\" />");
                    html.AppendLine("<input type=\"hidden\" name=\"action\" value=\"delete\" />");
                    html.AppendLine($"<input type=\"hidden\" name=\"egg\" value=\"{E(key)}\" />");
                    html.AppendLine("<button type=\"submit\" class=\"button button--ghost button--sm\">Remove</button>");
                    html.AppendLine("</form>");
                }

                html.AppendLine("</td></tr>");
            }

            html.AppendLine("</tbody></table></div>");
        }

        html.AppendLine("</section>");

        return AddonResponse.Page(html.ToString());
    }

    private async Task<Dictionary<string, string>> ReadIndexAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(IndexPath))
        {
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }

        try
        {
            var json = await File.ReadAllTextAsync(IndexPath, cancellationToken);
            var parsed = JsonSerializer.Deserialize<Dictionary<string, string>>(json);

            return parsed is null
                ? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                : new Dictionary<string, string>(parsed, StringComparer.OrdinalIgnoreCase);
        }
        catch (Exception exception) when (exception is JsonException or IOException)
        {
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }
    }

    private async Task WriteIndexAsync(
        Dictionary<string, string> index,
        CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(_context!.StorageDirectory);

        await File.WriteAllTextAsync(
            IndexPath,
            JsonSerializer.Serialize(index, new JsonSerializerOptions { WriteIndented = true }),
            cancellationToken);
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (IOException)
        {
            // A leftover file is harmless; the index no longer points at it.
        }
    }

    private static string ContentType(string fileName) =>
        Path.GetExtension(fileName).ToLowerInvariant() switch
        {
            ".png" => "image/png",
            ".jpg" or ".jpeg" => "image/jpeg",
            ".webp" => "image/webp",
            ".gif" => "image/gif",
            ".avif" => "image/avif",
            ".svg" => "image/svg+xml",
            _ => "application/octet-stream",
        };

    private static string FormValue(AddonRequest request, string name) =>
        request.Form.TryGetValue(name, out var value) ? value.Trim() : string.Empty;

    private static string QueryValue(AddonRequest request, string name) =>
        request.Query.TryGetValue(name, out var value) ? value.Trim() : string.Empty;

    private static AddonResponse Redirect(string message) =>
        new()
        {
            Redirect = AdminRoute,
            Flash = message,
        };

    private static string E(string value) => WebUtility.HtmlEncode(value);
}
