using RabbiCtyl.Core.Abstractions;
using RabbiCtyl.Core.Entities;
using RabbiCtyl.Core.Extensions;

namespace RabbiCtyl.Infrastructure.Extensions;

/// <summary>
/// Merges nest files (read-only, TOML) with panel-created templates (database).
/// Both expose the same <see cref="Egg"/> shape so creating a server only needs
/// to call <see cref="FindAsync"/>.
/// </summary>
public sealed class GameTemplateCatalog : IGameTemplateCatalog
{
    private readonly IEggStore _eggs;
    private readonly INestCatalog _nests;

    public GameTemplateCatalog(IEggStore eggs, INestCatalog nests)
    {
        _eggs = eggs;
        _nests = nests;
    }

    public async Task<IReadOnlyList<GameTemplate>> ListAsync(CancellationToken cancellationToken = default)
    {
        var templates = new List<GameTemplate>();
        var seen = new HashSet<Guid>();

        foreach (var nest in await _nests.ListAsync(cancellationToken))
        {
            foreach (var egg in nest.Eggs)
            {
                if (seen.Add(egg.Uuid))
                {
                    templates.Add(new GameTemplate(egg, nest.Id, nest.Name, FromNest: true));
                }
            }
        }

        foreach (var egg in await _eggs.ListAsync(cancellationToken))
        {
            if (seen.Add(egg.Uuid))
            {
                templates.Add(new GameTemplate(egg, null, null, FromNest: false));
            }
        }

        return templates
            .OrderBy(template => template.FromNest ? 0 : 1)
            .ThenBy(template => template.NestName ?? string.Empty, StringComparer.OrdinalIgnoreCase)
            .ThenBy(template => template.Egg.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public async Task<GameTemplate?> FindAsync(Guid uuid, CancellationToken cancellationToken = default)
    {
        foreach (var nest in await _nests.ListAsync(cancellationToken))
        {
            var egg = nest.Eggs.FirstOrDefault(item => item.Uuid == uuid);

            if (egg is not null)
            {
                return new GameTemplate(egg, nest.Id, nest.Name, FromNest: true);
            }
        }

        var stored = await _eggs.FindByUuidAsync(uuid, cancellationToken);

        return stored is null ? null : new GameTemplate(stored, null, null, FromNest: false);
    }
}
