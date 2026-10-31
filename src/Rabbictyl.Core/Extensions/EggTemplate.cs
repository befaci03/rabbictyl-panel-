using System.Text.Json;
using Rabbictyl.Core.Entities;

namespace Rabbictyl.Core.Extensions;

/// <summary>
/// Turns a stored game template into the values a server needs. The daemon never
/// reads eggs directly, so everything is copied onto the server at create time.
/// </summary>
public static class EggTemplate
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static IReadOnlyList<EggVariable> Variables(Egg egg)
    {
        if (string.IsNullOrWhiteSpace(egg.VariablesJson))
        {
            return [];
        }

        try
        {
            return JsonSerializer.Deserialize<List<EggVariable>>(egg.VariablesJson, JsonOptions) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    /// <summary>Default environment built from the variables that are user editable.</summary>
    public static Dictionary<string, string> DefaultEnvironment(Egg egg)
    {
        var environment = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var variable in Variables(egg))
        {
            if (!string.IsNullOrWhiteSpace(variable.EnvVariable))
            {
                environment[variable.EnvVariable] = variable.DefaultValue;
            }
        }

        return environment;
    }

    /// <summary>Named docker images stored on the egg (display name → image).</summary>
    public static Dictionary<string, string> Images(Egg egg)
    {
        if (string.IsNullOrWhiteSpace(egg.DockerImagesJson))
        {
            return new Dictionary<string, string> { ["default"] = egg.DockerImage };
        }

        try
        {
            return JsonSerializer.Deserialize<Dictionary<string, string>>(egg.DockerImagesJson, JsonOptions)
                   ?? new Dictionary<string, string>();
        }
        catch (JsonException)
        {
            return new Dictionary<string, string> { ["default"] = egg.DockerImage };
        }
    }

    /// <summary>First configured docker image, falling back to the legacy column.</summary>
    public static string Image(Egg egg) =>
        Images(egg).Values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))
        ?? egg.DockerImage;

    /// <summary>
    /// The named images offered when a server is created, default first and then
    /// the rest in stored order. Used by the admin list and the egg editor.
    /// </summary>
    public static IReadOnlyList<KeyValuePair<string, string>> ImageChoices(Egg egg)
    {
        var primary = Image(egg);
        var choices = new List<KeyValuePair<string, string>>
        {
            new("default", primary),
        };

        foreach (var pair in Images(egg))
        {
            if (string.Equals(pair.Key, "default", StringComparison.Ordinal)
                || string.Equals(pair.Value, primary, StringComparison.Ordinal))
            {
                continue;
            }

            choices.Add(pair);
        }

        return choices;
    }

    /// <summary>Startup detection patterns stored in the egg's process configuration.</summary>
    public static IReadOnlyList<string> DonePatterns(Egg egg)
    {
        using var document = TryParse(egg.ProcessConfigurationJson);

        if (document is null
            || document.RootElement.ValueKind != JsonValueKind.Object
            || !document.RootElement.TryGetProperty("startup", out var startup)
            || startup.ValueKind != JsonValueKind.Object
            || !startup.TryGetProperty("done", out var done)
            || done.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        return done.EnumerateArray()
            .Where(item => item.ValueKind == JsonValueKind.String)
            .Select(item => item.GetString() ?? string.Empty)
            .Where(item => item.Length > 0)
            .ToList();
    }

    /// <summary><c>stop.type</c> from the process configuration, e.g. <c>command</c> or <c>signal</c>.</summary>
    public static string StopType(Egg egg) => StopField(egg, "type") ?? "command";

    /// <summary><c>stop.value</c> from the process configuration, e.g. <c>stop</c> or <c>SIGTERM</c>.</summary>
    public static string StopValue(Egg egg) => StopField(egg, "value") ?? "stop";

    /// <summary>One <c>ENV=default</c> line per variable, for the egg editor.</summary>
    public static string VariableLines(Egg egg) => string.Join(
        '\n',
        Variables(egg)
            .Where(variable => !string.IsNullOrWhiteSpace(variable.EnvVariable))
            .Select(variable => $"{variable.EnvVariable}={variable.DefaultValue}"));

    private static string? StopField(Egg egg, string property)
    {
        using var document = TryParse(egg.ProcessConfigurationJson);

        if (document is not null
            && document.RootElement.ValueKind == JsonValueKind.Object
            && document.RootElement.TryGetProperty("stop", out var stop)
            && stop.ValueKind == JsonValueKind.Object
            && stop.TryGetProperty(property, out var value)
            && value.ValueKind == JsonValueKind.String)
        {
            return value.GetString();
        }

        return null;
    }

    private static JsonDocument? TryParse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            return JsonDocument.Parse(json);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// Copies an egg so a nest template can be edited in the panel. The UUID is
    /// preserved so servers keep pointing at the same template; the TOML file is
    /// never rewritten.
    /// </summary>
    public static Egg CopyOf(Egg egg) => new()
    {
        Uuid = egg.Uuid,
        Name = egg.Name,
        Description = egg.Description,
        Author = egg.Author,
        DockerImage = egg.DockerImage,
        DockerImagesJson = egg.DockerImagesJson,
        StartupCommand = egg.StartupCommand,
        ForceOutgoingIp = egg.ForceOutgoingIp,
        FileDenylistJson = egg.FileDenylistJson,
        ProcessConfigurationJson = egg.ProcessConfigurationJson,
        ScriptContainer = egg.ScriptContainer,
        ScriptEntrypoint = egg.ScriptEntrypoint,
        ScriptInstall = egg.ScriptInstall,
        VariablesJson = egg.VariablesJson,
    };

    public static void ApplyTo(Server server, Egg egg)
    {
        server.EggId = egg.Id;
        server.EggUuid = egg.Uuid.ToString();
        server.EggForceOutgoingIp = egg.ForceOutgoingIp;
        server.EggFileDenylistJson = egg.FileDenylistJson;
        server.StartupCommand = egg.StartupCommand;
        server.ProcessConfigurationJson = egg.ProcessConfigurationJson;

        var image = Image(egg);

        if (!string.IsNullOrWhiteSpace(image))
        {
            server.Image = image;
        }

        var environment = DefaultEnvironment(egg);

        if (environment.Count > 0)
        {
            server.EnvironmentJson = JsonSerializer.Serialize(environment, JsonOptions);
        }

        if (!string.IsNullOrWhiteSpace(egg.ScriptInstall))
        {
            server.InstallScriptJson = JsonSerializer.Serialize(
                new
                {
                    container_image = egg.ScriptContainer,
                    entrypoint = egg.ScriptEntrypoint,
                    script = egg.ScriptInstall,
                },
                JsonOptions);
        }
    }

    /// <summary>Builds the <c>process_configuration</c> document stored on an egg.</summary>
    public static string BuildProcessConfiguration(
        IEnumerable<string> donePatterns,
        string stopType,
        string stopValue)
    {
        // An empty done list is intentional: pats then treats the process as
        // running as soon as it boots instead of waiting for console output.
        var done = donePatterns
            .Select(pattern => pattern.Trim())
            .Where(pattern => pattern.Length > 0)
            .ToArray();

        return JsonSerializer.Serialize(
            new
            {
                startup = new
                {
                    done,
                    user_interaction = Array.Empty<string>(),
                    strip_ansi = false,
                },
                stop = new
                {
                    type = string.IsNullOrWhiteSpace(stopType) ? "command" : stopType.Trim(),
                    value = string.IsNullOrWhiteSpace(stopValue) ? "stop" : stopValue.Trim(),
                },
                configs = Array.Empty<object>(),
            },
            JsonOptions);
    }

    /// <summary>Builds <c>VariablesJson</c> from <c>ENV=default</c> lines.</summary>
    public static string BuildVariables(IEnumerable<string> lines)
    {
        var variables = new List<EggVariable>();

        foreach (var line in lines)
        {
            if (string.IsNullOrWhiteSpace(line) || !line.Contains('='))
            {
                continue;
            }

            var separator = line.IndexOf('=');
            var name = line[..separator].Trim();
            var value = line[(separator + 1)..].Trim();

            if (name.Length == 0)
            {
                continue;
            }

            variables.Add(new EggVariable
            {
                Name = name,
                EnvVariable = name,
                DefaultValue = value,
            });
        }

        return JsonSerializer.Serialize(variables, JsonOptions);
    }

    /// <summary>Builds <c>VariablesJson</c> from a TOML variable table.</summary>
    public static string BuildVariables(IEnumerable<KeyValuePair<string, string>> variables)
    {
        var list = variables
            .Where(pair => !string.IsNullOrWhiteSpace(pair.Key))
            .Select(pair => new EggVariable
            {
                Name = pair.Key,
                EnvVariable = pair.Key,
                DefaultValue = pair.Value,
            })
            .ToList();

        return JsonSerializer.Serialize(list, JsonOptions);
    }
}
