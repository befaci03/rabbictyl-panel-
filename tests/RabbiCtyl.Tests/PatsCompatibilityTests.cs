using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using RabbiCtyl.Core.Entities;
using RabbiCtyl.Infrastructure.Pats;

namespace RabbiCtyl.Tests;

public sealed class PatsCompatibilityTests
{
    [Fact]
    public async Task Create_server_uses_daemon_token_and_expected_payload()
    {
        var handler = new RecordingHandler("""{"state":"offline"}""");
        var client = CreateClient(handler);
        var uuid = Guid.NewGuid();

        await client.CreateServerAsync(uuid, startOnCompletion: true);

        Assert.NotNull(handler.Request);
        Assert.Equal(HttpMethod.Post, handler.Request!.Method);
        Assert.Equal("http://node.test:8080/api/servers", handler.Request.RequestUri!.ToString());
        Assert.Equal("Bearer secret-token", handler.Request.Headers.Authorization!.ToString());
        Assert.Contains(uuid.ToString(), handler.Body);
        Assert.Contains("\"start_on_completion\":true", handler.Body);
    }

    [Fact]
    public async Task Power_and_command_use_pterodactyl_paths()
    {
        var handler = new RecordingHandler("{}");
        var client = CreateClient(handler);
        var uuid = Guid.NewGuid();

        await client.PowerAsync(uuid, "restart");
        Assert.Equal($"http://node.test:8080/api/servers/{uuid}/power", handler.Request!.RequestUri!.ToString());
        Assert.Contains("\"action\":\"restart\"", handler.Body);

        await client.SendCommandsAsync(uuid, ["say hi"]);
        Assert.Equal($"http://node.test:8080/api/servers/{uuid}/commands", handler.Request!.RequestUri!.ToString());
        Assert.Contains("say hi", handler.Body);
    }

    [Fact]
    public async Task File_list_encodes_the_directory_query()
    {
        var handler = new RecordingHandler("[]");
        var client = CreateClient(handler);
        var uuid = Guid.NewGuid();

        await client.ListDirectoryAsync(uuid, "/plugins/my plugins");

        Assert.Equal(
            $"http://node.test:8080/api/servers/{uuid}/files/list-directory?directory=%2Fplugins%2Fmy%20plugins",
            handler.Request!.RequestUri!.OriginalString);
    }

    [Fact]
    public void Websocket_jwt_is_hs256_signed_with_the_daemon_token()
    {
        var node = new Node { DaemonToken = "secret-token" };
        var userId = Guid.NewGuid();
        var serverId = Guid.NewGuid();

        var token = PatsJwt.CreateWebsocketToken(node, userId, serverId, ["websocket.connect"]);

        var parts = token.Split('.');
        Assert.Equal(3, parts.Length);

        var expectedSignature = Base64Url(HMACSHA256.HashData(
            Encoding.UTF8.GetBytes("secret-token"),
            Encoding.ASCII.GetBytes($"{parts[0]}.{parts[1]}")));

        Assert.Equal(expectedSignature, parts[2]);

        using var payload = JsonDocument.Parse(Base64UrlDecode(parts[1]));
        Assert.Equal("websocket", payload.RootElement.GetProperty("scope").GetString());
        Assert.Equal(serverId.ToString(), payload.RootElement.GetProperty("server_uuid").GetString());
        Assert.Equal(userId.ToString(), payload.RootElement.GetProperty("user_uuid").GetString());
        Assert.True(payload.RootElement.GetProperty("exp").GetInt64() > DateTimeOffset.UtcNow.ToUnixTimeSeconds());
    }

    [Fact]
    public void Configuration_builder_emits_the_pats_settings_shape()
    {
        var server = new Server
        {
            Uuid = Guid.NewGuid(),
            Name = "Survival",
            Description = "A world",
            StartupCommand = "java -jar server.jar",
            Image = "eclipse-temurin:21",
            MemoryLimitMb = 2048,
            SwapMb = 512,
            DiskLimitMb = 4096,
            CpuLimitPercent = 200,
            AllocationIp = "10.0.0.5",
            AllocationPort = 25565,
            EnvironmentJson = """{"EULA":"true"}""",
            EggUuid = Guid.NewGuid().ToString(),
        };

        var settings = PatsConfigurationBuilder.BuildSettings(server);

        Assert.Equal(server.Uuid.ToString(), settings.GetProperty("uuid").GetString());
        Assert.Equal("Survival", settings.GetProperty("meta").GetProperty("name").GetString());
        Assert.Equal("java -jar server.jar", settings.GetProperty("invocation").GetString());
        Assert.Equal(2048, settings.GetProperty("build").GetProperty("memory_limit").GetInt32());
        Assert.Equal("10.0.0.5", settings.GetProperty("allocations").GetProperty("default").GetProperty("ip").GetString());
        Assert.Equal(25565, settings.GetProperty("allocations").GetProperty("default").GetProperty("port").GetInt32());
        Assert.Equal("true", settings.GetProperty("environment").GetProperty("EULA").GetString());
        Assert.Equal("java -jar server.jar", settings.GetProperty("environment").GetProperty("STARTUP").GetString());
    }

    private static IPatsClient CreateClient(RecordingHandler handler) =>
        new PatsClient(
            new HttpClient(handler),
            new Node
            {
                Name = "node",
                Fqdn = "node.test",
                UseSsl = false,
                DaemonPort = 8080,
                DaemonToken = "secret-token",
            },
            NullLogger<PatsClient>.Instance);

    private static string Base64Url(byte[] value) =>
        Convert.ToBase64String(value).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static byte[] Base64UrlDecode(string value)
    {
        var padded = value.Replace('-', '+').Replace('_', '/');

        return Convert.FromBase64String(padded.PadRight(padded.Length + ((4 - padded.Length % 4) % 4), '='));
    }

    private sealed class RecordingHandler : HttpMessageHandler
    {
        private readonly string _response;

        public RecordingHandler(string response)
        {
            _response = response;
        }

        public HttpRequestMessage? Request { get; private set; }

        public string? Body { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Request = request;
            Body = request.Content is null
                ? null
                : await request.Content.ReadAsStringAsync(cancellationToken);

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(_response, Encoding.UTF8, "application/json"),
            };
        }
    }
}
