using System.Text.Json;
using Microsoft.Extensions.Logging;
using RabbiCtyl.Configuration.Options;
using RabbiCtyl.Core.Abstractions;

namespace RabbiCtyl.Infrastructure.Security;

public sealed class CaptchaVerifier : ICaptchaVerifier
{
    private const string TurnstileEndpoint = "https://challenges.cloudflare.com/turnstile/v0/siteverify";
    private const string HCaptchaEndpoint = "https://hcaptcha.com/siteverify";

    private readonly CaptchaOptions _options;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<CaptchaVerifier> _logger;

    public CaptchaVerifier(
        SecurityOptions security,
        IHttpClientFactory httpClientFactory,
        ILogger<CaptchaVerifier> logger)
    {
        _options = security.Captcha;
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    public string Provider => _options.ResolveProvider();

    public bool Required => _options.Enabled;

    public string SiteKey => _options.SiteKey;

    public async Task<bool> VerifyAsync(
        string? token,
        string? remoteIp = null,
        CancellationToken cancellationToken = default)
    {
        if (!Required)
        {
            return true;
        }

        if (string.IsNullOrWhiteSpace(token))
        {
            return false;
        }

        var endpoint = Provider == CaptchaOptions.HCaptcha ? HCaptchaEndpoint : TurnstileEndpoint;

        var form = new Dictionary<string, string>
        {
            ["secret"] = _options.SecretKey,
            ["response"] = token,
        };

        if (!string.IsNullOrWhiteSpace(remoteIp))
        {
            form["remoteip"] = remoteIp;
        }

        try
        {
            var client = _httpClientFactory.CreateClient("captcha");
            using var content = new FormUrlEncodedContent(form);
            using var response = await client.PostAsync(endpoint, content, cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning(
                    "Captcha verification for {Provider} returned HTTP {Status}.",
                    Provider,
                    (int)response.StatusCode);

                return false;
            }

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);

            return document.RootElement.TryGetProperty("success", out var success)
                && success.ValueKind == JsonValueKind.True;
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or JsonException)
        {
            _logger.LogWarning(exception, "Could not verify the {Provider} captcha response.", Provider);

            return false;
        }
    }
}
