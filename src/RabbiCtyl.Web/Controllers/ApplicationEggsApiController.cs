using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RabbiCtyl.Core.Abstractions;
using RabbiCtyl.Core.Entities;
using RabbiCtyl.Core.Extensions;

namespace RabbiCtyl.Web.Controllers;

/// <summary>
/// Application API for game templates. Editing a nest template stores a panel
/// copy that shadows the TOML egg; the file itself is never rewritten.
/// </summary>
[ApiController]
[IgnoreAntiforgeryToken]
[Authorize(Roles = "admin")]
[Route("api/application/eggs")]
public sealed class ApplicationEggsApiController : ControllerBase
{
    private readonly IGameTemplateCatalog _templates;
    private readonly IEggStore _eggs;

    public ApplicationEggsApiController(IGameTemplateCatalog templates, IEggStore eggs)
    {
        _templates = templates;
        _eggs = eggs;
    }

    [HttpGet]
    public async Task<IActionResult> List(CancellationToken cancellationToken) =>
        Ok(new { data = (await _templates.ListAsync(cancellationToken)).Select(ToDto) });

    [HttpGet("{uuid}")]
    public async Task<IActionResult> Get(string uuid, CancellationToken cancellationToken)
    {
        var template = await FindAsync(uuid, cancellationToken);

        return template is null ? NotFound() : Ok(ToDto(template));
    }

    [HttpPost]
    public async Task<IActionResult> Create(
        [FromBody] CreateEggRequest request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Name) || string.IsNullOrWhiteSpace(request.Startup))
        {
            return ValidationProblem("A name and a startup command are required.");
        }

        var egg = new Egg
        {
            Uuid = Guid.NewGuid(),
            Name = request.Name.Trim(),
            Description = request.Description,
            Author = request.Author ?? string.Empty,
            DockerImage = request.Image,
            DockerImagesJson = System.Text.Json.JsonSerializer.Serialize(
                new Dictionary<string, string> { ["default"] = request.Image }),
            StartupCommand = request.Startup.Trim(),
            ScriptContainer = string.IsNullOrWhiteSpace(request.ScriptContainer)
                ? "alpine:3.20"
                : request.ScriptContainer,
            ScriptEntrypoint = string.IsNullOrWhiteSpace(request.ScriptEntrypoint)
                ? "ash"
                : request.ScriptEntrypoint,
            ScriptInstall = request.InstallScript,
            ProcessConfigurationJson = EggTemplate.BuildProcessConfiguration(
                request.DonePatterns ?? [],
                request.StopType ?? "command",
                request.StopValue ?? "stop"),
            VariablesJson = EggTemplate.BuildVariables(
                (request.Variables ?? new Dictionary<string, string>())
                    .Select(pair => $"{pair.Key}={pair.Value}")),
        };

        await _eggs.AddAsync(egg, cancellationToken);

        return CreatedAtAction(
            nameof(Get),
            new { uuid = egg.Uuid },
            ToDto(new GameTemplate(egg, null, null, FromNest: false)));
    }

    [HttpPatch("{uuid}")]
    public async Task<IActionResult> Update(
        string uuid,
        [FromBody] UpdateEggRequest request,
        CancellationToken cancellationToken)
    {
        var template = await FindAsync(uuid, cancellationToken);

        if (template is null)
        {
            return NotFound();
        }

        var egg = await _eggs.FindByUuidAsync(template.Egg.Uuid, cancellationToken);
        var created = egg is null;

        // A nest egg only exists in the TOML file; the first edit is stored as a
        // panel copy. Converting panel changes back to TOML is lossy, so the copy
        // shadows the file instead of writing to it.
        egg ??= EggTemplate.CopyOf(template.Egg);

        if (request.Name is not null)
        {
            egg.Name = request.Name.Trim();
        }

        if (request.Description is not null)
        {
            egg.Description = request.Description;
        }

        if (request.Author is not null)
        {
            egg.Author = request.Author;
        }

        if (request.Startup is not null)
        {
            egg.StartupCommand = request.Startup.Trim();
        }

        if (request.Image is not null)
        {
            egg.DockerImage = request.Image;
            egg.DockerImagesJson = System.Text.Json.JsonSerializer.Serialize(
                new Dictionary<string, string> { ["default"] = request.Image });
        }

        if (created)
        {
            await _eggs.AddAsync(egg, cancellationToken);
        }
        else
        {
            await _eggs.UpdateAsync(egg, cancellationToken);
        }

        return Ok(ToDto(new GameTemplate(egg, template.NestId, template.NestName, template.FromNest)));
    }

    [HttpDelete("{uuid}")]
    public async Task<IActionResult> Delete(string uuid, CancellationToken cancellationToken)
    {
        var template = await FindAsync(uuid, cancellationToken);

        if (template is null)
        {
            return NotFound();
        }

        var stored = await _eggs.FindByUuidAsync(template.Egg.Uuid, cancellationToken);

        if (stored is null)
        {
            return Conflict(new
            {
                error = "This template only exists in a nest file. Edit it once to store a panel copy before deleting.",
            });
        }

        await _eggs.DeleteAsync(stored, cancellationToken);

        return NoContent();
    }

    private async Task<GameTemplate?> FindAsync(string uuid, CancellationToken cancellationToken) =>
        Guid.TryParse(uuid, out var value) ? await _templates.FindAsync(value, cancellationToken) : null;

    private static object ToDto(GameTemplate template)
    {
        var egg = template.Egg;

        return new
        {
            uuid = egg.Uuid,
            name = egg.Name,
            description = egg.Description,
            author = egg.Author,
            startup = egg.StartupCommand,
            image = EggTemplate.Image(egg),
            source = template.FromNest ? "nest" : "panel",
            nest = template.NestId,
            script = new
            {
                container = egg.ScriptContainer,
                entrypoint = egg.ScriptEntrypoint,
                install = egg.ScriptInstall,
            },
            variables = EggTemplate.Variables(egg).Select(variable => new
            {
                name = variable.Name,
                env_variable = variable.EnvVariable,
                default_value = variable.DefaultValue,
                user_viewable = variable.UserViewable,
                user_editable = variable.UserEditable,
            }),
        };
    }

    public sealed class CreateEggRequest
    {
        public string Name { get; set; } = string.Empty;
        public string? Description { get; set; }
        public string? Author { get; set; }
        public string Startup { get; set; } = string.Empty;
        public string Image { get; set; } = "alpine:3.20";
        public string? ScriptContainer { get; set; }
        public string? ScriptEntrypoint { get; set; }
        public string? InstallScript { get; set; }
        public Dictionary<string, string>? Variables { get; set; }
        public List<string>? DonePatterns { get; set; }
        public string? StopType { get; set; }
        public string? StopValue { get; set; }
    }

    public sealed class UpdateEggRequest
    {
        public string? Name { get; set; }
        public string? Description { get; set; }
        public string? Author { get; set; }
        public string? Startup { get; set; }
        public string? Image { get; set; }
    }
}
