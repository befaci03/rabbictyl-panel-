using Rabbictyl.Core;

namespace Rabbictyl.Infrastructure.Maintenance;

public enum MaintenancePhase
{
    Starting,
    Migrating,
    Ready,
    Failed,
}

/// <summary>
/// Describes the current database state so the middleware can serve a
/// maintenance page (with an ETA) while the schema is being created or migrated.
/// </summary>
public sealed record MaintenanceSnapshot(
    MaintenancePhase Phase,
    int FromVersion,
    int ToVersion,
    DateTimeOffset StartedAt,
    TimeSpan EstimatedDuration,
    string? Detail,
    string? Error)
{
    public bool BlocksRequests => Phase is MaintenancePhase.Starting or MaintenancePhase.Migrating or MaintenancePhase.Failed;

    public TimeSpan Remaining
    {
        get
        {
            var remaining = StartedAt + EstimatedDuration - DateTimeOffset.UtcNow;

            return remaining > TimeSpan.Zero ? remaining : TimeSpan.Zero;
        }
    }
}

public sealed class MaintenanceState
{
    private static readonly TimeSpan DefaultDuration = TimeSpan.FromSeconds(30);

    private readonly object _gate = new();
    private MaintenanceSnapshot _current = new(
        MaintenancePhase.Starting,
        PanelSchema.CurrentVersion,
        PanelSchema.CurrentVersion,
        DateTimeOffset.UtcNow,
        DefaultDuration,
        "Preparing the database.",
        null);

    public MaintenanceSnapshot Current
    {
        get
        {
            lock (_gate)
            {
                return _current;
            }
        }
    }

    public void MarkMigrating(int fromVersion, int toVersion, string detail) =>
        Set(new MaintenanceSnapshot(
            MaintenancePhase.Migrating,
            fromVersion,
            toVersion,
            DateTimeOffset.UtcNow,
            DefaultDuration,
            detail,
            null));

    public void MarkReady() =>
        Set(new MaintenanceSnapshot(
            MaintenancePhase.Ready,
            PanelSchema.CurrentVersion,
            PanelSchema.CurrentVersion,
            DateTimeOffset.UtcNow,
            TimeSpan.Zero,
            null,
            null));

    public void MarkFailed(string error, int fromVersion = PanelSchema.CurrentVersion) =>
        Set(new MaintenanceSnapshot(
            MaintenancePhase.Failed,
            fromVersion,
            PanelSchema.CurrentVersion,
            DateTimeOffset.UtcNow,
            TimeSpan.Zero,
            "The panel could not prepare the database.",
            error));

    private void Set(MaintenanceSnapshot snapshot)
    {
        lock (_gate)
        {
            _current = snapshot;
        }
    }
}
