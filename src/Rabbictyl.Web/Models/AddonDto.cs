namespace Rabbictyl.Web.Models;

public sealed record AddonDto(
    string Id,
    string Name,
    string Version,
    string? Description,
    string? Author,
    bool Enabled,
    bool HasCSharp,
    IReadOnlyList<string> Scripts,
    IReadOnlyDictionary<string, string> Assets);
