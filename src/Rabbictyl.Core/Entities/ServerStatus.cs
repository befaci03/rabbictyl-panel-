namespace Rabbictyl.Core.Entities;

public enum ServerStatus
{
    Installing,
    InstallFailed,
    Suspended,
    RestoringBackup,
    Running,
    Offline,
    Starting,
    Stopping,
}
