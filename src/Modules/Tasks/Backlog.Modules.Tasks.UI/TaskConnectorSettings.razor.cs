using Microsoft.Extensions.Logging;

namespace Backlog.Desktop.UI.Tasks;

// The logging source generator cannot see Razor-generated code, so this component's
// [LoggerMessage] methods live here rather than in its @code block (org ADR 0007).
public partial class TaskConnectorSettings
{
    [LoggerMessage(Level = LogLevel.Error, Message = "Listing the disconnected connector sources threw.")]
    private static partial void LogDisconnectedSourcesListFailed(ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Error, Message = "Listing the deleted linked tasks threw.")]
    private static partial void LogDeletedLinkedTasksListFailed(ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Error, Message = "Signing in to {Connector} threw.")]
    private static partial void LogSignInFailed(ILogger logger, Exception exception, string connector);

    [LoggerMessage(Level = LogLevel.Error, Message = "Signing out of {Connector} threw.")]
    private static partial void LogSignOutFailed(ILogger logger, Exception exception, string connector);

    [LoggerMessage(Level = LogLevel.Error, Message = "Deleting the tasks of {Connector} {Target} threw.")]
    private static partial void LogSourceTasksDeleteFailed(ILogger logger, Exception exception, string connector, string target);

    [LoggerMessage(Level = LogLevel.Error, Message = "Restoring the deleted tasks of {Connector} {Target} threw.")]
    private static partial void LogDeletedTasksRestoreFailed(ILogger logger, Exception exception, string connector, string target);

    [LoggerMessage(Level = LogLevel.Error, Message = "Syncing {Connector} {Target} threw.")]
    private static partial void LogTargetSyncFailed(ILogger logger, Exception exception, string connector, string target);
}
