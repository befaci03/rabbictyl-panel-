using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using RabbiCtyl.Core.Entities;

namespace RabbiCtyl.Infrastructure.Pats;

public sealed class PatsClient : IPatsClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly MediaTypeWithQualityHeaderValue AcceptJson = new("application/json");

    private readonly HttpClient _http;
    private readonly Node _node;
    private readonly ILogger<PatsClient> _logger;

    public PatsClient(HttpClient http, Node node, ILogger<PatsClient> logger)
    {
        _http = http;
        _node = node;
        _logger = logger;
    }

    public async Task<PatsSystemInfo> GetSystemAsync(CancellationToken cancellationToken = default) =>
        await GetJsonAsync<PatsSystemInfo>("/api/system", cancellationToken)
        ?? throw new PatsApiException(HttpStatusCode.BadGateway, "pats returned an empty system response.");

    public async Task<IReadOnlyList<PatsServerSummary>> ListServersAsync(CancellationToken cancellationToken = default) =>
        await GetJsonAsync<PatsServerSummary[]>("/api/servers", cancellationToken) ?? [];

    public async Task CreateServerAsync(
        Guid uuid,
        bool startOnCompletion = false,
        CancellationToken cancellationToken = default)
    {
        using var response = await SendAsync(
            HttpMethod.Post,
            "/api/servers",
            JsonContent.Create(new PatsServerDetails
            {
                Uuid = uuid.ToString(),
                StartOnCompletion = startOnCompletion,
            }),
            cancellationToken);

        _logger.LogInformation("Asked pats node {Node} to create server {Server}.", _node.Name, uuid);
    }

    public Task DeleteServerAsync(Guid uuid, CancellationToken cancellationToken = default) =>
        SendNoContentAsync(HttpMethod.Delete, $"/api/servers/{uuid}", cancellationToken);

    public async Task<PatsServerState?> GetServerAsync(Guid uuid, CancellationToken cancellationToken = default)
    {
        try
        {
            return await GetJsonAsync<PatsServerState>($"/api/servers/{uuid}", cancellationToken);
        }
        catch (PatsApiException exception) when (exception.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    public Task PowerAsync(
        Guid uuid,
        string action,
        int waitSeconds = 30,
        CancellationToken cancellationToken = default) =>
        SendNoContentAsync(
            HttpMethod.Post,
            $"/api/servers/{uuid}/power",
            new { action, wait_seconds = waitSeconds },
            cancellationToken);

    public Task SendCommandsAsync(
        Guid uuid,
        IReadOnlyList<string> commands,
        CancellationToken cancellationToken = default) =>
        SendNoContentAsync(
            HttpMethod.Post,
            $"/api/servers/{uuid}/commands",
            new { commands },
            cancellationToken);

    public Task InstallAsync(Guid uuid, CancellationToken cancellationToken = default) =>
        SendNoContentAsync(HttpMethod.Post, $"/api/servers/{uuid}/install", null, cancellationToken);

    public Task ReinstallAsync(Guid uuid, CancellationToken cancellationToken = default) =>
        SendNoContentAsync(HttpMethod.Post, $"/api/servers/{uuid}/reinstall", null, cancellationToken);

    public Task SyncAsync(Guid uuid, CancellationToken cancellationToken = default) =>
        SendNoContentAsync(HttpMethod.Post, $"/api/servers/{uuid}/sync", null, cancellationToken);

    public async Task<string> GetLogsAsync(
        Guid uuid,
        int size = 100,
        CancellationToken cancellationToken = default) =>
        await GetStringAsync($"/api/servers/{uuid}/logs?size={Math.Clamp(size, 1, 100)}", cancellationToken);

    public async Task<string> ListDirectoryAsync(
        Guid uuid,
        string directory,
        CancellationToken cancellationToken = default) =>
        await GetStringAsync(
            $"/api/servers/{uuid}/files/list-directory?directory={Uri.EscapeDataString(directory)}",
            cancellationToken);

    public async Task<string> ReadFileAsync(
        Guid uuid,
        string file,
        bool download = false,
        CancellationToken cancellationToken = default) =>
        await GetStringAsync(
            $"/api/servers/{uuid}/files/contents?file={Uri.EscapeDataString(file)}" +
            (download ? "&download=1" : string.Empty),
            cancellationToken);

    public Task WriteFileAsync(
        Guid uuid,
        string file,
        string contents,
        CancellationToken cancellationToken = default) =>
        SendNoContentAsync(
            HttpMethod.Post,
            $"/api/servers/{uuid}/files/write?file={Uri.EscapeDataString(file)}",
            new StringContent(contents, Encoding.UTF8, "text/plain"),
            cancellationToken);

    public Task RenameAsync(
        Guid uuid,
        string root,
        IReadOnlyList<(string From, string To)> files,
        CancellationToken cancellationToken = default) =>
        SendNoContentAsync(
            HttpMethod.Put,
            $"/api/servers/{uuid}/files/rename",
            new
            {
                root,
                files = files.Select(file => new { from = file.From, to = file.To }).ToArray(),
            },
            cancellationToken);

    public Task CopyAsync(Guid uuid, string location, CancellationToken cancellationToken = default) =>
        SendNoContentAsync(
            HttpMethod.Post,
            $"/api/servers/{uuid}/files/copy",
            new { location },
            cancellationToken);

    public Task CreateDirectoryAsync(
        Guid uuid,
        string name,
        string path,
        CancellationToken cancellationToken = default) =>
        SendNoContentAsync(
            HttpMethod.Post,
            $"/api/servers/{uuid}/files/create-directory",
            new { name, path },
            cancellationToken);

    public Task DeleteFilesAsync(
        Guid uuid,
        string root,
        IReadOnlyList<string> files,
        CancellationToken cancellationToken = default) =>
        SendNoContentAsync(
            HttpMethod.Post,
            $"/api/servers/{uuid}/files/delete",
            new { root, files },
            cancellationToken);

    public async Task<string> CompressAsync(
        Guid uuid,
        string root,
        IReadOnlyList<string> files,
        CancellationToken cancellationToken = default) =>
        await SendAndReadAsync(
            HttpMethod.Post,
            $"/api/servers/{uuid}/files/compress",
            JsonContent.Create(new { root, files }),
            cancellationToken);

    public Task DecompressAsync(
        Guid uuid,
        string root,
        string file,
        CancellationToken cancellationToken = default) =>
        SendNoContentAsync(
            HttpMethod.Post,
            $"/api/servers/{uuid}/files/decompress",
            new { root, file },
            cancellationToken);

    public Task ChmodAsync(
        Guid uuid,
        string root,
        IReadOnlyList<(string File, string Mode)> files,
        CancellationToken cancellationToken = default) =>
        SendNoContentAsync(
            HttpMethod.Post,
            $"/api/servers/{uuid}/files/chmod",
            new
            {
                root,
                files = files.Select(file => new { file = file.File, mode = file.Mode }).ToArray(),
            },
            cancellationToken);

    public Task CreateBackupAsync(
        Guid uuid,
        Guid backupUuid,
        string adapter = "wings",
        CancellationToken cancellationToken = default) =>
        SendNoContentAsync(
            HttpMethod.Post,
            $"/api/servers/{uuid}/backup",
            new { adapter, uuid = backupUuid.ToString(), ignore = string.Empty },
            cancellationToken);

    public Task RestoreBackupAsync(
        Guid uuid,
        Guid backupUuid,
        bool truncateDirectory = false,
        CancellationToken cancellationToken = default) =>
        SendNoContentAsync(
            HttpMethod.Post,
            $"/api/servers/{uuid}/backup/{backupUuid}/restore",
            new { adapter = "wings", truncate_directory = truncateDirectory },
            cancellationToken);

    public Task DeleteBackupAsync(
        Guid uuid,
        Guid backupUuid,
        CancellationToken cancellationToken = default) =>
        SendNoContentAsync(
            HttpMethod.Delete,
            $"/api/servers/{uuid}/backup/{backupUuid}",
            cancellationToken);

    public Task TransferAsync(
        Guid uuid,
        string targetUrl,
        string transferToken,
        Guid serverUuid,
        CancellationToken cancellationToken = default) =>
        SendNoContentAsync(
            HttpMethod.Post,
            $"/api/servers/{uuid}/transfer",
            new
            {
                url = targetUrl,
                token = transferToken,
                server = new { uuid = serverUuid.ToString(), start_on_completion = false },
            },
            cancellationToken);

    public Task CancelTransferAsync(Guid uuid, CancellationToken cancellationToken = default) =>
        SendNoContentAsync(HttpMethod.Delete, $"/api/servers/{uuid}/transfer", cancellationToken);

    private async Task<T?> GetJsonAsync<T>(string path, CancellationToken cancellationToken)
    {
        using var response = await SendAsync(HttpMethod.Get, path, null, cancellationToken);

        return await response.Content.ReadFromJsonAsync<T>(JsonOptions, cancellationToken);
    }

    private async Task<string> GetStringAsync(string path, CancellationToken cancellationToken)
    {
        using var response = await SendAsync(HttpMethod.Get, path, null, cancellationToken);

        return await response.Content.ReadAsStringAsync(cancellationToken);
    }

    private Task SendNoContentAsync(
        HttpMethod method,
        string path,
        CancellationToken cancellationToken) =>
        SendNoContentAsync(method, path, null, cancellationToken);

    private async Task SendNoContentAsync(
        HttpMethod method,
        string path,
        object? body,
        CancellationToken cancellationToken)
    {
        using var response = await SendAsync(
            method,
            path,
            body is null ? null : JsonContent.Create(body),
            cancellationToken);
    }

    private async Task SendNoContentAsync(
        HttpMethod method,
        string path,
        HttpContent? content,
        CancellationToken cancellationToken)
    {
        using var response = await SendAsync(method, path, content, cancellationToken);
    }

    private async Task<string> SendAndReadAsync(
        HttpMethod method,
        string path,
        HttpContent? content,
        CancellationToken cancellationToken)
    {
        using var response = await SendAsync(method, path, content, cancellationToken);

        return await response.Content.ReadAsStringAsync(cancellationToken);
    }

    private async Task<HttpResponseMessage> SendAsync(
        HttpMethod method,
        string path,
        HttpContent? content,
        CancellationToken cancellationToken)
    {
        var request = new HttpRequestMessage(method, BuildUri(path))
        {
            Content = content,
        };

        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _node.DaemonToken);
        request.Headers.Accept.Add(AcceptJson);

        var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);

        if (response.IsSuccessStatusCode)
        {
            return response;
        }

        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        var status = response.StatusCode;
        response.Dispose();

        throw new PatsApiException(status, body);
    }

    private Uri BuildUri(string path) =>
        new($"{_node.DaemonBaseUrl.TrimEnd('/')}/{path.TrimStart('/')}");
}

public sealed class PatsClientFactory : IPatsClientFactory
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILoggerFactory _loggerFactory;

    public PatsClientFactory(IHttpClientFactory httpClientFactory, ILoggerFactory loggerFactory)
    {
        _httpClientFactory = httpClientFactory;
        _loggerFactory = loggerFactory;
    }

    public IPatsClient Create(Node node) =>
        new PatsClient(
            _httpClientFactory.CreateClient("pats"),
            node,
            _loggerFactory.CreateLogger<PatsClient>());
}
