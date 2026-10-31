using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Rabbictyl.Configuration.Options;
using Tomlyn;

namespace Rabbictyl.Web.Services;

/// <summary>
/// Backs the Administration → Games "Import from URL" feature.
///
/// A preview batch fetches and parses every remote nest file once and caches
/// the exact bytes it showed the administrator, so what is confirmed in the
/// warning popup is what gets written. An import job then writes the cached
/// files into the nests directory and reports one event per file to the
/// streaming endpoint; nest files are re-read by the catalog on every request,
/// so imported eggs appear immediately.
/// </summary>
public sealed class NestImportService
{
    public const int MaxUrls = 50;
    public const int MaxBytes = 2 * 1024 * 1024;
    public const int FetchConcurrency = 5;

    private static readonly TimeSpan BatchTtl = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan JobTtl = TimeSpan.FromMinutes(30);

    private static readonly TomlSerializerOptions TomlOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        PropertyNameCaseInsensitive = true,
    };

    private readonly ConcurrentDictionary<string, CachedBatch> _batches = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, ImportJob> _jobs = new(StringComparer.Ordinal);
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly PanelOptions _options;
    private readonly ILogger<NestImportService> _logger;

    public NestImportService(
        IHttpClientFactory httpClientFactory,
        PanelOptions options,
        ILogger<NestImportService> logger)
    {
        _httpClientFactory = httpClientFactory;
        _options = options;
        _logger = logger;
    }

    /// <summary>Summary shown in the confirmation popup for one URL.</summary>
    public sealed record PreviewItem(
        string Url,
        string Host,
        bool Ok,
        string? NestId,
        string? NestName,
        int EggCount,
        IReadOnlyList<string> Eggs,
        string TargetFile,
        bool Conflict,
        bool Duplicate,
        string? Error);

    public sealed record PreviewResult(
        string BatchId,
        IReadOnlyList<string> Warnings,
        IReadOnlyList<PreviewItem> Items);

    /// <summary>Final state of one file, replayed if the stream reconnects.</summary>
    public sealed record JobResult(int Index, string Url, string File, string State, string Detail);

    public static readonly IReadOnlyList<string> Warnings =
    [
        "Only import nest files from sources you trust. An egg's install script runs on your nodes and its startup command runs inside every server container created from it.",
        "Importing writes .toml files into this panel's nests folder. A file with the same name is replaced.",
        "Files are fetched by the server; the preview shows exactly the content that will be written.",
    ];

    /// <summary>
    /// Fetches and parses every URL, caches the content and returns the batch id
    /// that <see cref="TryCreateJob"/> accepts.
    /// </summary>
    public async Task<PreviewResult> PreviewAsync(IReadOnlyList<string> urls, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(urls);
        Prune();

        var items = new PreviewItem?[urls.Count];
        var cached = new CachedItem?[urls.Count];
        var client = _httpClientFactory.CreateClient("nest-import");
        using var throttle = new SemaphoreSlim(FetchConcurrency);

        var tasks = urls.Select(async (url, index) =>
        {
            await throttle.WaitAsync(cancellationToken);

            try
            {
                var (item, cache) = await FetchOneAsync(client, index, url.Trim(), cancellationToken);
                items[index] = item;
                cached[index] = cache;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception exception)
            {
                _logger.LogWarning(exception, "Nest import preview failed for {Url}", url);
                items[index] = new PreviewItem(
                    url, HostOf(url), false, null, null, 0, [], TargetOf(url), false, false,
                    $"Could not fetch: {exception.Message}");
            }
            finally
            {
                throttle.Release();
            }
        });

        await Task.WhenAll(tasks);

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var finalItems = new List<PreviewItem>(items.Length);
        var batchItems = new List<CachedItem>(items.Length);

        for (var i = 0; i < items.Length; i++)
        {
            var item = items[i]!;

            var duplicate = item.Ok && !seen.Add(item.TargetFile);
            item = item with { Duplicate = duplicate };
            finalItems.Add(item);

            if (item.Ok && cached[i] is { } entry)
            {
                batchItems.Add(entry);
            }
        }

        var batchId = Guid.NewGuid().ToString("n");
        _batches[batchId] = new CachedBatch { CreatedAt = DateTime.UtcNow, Items = batchItems };

        return new PreviewResult(batchId, Warnings, finalItems);
    }

    /// <summary>Summary of one nest file currently on disk.</summary>
    public sealed record LocalNestFile(
        string File,
        string? NestId,
        string? NestName,
        string? Version,
        int EggCount,
        DateTime ModifiedAt,
        string? Error);

    /// <summary>Nest files currently in the nests folder, sorted by name.</summary>
    public IReadOnlyList<LocalNestFile> ListLocal()
    {
        var directory = _options.Nests.Directory;

        if (!Directory.Exists(directory))
        {
            return [];
        }

        var files = new List<LocalNestFile>();

        foreach (var path in Directory.EnumerateFiles(directory, "*.toml").OrderBy(path => path, StringComparer.Ordinal))
        {
            string? nestId = null;
            string? nestName = null;
            string? version = null;
            var eggs = 0;
            string? error = null;

            try
            {
                var content = File.ReadAllText(path);

                if (TryParse(content, out var model, out var parseError))
                {
                    nestId = model!.Id;
                    nestName = model.Name;
                    version = model.Version;
                    eggs = model.Egg.Count;
                }
                else
                {
                    error = parseError;
                }
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                error = exception.Message;
            }

            files.Add(new LocalNestFile(
                Path.GetFileName(path),
                nestId,
                nestName,
                version,
                eggs,
                File.GetLastWriteTimeUtc(path),
                error));
        }

        return files;
    }

    /// <summary>
    /// Deletes one nest file by name. Only a plain <c>*.toml</c> file name is
    /// accepted, so nothing outside the nests folder can be removed.
    /// </summary>
    public (bool Ok, string? Error) DeleteLocal(string file)
    {
        if (string.IsNullOrWhiteSpace(file) || !file.EndsWith(".toml", StringComparison.OrdinalIgnoreCase))
        {
            return (false, "Only .toml nest files can be deleted.");
        }

        if (file.Contains('/') || file.Contains('\\') || !string.Equals(file, Path.GetFileName(file), StringComparison.Ordinal))
        {
            return (false, "Invalid file name.");
        }

        var directory = Path.GetFullPath(_options.Nests.Directory);
        var path = Path.GetFullPath(Path.Combine(directory, file));

        if (!path.StartsWith(directory + Path.DirectorySeparatorChar, StringComparison.Ordinal))
        {
            return (false, "Invalid file name.");
        }

        if (!File.Exists(path))
        {
            return (false, "That nest file no longer exists.");
        }

        File.Delete(path);

        return (true, null);
    }

    /// <summary>Creates an import job for a preview batch that still has content.</summary>
    public bool TryCreateJob(string batchId, out ImportJob? job)
    {
        job = null;

        if (string.IsNullOrWhiteSpace(batchId) || !_batches.TryGetValue(batchId, out var batch))
        {
            return false;
        }

        Prune();

        var created = new ImportJob(Guid.NewGuid().ToString("n"), batchId);
        _jobs[created.Id] = created;
        job = created;

        return true;
    }

    public bool TryGetJob(string jobId, out ImportJob? job)
    {
        if (string.IsNullOrWhiteSpace(jobId))
        {
            job = null;
            return false;
        }

        if (_jobs.TryGetValue(jobId, out var found))
        {
            job = found;
            return true;
        }

        job = null;
        return false;
    }

    /// <summary>
    /// Writes every cached file of the job into the nests directory, emitting one
    /// <c>item</c> event per file and a final <c>done</c> event.
    /// </summary>
    public async Task ExecuteAsync(
        ImportJob job,
        Func<string, object, Task> emit,
        CancellationToken cancellationToken)
    {
        var imported = 0;
        var failed = 0;
        CachedBatch? batch = null;

        try
        {
            if (!_batches.TryGetValue(job.BatchId, out batch))
            {
                batch = null;
                await emit("error", new { message = "The preview expired. Run the preview again." });
                failed = 1;
                return;
            }

            var directory = _options.Nests.Directory;
            Directory.CreateDirectory(directory);                for (var index = 0; index < batch.Items.Count; index++)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    break;
                }

                var entry = batch.Items[index];
                var path = Path.Combine(directory, entry.TargetFile);

                await emit("item", new
                {
                    index = entry.Index,
                    url = entry.Url,
                    file = entry.TargetFile,
                    state = "parsing",
                    detail = "Parsing nest file",
                });

                try
                {
                    if (!TryParse(entry.Content, out var model, out var error))
                    {
                        throw new InvalidOperationException(error);
                    }

                    await emit("item", new
                    {
                        index = entry.Index,
                        url = entry.Url,
                        file = entry.TargetFile,
                        state = "writing",
                        detail = $"Writing nests/{entry.TargetFile}",
                    });

                    await File.WriteAllTextAsync(path, entry.Content, new UTF8Encoding(false), cancellationToken);

                    var eggs = model!.Egg.Count;
                    imported++;

                    var detail = $"Imported {eggs} egg(s) into nests/{entry.TargetFile}";

                    if (entry.Existed)
                    {
                        detail += " (replaced existing file)";
                    }

                    job.AddResult(new JobResult(entry.Index, entry.Url, entry.TargetFile, "done", detail));

                    await emit("item", new
                    {
                        index = entry.Index,
                        url = entry.Url,
                        file = entry.TargetFile,
                        state = "done",
                        detail,
                    });
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception exception)
                {
                    failed++;
                    var detail = exception.Message;
                    job.AddResult(new JobResult(entry.Index, entry.Url, entry.TargetFile, "failed", detail));

                    await emit("item", new
                    {
                        index = entry.Index,
                        url = entry.Url,
                        file = entry.TargetFile,
                        state = "failed",
                        detail,
                    });

                    _logger.LogWarning(exception, "Nest import failed for {Url}", entry.Url);
                }
            }
        }
        finally
        {
            job.MarkCompleted();
        }

        try
        {
            await emit("done", new { imported, failed, total = batch?.Items.Count ?? imported + failed });
        }
        catch (OperationCanceledException)
        {
            // Client went away; the job results are kept for a later reconnect.
        }
    }

    private async Task<(PreviewItem Item, CachedItem? Cache)> FetchOneAsync(
        HttpClient client,
        int index,
        string url,
        CancellationToken cancellationToken)
    {
        var host = HostOf(url);

        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            return (
                new PreviewItem(url, host, false, null, null, 0, [], TargetOf(url), false, false,
                    "Only http:// and https:// URLs can be imported."),
                null);
        }

        using var response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            return (
                new PreviewItem(url, uri.Host, false, null, null, 0, [], TargetOf(url), false, false,
                    $"The server answered with HTTP {(int)response.StatusCode} {response.ReasonPhrase}."),
                null);
        }

        if (response.Content.Headers.ContentLength is > MaxBytes)
        {
            return (
                new PreviewItem(url, uri.Host, false, null, null, 0, [], TargetOf(url), false, false,
                    $"The file is larger than {MaxBytes / (1024 * 1024)} MiB."),
                null);
        }

        var bytes = await response.Content.ReadAsByteArrayAsync(cancellationToken);

        if (bytes.Length > MaxBytes)
        {
            return (
                new PreviewItem(url, uri.Host, false, null, null, 0, [], TargetOf(url), false, false,
                    $"The file is larger than {MaxBytes / (1024 * 1024)} MiB."),
                null);
        }

        var content = Encoding.UTF8.GetString(bytes);

        if (!TryParse(content, out var model, out var error))
        {
            return (
                new PreviewItem(url, uri.Host, false, null, null, 0, [], TargetOf(url), false, false, error),
                null);
        }

        var target = TargetFileName(model!.Id, uri);
        var conflict = File.Exists(Path.Combine(_options.Nests.Directory, target));

        var eggs = model.Egg
            .Select(egg => string.IsNullOrWhiteSpace(egg.Name)
                ? (egg.Id ?? "?")
                : egg.Name.Trim())
            .ToList();

        var item = new PreviewItem(
            url,
            uri.Host,
            true,
            model.Id,
            model.Name,
            model.Egg.Count,
            eggs,
            target,
            conflict,
            false,
            null);

        var cache = new CachedItem
        {
            Index = index,
            Url = url,
            TargetFile = target,
            Content = content,
            Existed = conflict,
        };

        return (item, cache);
    }

    private static bool TryParse(string content, out NestFileModel? model, out string? error)
    {
        model = null;
        error = null;

        try
        {
            model = TomlSerializer.Deserialize<NestFileModel>(content, TomlOptions);
        }
        catch (TomlException exception)
        {
            error = $"Not a valid nest TOML file: {exception.Message}";
            return false;
        }

        if (model is null)
        {
            error = "The file is empty.";
            return false;
        }

        if (model.Egg.Count == 0)
        {
            error = "The file contains no [[egg]] entries.";
            return false;
        }

        foreach (var egg in model.Egg)
        {
            if (string.IsNullOrWhiteSpace(egg.Id) || string.IsNullOrWhiteSpace(egg.Startup))
            {
                error = "Every [[egg]] entry needs an id and a startup command.";
                return false;
            }
        }

        return true;
    }

    /// <summary>Deterministic target file inside the nests folder for a nest id.</summary>
    private static string TargetFileName(string? nestId, Uri uri)
    {
        var candidate = nestId;

        if (string.IsNullOrWhiteSpace(candidate))
        {
            candidate = Path.GetFileNameWithoutExtension(uri.AbsolutePath);
        }

        if (string.IsNullOrWhiteSpace(candidate))
        {
            candidate = uri.Host;
        }

        var slug = Regex.Replace(candidate.Trim().ToLowerInvariant(), @"[^a-z0-9_-]+", "-").Trim('-');

        return (string.IsNullOrWhiteSpace(slug) ? "nest" : slug) + ".toml";
    }

    private static string TargetOf(string url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri) ? TargetFileName(null, uri) : "nest.toml";

    private static string HostOf(string url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri) ? uri.Host : url;

    private void Prune()
    {
        var now = DateTime.UtcNow;

        foreach (var (id, batch) in _batches)
        {
            if (now - batch.CreatedAt > BatchTtl)
            {
                _batches.TryRemove(id, out _);
            }
        }

        foreach (var (id, job) in _jobs)
        {
            if (now - job.CreatedAt > JobTtl)
            {
                _jobs.TryRemove(id, out _);
            }
        }
    }

    private sealed class CachedBatch
    {
        public required DateTime CreatedAt { get; init; }

        public required List<CachedItem> Items { get; init; }
    }

    private sealed class CachedItem
    {
        /// <summary>Position of the URL in the preview, so stream events line up with the popup rows.</summary>
        public required int Index { get; init; }

        public required string Url { get; init; }

        public required string TargetFile { get; init; }

        public required string Content { get; init; }

        public required bool Existed { get; init; }
    }

    /// <summary>
    /// One import run. <see cref="TryBegin"/> guarantees a job is executed only
    /// once even if the event stream reconnects; completed jobs keep their results
    /// for a short window so a reconnect can replay them.
    /// </summary>
    public sealed class ImportJob
    {
        private readonly List<JobResult> _results = [];
        private readonly object _sync = new();
        private int _started;
        private int _completed;

        public ImportJob(string id, string batchId)
        {
            Id = id;
            BatchId = batchId;
            CreatedAt = DateTime.UtcNow;
        }

        public string Id { get; }

        public string BatchId { get; }

        public DateTime CreatedAt { get; }

        public bool Completed => Volatile.Read(ref _completed) == 1;

        public bool TryBegin() => Interlocked.Exchange(ref _started, 1) == 0;

        public void MarkCompleted() => Volatile.Write(ref _completed, 1);

        public void AddResult(JobResult result)
        {
            lock (_sync)
            {
                _results.Add(result);
            }
        }

        public JobResult[] Snapshot()
        {
            lock (_sync)
            {
                return [.. _results];
            }
        }
    }

    private sealed class NestFileModel
    {
        public string? Id { get; set; }

        public string? Name { get; set; }

        public string? Author { get; set; }

        public string? Description { get; set; }

        public string? Version { get; set; }

        public List<NestEggModel> Egg { get; set; } = [];
    }

    private sealed class NestEggModel
    {
        public string? Id { get; set; }

        public string? Name { get; set; }

        public string? Startup { get; set; }

        public string? DockerImage { get; set; }
    }
}
