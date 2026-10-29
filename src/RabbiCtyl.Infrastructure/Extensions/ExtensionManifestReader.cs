using System.Text.Json;

namespace RabbiCtyl.Infrastructure.Extensions;

internal static class ExtensionManifestReader
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    public static async Task<TManifest?> ReadAsync<TManifest>(
        string manifestPath,
        CancellationToken cancellationToken)
        where TManifest : class
    {
        if (!File.Exists(manifestPath))
        {
            return null;
        }

        await using var stream = File.OpenRead(manifestPath);

        return await JsonSerializer.DeserializeAsync<TManifest>(stream, Options, cancellationToken);
    }
}
