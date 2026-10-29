using System.Text.Json;
using System.Text.Json.Serialization;

namespace RabbiCtyl.Infrastructure.Pats;

/// <summary>
/// Wire shapes accepted and returned by pats (the Wings-compatible daemon).
/// Property names follow pats' Go JSON tags exactly.
/// </summary>
public sealed class PatsSystemInfo
{
    [JsonPropertyName("architecture")]
    public string Architecture { get; init; } = string.Empty;

    [JsonPropertyName("cpu_count")]
    public int CpuCount { get; init; }

    [JsonPropertyName("kernel_version")]
    public string KernelVersion { get; init; } = string.Empty;

    [JsonPropertyName("os")]
    public string Os { get; init; } = string.Empty;

    [JsonPropertyName("version")]
    public string Version { get; init; } = string.Empty;
}

public sealed class PatsServerSummary
{
    [JsonPropertyName("uuid")]
    public string Uuid { get; init; } = string.Empty;

    [JsonPropertyName("settings")]
    public JsonElement Settings { get; init; }

    [JsonPropertyName("process_configuration")]
    public JsonElement ProcessConfiguration { get; init; }
}

public sealed class PatsServerState
{
    [JsonPropertyName("state")]
    public string State { get; init; } = string.Empty;

    [JsonPropertyName("is_suspended")]
    public bool IsSuspended { get; init; }

    [JsonPropertyName("utilization")]
    public JsonElement Utilization { get; init; }

    [JsonPropertyName("configuration")]
    public JsonElement Configuration { get; init; }
}

public sealed class PatsProcessConfiguration
{
    [JsonPropertyName("startup")]
    public JsonElement Startup { get; init; }

    [JsonPropertyName("stop")]
    public JsonElement Stop { get; init; }

    [JsonPropertyName("configs")]
    public JsonElement Configs { get; init; }
}

public sealed class PatsInstallationScript
{
    [JsonPropertyName("container_image")]
    public string ContainerImage { get; init; } = string.Empty;

    [JsonPropertyName("entrypoint")]
    public string Entrypoint { get; init; } = string.Empty;

    [JsonPropertyName("script")]
    public string Script { get; init; } = string.Empty;
}

public sealed class PatsServerDetails
{
    [JsonPropertyName("uuid")]
    public string Uuid { get; init; } = string.Empty;

    [JsonPropertyName("start_on_completion")]
    public bool StartOnCompletion { get; init; }
}

public sealed class PatsServerConfigurationResponse
{
    [JsonPropertyName("settings")]
    public JsonElement Settings { get; init; }

    [JsonPropertyName("process_configuration")]
    public PatsProcessConfiguration? ProcessConfiguration { get; init; }
}
