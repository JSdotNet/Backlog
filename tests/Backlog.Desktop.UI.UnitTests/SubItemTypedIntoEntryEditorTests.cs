using Backlog.Infrastructure.GitHub;

namespace Backlog.Desktop.UI.UnitTests;

/// <summary>
/// The entry's own editor shows the entry without its sub-items, so writing a
/// new <c>##</c> heading into it is a person adding a sub-item — the one gesture
/// the placeholder actually teaches. What must not happen is the app reading
/// that heading straight back as an existing chapter and handing the text below
/// it over a second time, once per keystroke.
/// </summary>
public sealed class SubItemTypedIntoEntryEditorTests : IDisposable
{
    private const string OneSubItem =
        "# Ship the importer\n" +
        "`task` `*medium` `!draft`\n" +
        "\n" +
        "## Read the file\n" +
        "Notes for reading.\n";

    private readonly List<string> _tempDirs = [];

    [Fact]
    public async Task Typing_a_new_sub_item_does_not_duplicate_the_ones_already_there()
    {
        var state = State();
        await state.InitializeAsync();

        var row = new EntryRow { RawText = OneSubItem };
        state.Rows.Add(row);
        state.BeginEdit(row);

        // Typed a character at a time, because that is the only way the app is
        // ever used and the only way the duplication showed up.
        var typed = state.EntryEditText(row) + "\n\n## Write the rows\nNotes for writing.\n";
        for (var i = 1; i <= typed.Length; i++)
        {
            state.OnRawTextInput(row, typed[..i]);
        }

        Assert.Equal(2, EntryTextParser.CountSubItems(row.RawText));
        Assert.Equal(1, Occurrences(row.RawText, "## Read the file"));
        Assert.Equal(1, Occurrences(row.RawText, "## Write the rows"));
    }

    [Fact]
    public async Task Leaving_that_editor_saves_one_entry_rather_than_a_pile_of_them()
    {
        var state = State();
        await state.InitializeAsync();

        var row = new EntryRow { RawText = OneSubItem };
        state.Rows.Add(row);
        state.BeginEdit(row);

        var typed = state.EntryEditText(row) + "\n\n## Write the rows\nNotes for writing.\n";
        for (var i = 1; i <= typed.Length; i++)
        {
            state.OnRawTextInput(row, typed[..i]);
        }

        await state.EndEditAsync(row);

        Assert.Single(state.Rows);
        Assert.Equal(2, Assert.Single(state.Rows).PreviewSubItems.Count);
    }

    private static int Occurrences(string text, string value)
    {
        var count = 0;
        for (var index = text.IndexOf(value, StringComparison.Ordinal); index >= 0;
             index = text.IndexOf(value, index + value.Length, StringComparison.Ordinal))
        {
            count++;
        }

        return count;
    }

    private BacklogDesktopState State()
    {
        var root = Path.Combine(Path.GetTempPath(), "backlog-typed-sub-item", Guid.NewGuid().ToString("n"));
        _tempDirs.Add(root);

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
