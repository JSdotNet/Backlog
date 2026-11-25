using System.Text.RegularExpressions;

namespace Backlog.ArchitectureTests;

/// <summary>
/// Code that needs the current time asks an injected <see cref="TimeProvider"/>, and tests
/// hand it a <c>FakeTimeProvider</c> (app-defaults ADR 0005). A direct read of the static
/// clock cannot be tested at a chosen moment, and twice at the same one, which is what
/// "today", an age or an expiry needs.
///
/// <para><c>DateTime.Now</c> and <c>DateTime.UtcNow</c> are the reads the decision names, and
/// <c>src/</c> holds none: those are banned outright. <c>DateTime.Today</c> and
/// <c>DateTimeOffset.Now</c> / <c>UtcNow</c> are the same read in other clothes; the files that
/// still make one predate this rule and are listed in <see cref="PredatesTheRule"/>, so a new
/// file cannot add one and the list only shrinks.</para>
///
/// <para>A source scan rather than an analyzer because the rule covers the MAUI heads and the
/// Razor components too, which no analyzer test project builds.</para>
/// </summary>
public partial class ClockReadTests
{
    /// <summary>
    /// The files that still read <c>DateTime.Today</c> or <c>DateTimeOffset.Now</c> /
    /// <c>UtcNow</c> directly, as of the rule landing. Moving one onto an injected
    /// <see cref="TimeProvider"/> takes it off this list; nothing is ever added to it.
    /// </summary>
    private static readonly string[] PredatesTheRule =
    [
        "src/App/Backlog.Desktop.UI/Feedback/FeedbackReporter.cs",
        "src/App/Backlog.Desktop.UI/Mcp/McpCallAudit.cs",
        "src/App/Backlog.Desktop.UI/Shell/AppErrorBoundary.razor",
        "src/App/Backlog.Mobile/Platforms/Android/RecordingService.cs",
        "src/Core/Backlog.UI.Components/Inputs/DayCalendar.razor",
        "src/Core/Backlog.UI.Components/Shell/SyncStatusLine.razor",
        "src/Harness/Backlog.AzureFoundry.TestService/Program.cs",
        "src/Infrastructure/Backlog.Infrastructure.FileSystem/Workspace/DevbookSnapshotCache.cs",
        "src/Infrastructure/Backlog.Infrastructure.GitHub/GitHubClient.cs",
        "src/Infrastructure/Backlog.Infrastructure.Sessions/LocalDeliverySurfaceLifecycle.cs",
        "src/Infrastructure/Backlog.Infrastructure.Sessions/ValidationQuestions.cs",
        "src/Infrastructure/Backlog.Infrastructure.Sqlite/Notifications/SqliteNotificationRepository.cs",
        "src/Infrastructure/Backlog.Infrastructure.Sqlite/Progress/SqliteGoalRepository.cs",
        "src/Infrastructure/Backlog.Infrastructure.Sqlite/Progress/SqliteImpedimentRepository.cs",
        "src/Infrastructure/Backlog.Infrastructure.Sqlite/Roadmap/SqliteRoadmapPlanRepository.cs",
        "src/Infrastructure/Backlog.Infrastructure.Sqlite/Sessions/SqliteClaudeApiRequestStore.cs",
        "src/Modules/DevPc/Backlog.Modules.DevPc.UI/ToolsPane.razor",
        "src/Modules/Inbox/Backlog.Modules.Inbox/DomainModels/InboxGroup.cs",
        "src/Modules/Inbox/Backlog.Modules.Inbox/DomainModels/InboxItem.cs",
        "src/Modules/Inbox/Backlog.Modules.Inbox/DomainModels/InboxList.cs",
        "src/Modules/Roadmap/Backlog.Modules.Roadmap.UI/RoadmapBand.razor",
        "src/Modules/Roadmap/Backlog.Modules.Roadmap.UI/RoadmapDaysOffDialog.razor",
        "src/Modules/Roadmap/Backlog.Modules.Roadmap.UI/RoadmapHoursReport.razor",
        "src/Modules/Roadmap/Backlog.Modules.Roadmap.UI/RoadmapItemEditor.razor",
        "src/Modules/Roadmap/Backlog.Modules.Roadmap.UI/RoadmapMilestoneEditor.razor",
        "src/Modules/Sessions/Backlog.Modules.Sessions.Abstractions/Sessions.cs",
        "src/Modules/Sessions/Backlog.Modules.Sessions.UI/SchedulesPage.razor",
        "src/Modules/Sessions/Backlog.Modules.Sessions.UI/SessionsPane.razor",
        "src/Modules/Sync/Backlog.Modules.Sync.Api/Security/HmacSyncCursorCodec.cs",
        "src/Modules/Sync/Backlog.Modules.Sync.UI/Settings/DevicesSettings.razor",
        "src/Modules/Tasks/Backlog.Modules.Tasks/DomainModels/TaskItem.cs",
        "src/Modules/Tasks/Backlog.Modules.Tasks/Services/RecurrencePolicy.cs",
    ];

    [Fact]
    public void No_source_file_reads_DateTime_Now_or_UtcNow()
    {
        var offenders = Reads(DateTimeNowRead()).ToList();

        Assert.True(
            offenders.Count == 0,
            "These lines read the static clock, which app-defaults ADR 0005 rules out. Inject a "
            + "TimeProvider and read GetUtcNow() or GetLocalNow() from it; a test passes a FakeTimeProvider:\n"
            + string.Join('\n', offenders));
    }

    [Fact]
    public void No_new_file_reads_the_clock_directly()
    {
        var offenders = Reads(AnyClockRead())
            .Where(read => !PredatesTheRule.Contains(read.File, StringComparer.Ordinal))
            .ToList();

        Assert.True(
            offenders.Count == 0,
            "These lines read DateTime.Today or DateTimeOffset.Now/UtcNow directly. Inject a "
            + "TimeProvider instead (app-defaults ADR 0005); the predating list only shrinks:\n"
            + string.Join('\n', offenders));
    }

    /// <summary>An entry that no longer reads the clock is one that was fixed: it comes off the
    /// list in the same change, or the list stops saying what is left.</summary>
    [Fact]
    public void Every_predating_file_still_reads_the_clock()
    {
        var reading = Reads(AnyClockRead()).Select(read => read.File).ToHashSet(StringComparer.Ordinal);

        var stale = PredatesTheRule.Where(file => !reading.Contains(file)).ToList();

        Assert.True(
            stale.Count == 0,
            "These files no longer read the clock directly; take them off PredatesTheRule: "
            + string.Join(", ", stale));
    }

    private static IEnumerable<Read> Reads(Regex pattern)
    {
        var src = new DirectoryInfo(Path.Combine(Repository.Root.FullName, "src"));
        var files = Repository.SourceFilesUnder(src, ".cs", ".razor").ToList();

        Assert.NotEmpty(files);

        foreach (var file in files)
        {
            var lines = File.ReadAllLines(file.FullName);

            for (var index = 0; index < lines.Length; index++)
            {
                if (pattern.IsMatch(CodeOf(lines[index])))
                {
                    yield return new Read(Repository.RelativePath(file), index + 1);
                }
            }
        }
    }

    /// <summary>The line with a trailing <c>//</c> comment cut off, so a comment that names
    /// the clock is not a read of it.</summary>
    private static string CodeOf(string line)
    {
        var comment = line.IndexOf("//", StringComparison.Ordinal);
        return comment < 0 ? line : line[..comment];
    }

    private sealed record Read(string File, int Line)
    {
        public override string ToString() => $"{File}:{Line}";
    }

    [GeneratedRegex(@"\bDateTime\.(?:Now|UtcNow)\b")]
    private static partial Regex DateTimeNowRead();

    [GeneratedRegex(@"\bDateTime(?:Offset)?\.(?:Now|UtcNow|Today)\b")]
    private static partial Regex AnyClockRead();
}
