namespace RabbiCtyl.Core.Extensions;

public sealed record ThemeDescriptor(
    ThemeManifest Manifest,
    IReadOnlyList<string> StylesheetUrls,
    IReadOnlyList<string> ScriptUrls,
    IReadOnlyDictionary<string, string> AssetUrls)
{
    public string Id => Manifest.Id;

    public string Name => Manifest.Name;
}
