using Microsoft.Extensions.Logging;

namespace Backlog.Desktop.UI.Shell;

// The logging source generator cannot see Razor-generated code, so this component's
// [LoggerMessage] methods live here rather than in its @code block (org ADR 0007).
public partial class AppErrorBoundary
{
    [LoggerMessage(Level = LogLevel.Error, Message = "Unhandled error in the page at {Location}")]
    private static partial void LogUnhandledPageError(ILogger logger, Exception exception, string location);
}
