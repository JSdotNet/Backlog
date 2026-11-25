using Microsoft.Extensions.Logging;

namespace Backlog.Modules.Roadmap.UI;

// The logging source generator cannot see Razor-generated code, so this component's
// [LoggerMessage] methods live here rather than in its @code block (org ADR 0007).
public partial class RoadmapBand
{
    [LoggerMessage(Level = LogLevel.Warning, Message = "The roadmap could not store the windows its effort-placed work now makes; it draws them from what is stored.")]
    private static partial void LogEffortWindowsNotStored(ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Warning, Message = "The roadmap could not read the actual hours from {From} through {Through}; its begun heads show no hours.")]
    private static partial void LogActualHoursNotRead(ILogger logger, Exception exception, DateOnly from, DateOnly through);
}
