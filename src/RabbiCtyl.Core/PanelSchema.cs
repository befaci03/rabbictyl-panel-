namespace RabbiCtyl.Core;

/// <summary>
/// Schema versions understood by this build. Bump <see cref="CurrentVersion"/>
/// whenever the stored schema changes so an older database is migrated (and
/// served the maintenance page) instead of being used as-is.
/// </summary>
public static class PanelSchema
{
    public const int CurrentVersion = 1;
}
