using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Rabbictyl.Web.Services;

namespace Rabbictyl.Web.Controllers;

/// <summary>
/// Administration → Games: manage the nest files on disk.
///
/// The flow is preview → confirm → stream: <c>preview</c> fetches every URL
/// server-side and returns the warnings plus one row per URL, <c>start</c>
/// turns the confirmed preview batch into an import job, and <c>stream</c>
/// sends Server-Sent Events while the job writes the files into the nests
/// folder so the administrator sees progress in real time. <c>nests/list</c>
/// and <c>nests/delete</c> manage the files that are already on disk.
/// </summary>
[Authorize(Roles = "admin")]
[Route("admin/games")]
public sealed class AdminNestController : Controller
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly NestImportService _imports;
    private readonly ILogger<AdminNestController> _logger;

    public AdminNestController(
        NestImportService imports,
        ILogger<AdminNestController> logger)
    {
        _imports = imports;
        _logger = logger;
    }

    public sealed record PreviewRequest(string[]? Urls);

    public sealed record StartRequest(string? BatchId);

    public sealed record DeleteRequest(string? File);

    /// <summary>The nest files currently in the nests folder.</summary>
    [HttpGet("nests/list")]
    public IActionResult ListNests() => Ok(new { nests = _imports.ListLocal() });

    /// <summary>
    /// Deletes one nest file. Only a plain <c>*.toml</c> file name inside the
    /// nests folder is accepted, so no path outside of it can be touched.
    /// </summary>
    [HttpPost("nests/delete")]
    public IActionResult DeleteNest([FromBody] DeleteRequest? request)
    {
        var file = request?.File?.Trim() ?? string.Empty;
        var (ok, error) = _imports.DeleteLocal(file);

        if (!ok)
        {
            return BadRequest(new { error });
        }

        _logger.LogInformation("Administrator {User} deleted nest file {File}.", User.Identity?.Name, file);

        return Ok(new { file });
    }

    [HttpPost("import/preview")]
    public async Task<IActionResult> Preview([FromBody] PreviewRequest? request, CancellationToken cancellationToken)
    {
        var urls = request?.Urls?
            .Where(url => !string.IsNullOrWhiteSpace(url))
            .Select(url => url.Trim())
            .ToArray() ?? [];

        if (urls.Length == 0)
        {
            return BadRequest(new { error = "Provide at least one URL to import." });
        }

        if (urls.Length > NestImportService.MaxUrls)
        {
            return BadRequest(new { error = $"At most {NestImportService.MaxUrls} URLs can be imported at once." });
        }

        var result = await _imports.PreviewAsync(urls, cancellationToken);

        return Ok(new
        {
            batchId = result.BatchId,
            warnings = result.Warnings,
            items = result.Items,
        });
    }

    [HttpPost("import/start")]
    public IActionResult Start([FromBody] StartRequest? request)
    {
        if (!_imports.TryCreateJob(request?.BatchId ?? string.Empty, out var job) || job is null)
        {
            return BadRequest(new { error = "The preview expired. Run the preview again." });
        }

        _logger.LogInformation("Administrator {User} started nest import job {JobId}.", User.Identity?.Name, job.Id);

        return Ok(new { jobId = job.Id });
    }

    /// <summary>Server-Sent Events stream for one import job.</summary>
    [HttpGet("import/stream")]
    public async Task Stream(string? jobId, CancellationToken cancellationToken)
    {
        if (!_imports.TryGetJob(jobId ?? string.Empty, out var job) || job is null)
        {
            Response.StatusCode = StatusCodes.Status404NotFound;
            return;
        }

        Response.Headers.CacheControl = "no-cache";
        Response.Headers.Append("X-Accel-Buffering", "no");
        Response.ContentType = "text/event-stream";

        async Task Emit(string eventName, object data)
        {
            var json = JsonSerializer.Serialize(data, JsonOptions);
            await Response.WriteAsync($"event: {eventName}\ndata: {json}\n\n", cancellationToken);
            await Response.Body.FlushAsync(cancellationToken);
        }

        try
        {
            // A reconnect after the run finished replays the stored results.
            if (job.Completed)
            {
                await ReplayAsync(job, Emit);
                return;
            }

            // Only one connection may execute a job; the second one is told off.
            if (!job.TryBegin())
            {
                await Emit("error", new { message = "This import is already running in another connection." });
                return;
            }

            await _imports.ExecuteAsync(job, Emit, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            // The browser went away; completed results stay available for a reconnect.
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Nest import stream for job {JobId} failed.", job.Id);

            try
            {
                await Emit("error", new { message = $"The import stopped unexpectedly: {exception.Message}" });
                await ReplayAsync(job, Emit);
            }
            catch
            {
                // Connection already gone.
            }
        }
    }

    /// <summary>Replays the stored per-file results and the final counts.</summary>
    private static async Task ReplayAsync(
        NestImportService.ImportJob job,
        Func<string, object, Task> emit)
    {
        var snapshot = job.Snapshot();

        foreach (var result in snapshot)
        {
            await emit("item", result);
        }

        await emit("done", new
        {
            imported = snapshot.Count(result => result.State == "done"),
            failed = snapshot.Count(result => result.State == "failed"),
            total = snapshot.Length,
        });
    }
}
