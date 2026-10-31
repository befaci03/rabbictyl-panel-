using Rabbictyl.Core.Extensions;

namespace Rabbictyl.Web.Models;

/// <summary>Administration → Nests, grouped by the nest the eggs came from.</summary>
public sealed class AdminNestsViewModel
{
    public IReadOnlyList<AdminNestGroup> Nests { get; init; } = [];

    /// <summary>Which tab of the nests page is open: <c>eggs</c>, <c>import</c> or <c>files</c>.</summary>
    public string Tab { get; init; } = "eggs";

    public int EggCount => Nests.Sum(nest => nest.EggCount);
}

/// <summary>One nest file (or the panel's own templates) and the eggs inside it.</summary>
public sealed class AdminNestGroup
{
    public string Id { get; init; } = string.Empty;

    public string Name { get; init; } = string.Empty;

    public string? Version { get; init; }

    public IReadOnlyList<GameTemplate> Eggs { get; init; } = [];

    /// <summary>Latest edit among the eggs, so the header shows when the nest changed.</summary>
    public DateTimeOffset? EditedAt { get; init; }

    /// <summary>Panel templates are grouped under an empty id.</summary>
    public bool FromNest => Id.Length > 0;

    public int EggCount => Eggs.Count;
}

/// <summary>Backs the create/edit page for a single nest egg.</summary>
public sealed class AdminGameEditViewModel
{
    public bool Create { get; init; }

    public Guid Uuid { get; init; }

    public string Name { get; init; } = string.Empty;

    public string? Description { get; init; }

    public string Author { get; init; } = string.Empty;

    public string? NestName { get; init; }

    public string? NestVersion { get; init; }

    public DateTimeOffset? EditedAt { get; init; }

    public bool FromNest => NestName is not null;

    /// <summary>The default image; also the value of the legacy egg column.</summary>
    public string Image { get; init; } = string.Empty;

    /// <summary>Named alternatives offered when a server is created, default excluded.</summary>
    public IReadOnlyList<KeyValuePair<string, string>> ExtraImages { get; init; } = [];

    public string Startup { get; init; } = string.Empty;

    public IReadOnlyList<string> DonePatterns { get; init; } = [];

    public string StopType { get; init; } = "command";

    public string StopValue { get; init; } = "stop";

    public string ScriptContainer { get; init; } = string.Empty;

    public string ScriptEntrypoint { get; init; } = string.Empty;

    public string? InstallScript { get; init; }

    /// <summary>One <c>ENV=default</c> line per variable.</summary>
    public string Variables { get; init; } = string.Empty;

    public static AdminGameEditViewModel Blank() => new()
    {
        Create = true,
        Uuid = Guid.NewGuid(),
        Name = string.Empty,
        Author = string.Empty,
        Image = "ghcr.io/pterodactyl/yolks:java_21",
        Startup = "java -Xms128M -Xmx{{SERVER_MEMORY}}M -jar {{SERVER_JARFILE}}",
        ScriptContainer = "ghcr.io/pterodactyl/installers:alpine",
        ScriptEntrypoint = "ash",
    };

    public static AdminGameEditViewModel From(GameTemplate template)
    {
        var egg = template.Egg;
        var primary = EggTemplate.Image(egg);

        return new AdminGameEditViewModel
        {
            Uuid = egg.Uuid,
            Name = egg.Name,
            Description = egg.Description,
            Author = egg.Author,
            NestName = template.NestName,
            NestVersion = template.NestVersion,
            EditedAt = template.EditedAt,
            Image = primary,
            ExtraImages = EggTemplate.ImageChoices(egg)
                .Where(choice => !string.Equals(choice.Key, "default", StringComparison.Ordinal)
                    && !string.Equals(choice.Value, primary, StringComparison.Ordinal))
                .ToList(),
            Startup = egg.StartupCommand,
            DonePatterns = EggTemplate.DonePatterns(egg),
            StopType = EggTemplate.StopType(egg),
            StopValue = EggTemplate.StopValue(egg),
            ScriptContainer = egg.ScriptContainer,
            ScriptEntrypoint = egg.ScriptEntrypoint,
            InstallScript = egg.ScriptInstall,
            Variables = EggTemplate.VariableLines(egg),
        };
    }
}
