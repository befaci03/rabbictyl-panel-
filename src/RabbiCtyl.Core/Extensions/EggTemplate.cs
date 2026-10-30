using System.Text.Json;
using RabbiCtyl.Core.Entities;

namespace RabbiCtyl.Core.Extensions;

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

    /// <summary>First configured docker image, falling back to the legacy column.</summary>
    public static string Image(Egg egg)
    {
        if (string.IsNullOrWhiteSpace(egg.DockerImagesJson))
        {
            return egg.DockerImage;
        }

        try
        {
            var images = JsonSerializer.Deserialize<Dictionary<string, string>>(egg.DockerImagesJson, JsonOptions);

            return images?.Values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))
                ?? egg.DockerImage;
        }
        catch (JsonException)
        {
            return egg.DockerImage;
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
