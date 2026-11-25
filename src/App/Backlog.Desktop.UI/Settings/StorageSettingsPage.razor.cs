using Microsoft.Extensions.Logging;

namespace Backlog.Desktop.UI.Shell;

// The logging source generator cannot see Razor-generated code, so this component's
// [LoggerMessage] methods live here rather than in its @code block (org ADR 0007).
public partial class StorageSettingsPage
{
    [LoggerMessage(Level = LogLevel.Information, Message = "Backup is not composed in this host, so the backup controls are not offered.")]
    private static partial void LogBackupNotComposed(ILogger logger, Exception exception);
}
