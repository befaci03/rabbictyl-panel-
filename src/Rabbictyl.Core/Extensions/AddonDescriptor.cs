namespace Rabbictyl.Core.Extensions;

public sealed record AddonDescriptor(
    AddonManifest Manifest,
    string Directory,
    string? AssemblyPath,
    IReadOnlyList<string> ScriptUrls,
    IReadOnlyDictionary<string, string> AssetUrls)
{
    public string Id => Manifest.Id;

    public string Name => Manifest.Name;

    public bool IsEnabled => Manifest.Enabled;
}
