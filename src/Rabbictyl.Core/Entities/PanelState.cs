namespace Rabbictyl.Core.Entities;

/// <summary>
/// Single-row bookkeeping table (<c>_state_</c>) that records which schema
/// version the database currently holds. A mismatch triggers the migration
/// screen instead of letting an old schema serve requests.
/// </summary>
public sealed class PanelState
{
    public const string SingletonKey = "state";

    public string Key { get; set; } = SingletonKey;

    public int Version { get; set; }

    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;

    public DateTimeOffset? MigrationStartedAt { get; set; }

    public int MigrationEtaSeconds { get; set; }
}
