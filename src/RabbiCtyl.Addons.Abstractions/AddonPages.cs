using RabbiCtyl.Core.Entities;

namespace RabbiCtyl.Addons.Abstractions;

public enum AddonHttpMethod
{
    Get,
    Post,
}

/// <summary>An uploaded file passed to an addon.</summary>
public sealed record AddonFile(string FileName, string ContentType, byte[] Content);

/// <summary>
/// A framework-free view of the HTTP request, so addons never reference ASP.NET.
/// </summary>
public sealed record AddonRequest
{
    public required AddonHttpMethod Method { get; init; }

    /// <summary>Path below the addon's own route, for example <c>banner</c>.</summary>
    public required string Path { get; init; }

    public IReadOnlyDictionary<string, string> Query { get; init; } =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyDictionary<string, string> Form { get; init; } =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyDictionary<string, AddonFile> Files { get; init; } =
        new Dictionary<string, AddonFile>(StringComparer.OrdinalIgnoreCase);

    /// <summary>Antiforgery token the addon must return in its own forms.</summary>
    public string? CsrfToken { get; init; }

    public Guid? UserId { get; init; }

    public string? Username { get; init; }

    public bool IsAdmin { get; init; }

    /// <summary>Panel URL without a trailing slash.</summary>
    public string BaseUrl { get; init; } = string.Empty;
}

public sealed record AddonResponse
{
    public string? Html { get; init; }

    public byte[]? Body { get; init; }

    public string? ContentType { get; init; }

    public int StatusCode { get; init; } = 200;

    public string? Redirect { get; init; }

    /// <summary>Success message shown once on the page the redirect lands on.</summary>
    public string? Flash { get; init; }

    public static AddonResponse Page(string html) => new() { Html = html };

    public static AddonResponse File(byte[] body, string contentType) =>
        new() { Body = body, ContentType = contentType };

    public static AddonResponse NotFound() => new() { StatusCode = 404 };
}

/// <summary>
/// A settings page contributed by an addon. The panel lists it as an extra tab
/// in Administration and renders the returned HTML inside its own shell, so the
/// feature stays in the addon folder instead of the web application.
/// </summary>
public interface IAddonAdminPage
{
    /// <summary>URL segment used under <c>/admin/addons/{slug}</c>.</summary>
    string Slug { get; }

    string Label { get; }

    Task<AddonResponse> HandleAsync(AddonRequest request, CancellationToken cancellationToken = default);
}

/// <summary>
/// Public (signed-in) endpoints an addon serves under
/// <c>/addons/{addon-id}/api/...</c>, for example images used by its scripts.
/// </summary>
public interface IAddonEndpoint
{
    Task<AddonResponse> HandleAsync(AddonRequest request, CancellationToken cancellationToken = default);
}

/// <summary>
/// Lets an addon lock accounts it manages. The panel refuses to edit or delete
/// a protected account and shows the reason instead.
/// </summary>
public interface IAddonUserGuard
{
    /// <summary>Why the account is protected, or null when the addon does not manage it.</summary>
    string? ProtectionReason(User user);
}
