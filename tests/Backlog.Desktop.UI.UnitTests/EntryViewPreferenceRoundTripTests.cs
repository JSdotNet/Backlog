using Backlog.Infrastructure.GitHub;

namespace Backlog.Desktop.UI.UnitTests;

/// <summary>
/// The <c>view:</c> token has to survive the whole way round, and "the whole way
/// round" is the same seven layers <see cref="SchedulingRoundTripTests"/> describes:
/// the text is parsed, written on to the aggregate, serialized to YAML frontmatter,
/// read back, mapped to a DTO, and finally rebuilt into text by
/// <see cref="EntryTextParser.ToRawText"/> — which composes the metadata line from
/// DTO fields alone and cannot recover a token it was not told about.
/// <para>
/// This test was written before the token existed, precisely because the failure it
/// guards is silent: a token added to the parser and to nothing else parses,
/// displays, and is then destroyed by the next flush save with nothing in a log to
/// notice it by. A green parser test would have said the feature worked.
/// </para>
/// <para>
/// What is being persisted here is a display preference rather than a fact about
/// the work — see <c>.design/content-editing.md#scheduling-and-dependency-tokens</c>
/// and <c>.domain/backlog/domain.md</c>. It rides on the metadata line anyway
/// because the markdown is canonical: a preference kept in a sidecar would not
/// survive the file being shared, and the reader who opened the entry somewhere
/// else would get somebody else's default.
/// </para>
/// </summary>
public sealed class EntryViewPreferenceRoundTripTests : IDisposable
{
    private const string PreferringNotes =
        "# Deploy SpecManager\n" +
        "`task` `*high` `!ready` `@repos` `view:notes`\n" +
        "\n" +
        "Ship it before the demo.\n" +
        "\n" +
        "## Warm the cache\n" +
        "So the first request is not the slow one.\n";

    private readonly List<string> _tempDirs = [];

    [Fact]
    public async Task An_entry_that_prefers_the_markdown_block_still_prefers_it_after_a_reload()
    {
        var root = TempRoot();

        var writing = State(root);
        await writing.InitializeAsync();

        var row = new EntryRow { RawText = PreferringNotes };
        writing.Rows.Add(row);
        writing.BeginEdit(row);
        await writing.EndEditAsync(row);

        // A second state over the same folder, because the point is what the store
        // kept rather than what the first state still happened to hold.
        var reading = State(root);
        await reading.InitializeAsync();

        var reloaded = Assert.Single(reading.Rows);

        Assert.Equal(EntryView.Notes, EntryTextParser.Parse(reloaded.RawText).View);
        Assert.Equal(EntryView.Notes, reloaded.PreviewView);

        // And the token itself is back in canonical form, because the metadata line
        // is what a person reads and hand-edits.
        Assert.Contains("`view:notes`", reloaded.RawText, StringComparison.Ordinal);
    }

    /// <summary>
    /// Absent means absent here too: an entry nobody has expressed a preference
    /// about carries no token, and acquires none by being saved. A default written
    /// down is a preference the reader never made, and it would then have to be
    /// unwritten before the real default could ever change.
    /// </summary>
    [Fact]
    public async Task An_entry_with_no_preference_is_not_given_one_by_being_saved()
    {
        var root = TempRoot();

        var writing = State(root);
        await writing.InitializeAsync();

        var row = new EntryRow { RawText = "# Plain\n`task` `*medium` `!draft`\n\nJust prose.\n" };
        writing.Rows.Add(row);
        writing.BeginEdit(row);
        await writing.EndEditAsync(row);

        var reading = State(root);
        await reading.InitializeAsync();

        var reloaded = Assert.Single(reading.Rows);

        Assert.DoesNotContain("view:", reloaded.RawText, StringComparison.Ordinal);
        Assert.Null(EntryTextParser.Parse(reloaded.RawText).View);
    }

    /// <summary>Switching the view is a metadata rewrite like every other one, so
    /// the token it writes has to come back out of the store the same way a
    /// hand-typed one does.</summary>
    [Fact]
    public async Task Choosing_a_view_writes_the_token_and_the_store_keeps_it()
    {
        var root = TempRoot();

        var writing = State(root);
        await writing.InitializeAsync();

        var row = new EntryRow { RawText = "# Plain\n`task` `*medium` `!draft`\n\nJust prose.\n" };
        writing.Rows.Add(row);
        writing.BeginEdit(row);
        await writing.EndEditAsync(row);

        await writing.ChangeViewAsync(row, EntryView.Steps);

        Assert.Contains("`view:steps`", row.RawText, StringComparison.Ordinal);

        var reading = State(root);
        await reading.InitializeAsync();

        Assert.Equal(EntryView.Steps, Assert.Single(reading.Rows).PreviewView);
    }

    /// <summary>A value the grammar does not know is refused rather than swallowed,
    /// on the same terms as <c>due:friday</c>: the field stays unset and the reading
    /// line says the words were not understood.</summary>
    [Fact]
    public void A_view_token_nobody_can_read_is_refused_rather_than_guessed()
    {
        var parsed = EntryTextParser.Parse("# Plain\n`task` `view:kanban`\n");

        Assert.Null(parsed.View);
        Assert.Contains(parsed.Unreadable ?? [], token => token is { Name: "view", Value: "kanban" });
    }

    private string TempRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), "backlog-entry-view", Guid.NewGuid().ToString("n"));
        _tempDirs.Add(root);
        return root;
    }

    private static BacklogDesktopState State(string root)
    {
        var store = new WorkspaceSettingsStore(root, Path.Combine(root, "settings.json"));
        var settings = new GitHubSettingsStore(Path.Combine(root, "github.json"));
        return BacklogTestHost.StateFor(store, new GitHubIntegration(settings, new StubGitHubClient(), new StubProbe()));
    }

    public void Dispose()
    {
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
