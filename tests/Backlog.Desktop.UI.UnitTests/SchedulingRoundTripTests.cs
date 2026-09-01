using Backlog.Infrastructure.GitHub;

namespace Backlog.Desktop.UI.UnitTests;

/// <summary>
/// The scheduling and dependency tokens have to survive the whole way round, and
/// "the whole way round" is longer than it looks: the text is parsed, written on
/// to the aggregate, serialized to YAML frontmatter, read back, mapped to a DTO,
/// and finally rebuilt into text by <see cref="EntryTextParser.ToRawText"/> —
/// which composes the metadata line from DTO fields alone and cannot recover a
/// token it was not told about. A field missing from any one of those layers is
/// destroyed silently on the next flush save, with nothing to see in a log, so
/// the test that matters is the one that drives the real module over the real
/// file store rather than any single layer of it.
/// </summary>
public sealed class SchedulingRoundTripTests : IDisposable
{
    private const string Scheduled =
        "# Deploy SpecManager\n" +
        "`task` `*high` `!ready` `@repos` `#deploy` `due:2026-08-21` " +
        "`remind:2026-08-21T09:00` `repeat:weekly` `myday:2026-08-19` `after:a1b2c3` `after:d4e5f6`\n" +
        "\n" +
        "Ship it before the demo.\n";

    private readonly List<string> _tempDirs = [];
    private readonly List<BacklogDesktopState> _states = [];

    [Fact]
    public async Task An_entry_carrying_every_scheduling_token_still_carries_them_after_a_reload()
    {
        var root = TempRoot();

        var writing = State(root);
        await writing.InitializeAsync();

        var row = new EntryRow { RawText = Scheduled };
        writing.Rows.Add(row);
        writing.BeginEdit(row);
        await writing.EndEditAsync(row);

        // A second state over the same folder, because the point is what the
        // store kept rather than what the first state still happened to hold.
        var reading = State(root);
        await reading.InitializeAsync();

        var reloaded = Assert.Single(reading.Rows);
        var parsed = EntryTextParser.Parse(reloaded.RawText);

        Assert.Equal(new DateOnly(2026, 8, 21), parsed.DueOn);
        Assert.Equal(new DateTime(2026, 8, 21, 9, 0, 0, DateTimeKind.Unspecified), parsed.RemindAt);
        Assert.Equal(new Recurrence(1, RecurrenceUnit.Week), parsed.Recurrence);
        Assert.Equal(new DateOnly(2026, 8, 19), parsed.InMyDayOn);
        Assert.Equal(["a1b2c3", "d4e5f6"], parsed.DependsOn);

        // And the tokens themselves are back in canonical form, because the
        // metadata line is what a person reads and hand-edits.
        Assert.Contains("`due:2026-08-21`", reloaded.RawText, StringComparison.Ordinal);
        Assert.Contains("`remind:2026-08-21T09:00`", reloaded.RawText, StringComparison.Ordinal);
        Assert.Contains("`repeat:weekly`", reloaded.RawText, StringComparison.Ordinal);
        Assert.Contains("`myday:2026-08-19`", reloaded.RawText, StringComparison.Ordinal);
        Assert.Contains("`after:a1b2c3`", reloaded.RawText, StringComparison.Ordinal);
        Assert.Contains("`after:d4e5f6`", reloaded.RawText, StringComparison.Ordinal);
    }

    /// <summary>
    /// A reminder is wall-clock intent, so what comes back out of storage has to
    /// be the same clock reading with no zone attached to it. An offset picked up
    /// on the way through would move the reminder whenever the machine moved.
    /// </summary>
    [Fact]
    public async Task A_reminder_comes_back_as_an_unzoned_wall_clock_time()
    {
        var root = TempRoot();

        var writing = State(root);
        await writing.InitializeAsync();

        var row = new EntryRow { RawText = Scheduled };
        writing.Rows.Add(row);
        writing.BeginEdit(row);
        await writing.EndEditAsync(row);

        var reading = State(root);
        await reading.InitializeAsync();

        var remindAt = EntryTextParser.Parse(Assert.Single(reading.Rows).RawText).RemindAt;

        Assert.NotNull(remindAt);
        Assert.Equal(DateTimeKind.Unspecified, remindAt!.Value.Kind);
        Assert.Equal(new TimeOnly(9, 0), TimeOnly.FromDateTime(remindAt.Value));
    }

    private string TempRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), "backlog-scheduling", Guid.NewGuid().ToString("n"));
        _tempDirs.Add(root);
        return root;
    }

    private BacklogDesktopState State(string root)
    {
        var store = new WorkspaceSettingsStore(root, Path.Combine(root, "settings.json"));
        var settings = new GitHubSettingsStore(Path.Combine(root, "github.json"));
        var state = BacklogTestHost.StateFor(store, new GitHubIntegration(settings, new StubGitHubClient(), new StubProbe()));
        _states.Add(state);
        return state;
    }

    public void Dispose()
    {
        // Before the folders below go: the state arms timed saves, and one that
        // elapsed after its folder was deleted is work the test host is still
        // holding when the run is over. See BacklogDesktopStateLifetimeTests.
        foreach (var state in _states)
        {
            state.Dispose();
        }

        foreach (var dir in _tempDirs.Where(Directory.Exists))
        {
            try { Directory.Delete(dir, recursive: true); } catch (IOException) { }
        }
    }

    private sealed class StubGitHubClient : IGitHubClient
    {
        public Task<GitHubIssue> CreateIssueAsync(
            GitHubRepositoryRef repository,
            string title,
            string? body,
            IEnumerable<string>? labels = null,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<GitHubIssueSnapshot> GetIssueAsync(
            GitHubRepositoryRef repository,
            int number,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<GitHubUploadedFile> UploadFileAsync(
            GitHubRepositoryRef repository,
            string path,
            string branch,
            byte[] content,
            string commitMessage,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed class StubProbe : IGitHubConnectionProbe
    {
        public Task<GitHubConnection> DescribeAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new GitHubConnection(false, "Not connected."));

        public void Invalidate()
        {
        }
    }
}
