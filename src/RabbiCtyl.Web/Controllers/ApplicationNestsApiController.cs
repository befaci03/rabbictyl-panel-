using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RabbiCtyl.Core.Abstractions;

namespace RabbiCtyl.Web.Controllers;

/// <summary>
/// Read-only view of the nest files loaded from <c>nests/*.toml</c>. Editing a
/// nest means editing the TOML file, so there is no write endpoint here.
/// </summary>
[ApiController]
[Authorize(Roles = "admin")]
[Route("api/application/nests")]
public sealed class ApplicationNestsApiController : ControllerBase
{
    private readonly INestCatalog _nests;

    public ApplicationNestsApiController(INestCatalog nests)
    {
        _nests = nests;
    }

    [HttpGet]
    public async Task<IActionResult> List(CancellationToken cancellationToken) =>
        Ok(new
        {
            data = (await _nests.ListAsync(cancellationToken)).Select(nest => new
            {
                id = nest.Id,
                name = nest.Name,
                author = nest.Author,
                description = nest.Description,
                eggs = nest.Eggs.Select(egg => new
                {
                    uuid = egg.Uuid,
                    name = egg.Name,
                    startup = egg.StartupCommand,
                }),
            }),
        });

    [HttpGet("{id}")]
    public async Task<IActionResult> Get(string id, CancellationToken cancellationToken)
    {
        var nest = (await _nests.ListAsync(cancellationToken))
            .FirstOrDefault(item => string.Equals(item.Id, id, StringComparison.OrdinalIgnoreCase));

        if (nest is null)
        {
            return NotFound();
        }

        return Ok(new
        {
            id = nest.Id,
            name = nest.Name,
            author = nest.Author,
            description = nest.Description,
            eggs = nest.Eggs.Select(egg => new
            {
                uuid = egg.Uuid,
                name = egg.Name,
                description = egg.Description,
                startup = egg.StartupCommand,
            }),
        });
    }
}
