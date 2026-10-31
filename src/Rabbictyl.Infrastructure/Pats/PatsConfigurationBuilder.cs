using System.Text.Json;
using Rabbictyl.Core.Entities;

namespace Rabbictyl.Infrastructure.Pats;

/// <summary>
/// Renders the <c>settings</c> and <c>install</c> payloads pats expects. Servers
/// carry a rendered snapshot after create/update; this builder also has a
/// fallback so existing rows and manually created servers still work.
/// </summary>
public static class PatsConfigurationBuilder
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = false,
    };

    public static JsonElement BuildSettings(Server server)
    {
        if (!string.IsNullOrWhiteSpace(server.DaemonConfigurationJson))
        {
            return ParseObjectOrThrow(server.DaemonConfigurationJson);
        }

        var environment = ParseStringMap(server.EnvironmentJson);

        environment.TryAdd("STARTUP", server.StartupCommand);
        environment.TryAdd("P_SERVER_UUID", server.Uuid.ToString());
        environment.TryAdd("P_SERVER_LOCATION", server.Node?.Name ?? "unknown");

        var mappings = ParseObjectOrEmpty(server.AllocationMappingsJson);

        if (mappings.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null ||
            (mappings.ValueKind == JsonValueKind.Object && !mappings.EnumerateObject().Any()))
        {
            mappings = JsonSerializer.SerializeToElement(
                new Dictionary<string, int[]>
                {
                    [server.AllocationIp] = [server.AllocationPort],
                },
                JsonOptions);
        }

        return JsonSerializer.SerializeToElement(
            new Dictionary<string, object?>
            {
                ["uuid"] = server.Uuid.ToString(),
                ["meta"] = new
                {
                    name = server.Name,
                    description = server.Description ?? string.Empty,
                },
                ["suspended"] = server.Suspended,
                ["environment"] = environment,
                ["invocation"] = server.StartupCommand,
                ["skip_egg_scripts"] = server.SkipScripts,
                ["build"] = new
                {
                    memory_limit = server.MemoryLimitMb,
                    swap = server.SwapMb,
                    io_weight = server.IoWeight,
                    cpu_limit = server.CpuLimitPercent,
                    threads = server.Threads ?? string.Empty,
                    disk_space = server.DiskLimitMb,
                    oom_disabled = server.OomDisabled,
                },
                ["container"] = new
                {
                    image = server.Image,
                    oom_disabled = server.OomDisabled,
                    requires_rebuild = false,
                },
                ["allocations"] = new
                {
                    force_outgoing_ip = server.EggForceOutgoingIp,
                    @default = new
                    {
                        ip = server.AllocationIp,
                        port = server.AllocationPort,
                    },
                    mappings = mappings,
                },
                ["mounts"] = ParseArrayOrEmpty(server.MountsJson),
                ["egg"] = new
                {
                    id = server.EggUuid,
                    file_denylist = ParseArrayOrEmpty(server.EggFileDenylistJson),
                },
            },
            JsonOptions);
    }

    public static PatsProcessConfiguration BuildProcessConfiguration(Server server)
    {
        var document = string.IsNullOrWhiteSpace(server.ProcessConfigurationJson)
            ? DefaultProcessConfiguration()
            : ParseObjectOrThrow(server.ProcessConfigurationJson);

        return new PatsProcessConfiguration
        {
            Startup = document.TryGetProperty("startup", out var startup)
                ? startup.Clone()
                : JsonSerializer.SerializeToElement(new
                {
                    done = Array.Empty<string>(),
                    user_interaction = Array.Empty<string>(),
                    strip_ansi = false,
                }, JsonOptions),
            Stop = document.TryGetProperty("stop", out var stop)
                ? stop.Clone()
                : JsonSerializer.SerializeToElement(new { type = "command", value = "stop" }, JsonOptions),
            Configs = document.TryGetProperty("configs", out var configs)
                ? configs.Clone()
                : JsonSerializer.SerializeToElement(Array.Empty<object>(), JsonOptions),
        };
    }

    public static PatsInstallationScript BuildInstallScript(Server server)
    {
        if (!string.IsNullOrWhiteSpace(server.InstallScriptJson))
        {
            var document = ParseObjectOrThrow(server.InstallScriptJson);

            return new PatsInstallationScript
            {
                ContainerImage = GetString(document, "container_image", "alpine:3.20"),
                Entrypoint = GetString(document, "entrypoint", "ash"),
                Script = GetString(document, "script", string.Empty),
            };
        }

        return new PatsInstallationScript
        {
            ContainerImage = string.IsNullOrWhiteSpace(server.Image) ? "alpine:3.20" : server.Image,
            Entrypoint = "ash",
            Script = string.Empty,
        };
    }

    public static PatsServerConfigurationResponse BuildConfiguration(Server server) =>
        new()
        {
            Settings = BuildSettings(server),
            ProcessConfiguration = BuildProcessConfiguration(server),
        };

    private static JsonElement DefaultProcessConfiguration() =>
        JsonSerializer.SerializeToElement(new
        {
            startup = new
            {
                done = new[] { "regex:.*Done.*" },
                user_interaction = Array.Empty<string>(),
                strip_ansi = false,
            },
            stop = new { type = "command", value = "stop" },
            configs = Array.Empty<object>(),
        }, JsonOptions);

    private static Dictionary<string, string> ParseStringMap(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return new Dictionary<string, string>(StringComparer.Ordinal);
        }

        try
        {
            return JsonSerializer.Deserialize<Dictionary<string, string>>(json, JsonOptions)
                ?? new Dictionary<string, string>(StringComparer.Ordinal);
        }
        catch (JsonException)
        {
            return new Dictionary<string, string>(StringComparer.Ordinal);
        }
    }

    private static JsonElement ParseObjectOrEmpty(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return JsonSerializer.SerializeToElement(new Dictionary<string, object>(), JsonOptions);
        }

        try
        {
            using var document = JsonDocument.Parse(json);
            return document.RootElement.Clone();
        }
        catch (JsonException)
        {
            return JsonSerializer.SerializeToElement(new Dictionary<string, object>(), JsonOptions);
        }
    }

    private static JsonElement ParseObjectOrThrow(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }

    private static JsonElement ParseArrayOrEmpty(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return JsonSerializer.SerializeToElement(Array.Empty<object>(), JsonOptions);
        }

        try
        {
            using var document = JsonDocument.Parse(json);
            return document.RootElement.Clone();
        }
        catch (JsonException)
        {
            return JsonSerializer.SerializeToElement(Array.Empty<object>(), JsonOptions);
        }
    }

    private static string GetString(JsonElement element, string property, string fallback) =>
        element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? fallback
            : fallback;
}
