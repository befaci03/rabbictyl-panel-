using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Rabbictyl.Core.Abstractions;
using Rabbictyl.Core.Entities;

namespace Rabbictyl.Infrastructure.Persistence.Relational;

public sealed class EfPanelStateStore : IPanelStateStore
{
    private readonly PanelDbContext _db;
    private readonly ILogger<EfPanelStateStore> _logger;

    public EfPanelStateStore(PanelDbContext db, ILogger<EfPanelStateStore> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task<PanelState?> GetAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            return await _db.PanelStates
                .AsNoTracking()
                .FirstOrDefaultAsync(state => state.Key == PanelState.SingletonKey, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // On a fresh database the _state_ table does not exist yet. That is
            // expected: the initializer creates it and writes the version.
            _logger.LogDebug(exception, "The _state_ table is not readable; assuming a fresh database.");

            return null;
        }
    }

    public async Task SaveAsync(PanelState state, CancellationToken cancellationToken = default)
    {
        var existing = await _db.PanelStates
            .FirstOrDefaultAsync(item => item.Key == state.Key, cancellationToken);

        if (existing is null)
        {
            _db.PanelStates.Add(state);
        }
        else
        {
            _db.Entry(existing).CurrentValues.SetValues(state);
        }

        await _db.SaveChangesAsync(cancellationToken);
    }
}
