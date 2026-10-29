using System.Collections.Concurrent;
using System.Globalization;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;

namespace RabbiCtyl.Web.Localization;

public sealed class PanelLocalizer : IPanelLocalizer
{
    private static readonly ConcurrentDictionary<string, IReadOnlyDictionary<string, string>> Cache =
        new(StringComparer.OrdinalIgnoreCase);

    private readonly IReadOnlyDictionary<string, string> _strings;

    public PanelLocalizer(IWebHostEnvironment environment)
    {
        _strings = Load(environment.ContentRootPath, CultureInfo.CurrentUICulture);
    }

    public string this[string key] =>
        _strings.TryGetValue(key, out var value) ? value : key;

    private static IReadOnlyDictionary<string, string> Load(string contentRoot, CultureInfo culture)
    {
        var path = Path.Combine(
            contentRoot,
            "Localization",
            $"{culture.TwoLetterISOLanguageName}.json");

        if (!File.Exists(path))
        {
            path = Path.Combine(contentRoot, "Localization", "en.json");
        }

        return Cache.GetOrAdd(path, static file =>
        {
            if (!File.Exists(file))
            {
                return new Dictionary<string, string>();
            }

            using var stream = File.OpenRead(file);
            return JsonSerializer.Deserialize<Dictionary<string, string>>(stream)
                ?? new Dictionary<string, string>();
        });
    }
}
