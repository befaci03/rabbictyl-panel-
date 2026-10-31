using Rabbictyl.Core.Entities;

namespace Rabbictyl.Core.Extensions;

/// <summary>
/// One game template together with where it came from. Nest templates are read
/// from TOML files on disk and are read-only; panel templates live in the
/// database and can be edited from the admin area.
/// </summary>
public sealed record GameTemplate(
    Egg Egg,
    string? NestId,
    string? NestName,
    bool FromNest,
    DateTimeOffset? EditedAt = null);

/// <summary>A nest file as loaded from <c>nests/*.toml</c>.</summary>
public sealed record NestDescriptor(
    string Id,
    string Name,
    string Author,
    string? Description,
    IReadOnlyList<Egg> Eggs,
    string? Version = null);
