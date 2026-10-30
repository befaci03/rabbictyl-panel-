using RabbiCtyl.Core.Abstractions;
using RabbiCtyl.Core.Entities;
using RabbiCtyl.Core.Extensions;

namespace RabbiCtyl.Infrastructure.Extensions;

/// <summary>
/// Merges nest files (TOML) with panel templates (database). Editing a nest egg
/// stores a database copy with the same UUID that shadows the file version, so
/// the TOML file itself is never rewritten.
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
        var stored = (await _eggs.ListAsync(cancellationToken))
            .ToDictionary(egg => egg.Uuid);
        var templates = new List<GameTemplate>();
        var seen = new HashSet<Guid>();

        foreach (var nest in await _nests.ListAsync(cancellationToken))
        {
            foreach (var egg in nest.Eggs)
            {
                if (!seen.Add(egg.Uuid))
                {
                    continue;
                }

                var storedCopy = stored.GetValueOrDefault(egg.Uuid);
                var effective = storedCopy ?? egg;

                templates.Add(new GameTemplate(
                    effective,
                    nest.Id,
                    nest.Name,
                    FromNest: true,
                    EditedAt: storedCopy?.UpdatedAt));
            }
        }

        foreach (var egg in stored.Values)
        {
            if (seen.Add(egg.Uuid))
            {
                templates.Add(new GameTemplate(egg, null, null, FromNest: false, EditedAt: egg.UpdatedAt));
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
        var stored = await _eggs.FindByUuidAsync(uuid, cancellationToken);

        foreach (var nest in await _nests.ListAsync(cancellationToken))
        {
            var nestEgg = nest.Eggs.FirstOrDefault(item => item.Uuid == uuid);

            if (nestEgg is not null)
            {
                return new GameTemplate(
                    stored ?? nestEgg,
                    nest.Id,
                    nest.Name,
                    FromNest: true,
                    EditedAt: stored?.UpdatedAt);
            }
        }

        return stored is null
            ? null
            : new GameTemplate(stored, null, null, FromNest: false, EditedAt: stored.UpdatedAt);
    }
}
