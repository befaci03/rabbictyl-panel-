namespace RabbiCtyl.Web.Models;

public sealed record ThemeDto(
    string Id,
    string Name,
    string Version,
    string? Description,
    string? Author,
    IReadOnlyList<string> Styles,
    IReadOnlyList<string> Scripts,
    IReadOnlyDictionary<string, string> Assets);
