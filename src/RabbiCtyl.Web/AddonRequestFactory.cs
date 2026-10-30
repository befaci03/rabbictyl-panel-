using Microsoft.AspNetCore.Http;
using RabbiCtyl.Addons.Abstractions;
using RabbiCtyl.Configuration.Options;
using RabbiCtyl.Web.Security;

namespace RabbiCtyl.Web;

/// <summary>
/// Turns the current HTTP request into the framework-free request record an
/// addon receives, so addons never take a dependency on ASP.NET.
/// </summary>
internal static class AddonRequestFactory
{
    public static async Task<AddonRequest> CreateAsync(
        HttpContext context,
        string path,
        string? csrfToken,
        PanelOptions options,
        CancellationToken cancellationToken)
    {
        var form = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var files = new Dictionary<string, AddonFile>(StringComparer.OrdinalIgnoreCase);

        if (context.Request.HasFormContentType)
        {
            IFormCollection body;

            try
            {
                body = await context.Request.ReadFormAsync(cancellationToken);
            }
            catch (InvalidDataException)
            {
                body = FormCollection.Empty;
            }

            foreach (var (key, value) in body)
            {
                form[key] = value.ToString();
            }

            foreach (var file in body.Files)
            {
                if (file.Length == 0)
                {
                    continue;
                }

                await using var stream = file.OpenReadStream();
                using var buffer = new MemoryStream();
                await stream.CopyToAsync(buffer, cancellationToken);

                files[file.Name] = new AddonFile(
                    file.FileName ?? "upload",
                    file.ContentType ?? "application/octet-stream",
                    buffer.ToArray());
            }
        }

        var query = context.Request.Query.ToDictionary(
            pair => pair.Key,
            pair => pair.Value.ToString(),
            StringComparer.OrdinalIgnoreCase);

        var authenticated = context.User.Identity?.IsAuthenticated == true;

        return new AddonRequest
        {
            Method = HttpMethods.IsPost(context.Request.Method) ? AddonHttpMethod.Post : AddonHttpMethod.Get,
            Path = path,
            Query = query,
            Form = form,
            Files = files,
            CsrfToken = csrfToken,
            UserId = authenticated ? context.User.GetUserId() : null,
            Username = authenticated ? context.User.Identity?.Name : null,
            IsAdmin = authenticated && context.User.IsInRole("admin"),
            BaseUrl = options.App.Url.TrimEnd('/'),
        };
    }
}
