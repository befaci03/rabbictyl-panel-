using Rabbictyl.Core.Extensions;

namespace Rabbictyl.Core.Abstractions;

public interface IPanelBrandingStore
{
    PanelBranding Current { get; }

    /// <summary>Absolute path to the uploaded logo, or null when the bundled logo is used.</summary>
    string? LogoPath { get; }

    /// <summary>Changes whenever the logo file is replaced, for cache-busting URLs.</summary>
    long LogoVersion { get; }

    Task SaveAsync(PanelBranding branding, CancellationToken cancellationToken = default);

    Task SaveLogoAsync(Stream content, string extension, CancellationToken cancellationToken = default);

    Task RemoveLogoAsync(CancellationToken cancellationToken = default);
}
