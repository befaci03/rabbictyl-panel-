namespace Rabbictyl.Core.Abstractions;

/// <summary>
/// Verifies the challenge token returned by the browser widget. When no
/// provider is configured the check is disabled and always passes.
/// </summary>
public interface ICaptchaVerifier
{
    /// <summary>Provider id: <c>none</c>, <c>turnstile</c> or <c>hcaptcha</c>.</summary>
    string Provider { get; }

    /// <summary>True when the sign-in form must show a widget.</summary>
    bool Required { get; }

    /// <summary>Public site key used by the browser widget.</summary>
    string SiteKey { get; }

    Task<bool> VerifyAsync(
        string? token,
        string? remoteIp = null,
        CancellationToken cancellationToken = default);
}
