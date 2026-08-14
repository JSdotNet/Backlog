using Backlog.Desktop.UI.Services;
using Backlog.Infrastructure.GitHub;
using Backlog.Modules.Backlog;
using Backlog.Modules.Backlog.DomainModels;

namespace Backlog.Desktop.UI.UnitTests;

/// <summary>
/// Repository-authored <c>.backlog</c> rows are editable and persisted back to
/// their source file. Edits survive reload because the write targets the correct
/// segment in the correct file, not a local store copy.
/// </summary>
[Collection(BacklogStoreCollection.Name)]
public sealed class RepositoryBacklogWriteTests : IDisposable
{
    private readonly List<string> _tempDirs = [];

    public void Dispose()
    {
        foreach (var dir in _tempDirs.Where(Directory.Exists))
        {
            try { Directory.Delete(dir, recursive: true); } catch (IOException) { }
        }
    }

    [Fact]
    public async Task Repository_backlog_row_is_editable()
    {
        var harness = Build("# Roadmap planning\n\n```meta\nstatus: active\n```\n\n## Add the view\n");
        await harness.State.InitializeAsync();

        var row = Assert.Single(harness.State.Rows);

        // The row must accept editing — it is no longer read-only.
        harness.State.BeginEdit(row);
        Assert.NotNull(harness.State.EditingRow);
        Assert.Same(row, harness.State.EditingRow);
    }

    [Fact]
    public async Task Editing_a_repository_row_persists_back_to_the_source_file()
    {
        var harness = Build("# Roadmap planning\n\n```meta\nstatus: active\n```\n\nOriginal prose.\n");
        await harness.State.InitializeAsync();

        var row = Assert.Single(harness.State.Rows);

        // Change status via metadata selector — should write back.
        await harness.State.ChangeStatusAsync(row, EntryStatus.Done);

        // Reload the file and verify the change is persisted.
        var fileContent = File.ReadAllText(harness.BacklogFile);
        Assert.Contains("status: done", fileContent, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Toggling_a_repository_sub_item_persists_back()
    {
        var harness = Build("# Roadmap planning\n\n```meta\nstatus: active\n```\n\n## [ ] Add the view\n");
        await harness.State.InitializeAsync();

        var row = Assert.Single(harness.State.Rows);
        Assert.Equal(1, row.PreviewSubItems.Count);

        await harness.State.ToggleSubItemAsync(row, 0);

        var fileContent = File.ReadAllText(harness.BacklogFile);
        Assert.Contains("[x]", fileContent, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Origin_carries_file_path_and_segment_index()
    {
        var harness = Build("# First\n\n```meta\nstatus: draft\n```\n\n# Second\n\n```meta\nstatus: active\n```\n");
        await harness.State.InitializeAsync();

        Assert.Equal(2, harness.State.Rows.Count);
        var first = harness.State.Rows[0];
        var second = harness.State.Rows[1];

        Assert.NotNull(first.Origin);
        Assert.NotNull(second.Origin);
        Assert.Equal(0, first.Origin!.SegmentIndex);
        Assert.Equal(1, second.Origin!.SegmentIndex);
        Assert.Equal(harness.BacklogFile, first.Origin.FilePath);
    }

    [Fact]
    public async Task Editing_second_segment_does_not_corrupt_first()
    {
        var harness = Build("# First\n\n```meta\nstatus: draft\n```\n\nFirst prose.\n\n# Second\n\n```meta\nstatus: active\n```\n\nSecond prose.\n");
        await harness.State.InitializeAsync();

        Assert.Equal(2, harness.State.Rows.Count);
        var second = harness.State.Rows[1];

        await harness.State.ChangeStatusAsync(second, EntryStatus.Done);

        var fileContent = File.ReadAllText(harness.BacklogFile);
        // First segment's status must remain untouched.
        Assert.Contains("status: draft", fileContent, StringComparison.OrdinalIgnoreCase);
        // Second segment's status must be updated.
        var secondMeta = fileContent[(fileContent.IndexOf("# Second", StringComparison.Ordinal))..];
        Assert.Contains("status: done", secondMeta, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Meta_blocks_of_other_segments_are_preserved()
    {
        var original = "# First\n\n```meta\nstatus: draft\nimplements: [.domain/x.md]\n```\n\nProse.\n";
        var harness = Build(original);
        await harness.State.InitializeAsync();

        var row = Assert.Single(harness.State.Rows);
        await harness.State.ChangeStatusAsync(row, EntryStatus.Ready);

        var fileContent = File.ReadAllText(harness.BacklogFile);
        // The implements field must survive the write.
        Assert.Contains("implements:", fileContent, StringComparison.OrdinalIgnoreCase);
        // Status must be updated (Ready maps to "accepted" in knowledge vocabulary).
        Assert.Contains("status: accepted", fileContent, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Adding_a_new_local_entry_still_works()
    {
        var harness = Build("# Existing\n\n```meta\nstatus: draft\n```\n");
        await harness.State.InitializeAsync();

        // The existing repo row should be in the list.
        Assert.Single(harness.State.Rows);

        // Add a new local entry.
        harness.State.NewRow();
        Assert.Equal(2, harness.State.Rows.Count);

        // The new row has no origin.
        var newRow = harness.State.Rows.Last();
        Assert.Null(newRow.Origin);
    }

    [Fact]
    public void RepositoryBacklogText_round_trips_status_translation()
    {
        // Active (knowledge) → InProgress (backlog sigil) → active (knowledge meta)
        var entries = RepositoryBacklogText.ToEntries("# Work\n\n```meta\nstatus: active\n```\n");
        var entry = Assert.Single(entries);
        Assert.Equal(EntryStatus.InProgress, entry.Status);

        // Now verify the reverse: when writing back, InProgress should map to "active"
        var knowledgeStatus = RepositoryBacklogText.ToKnowledgeStatus(EntryStatus.InProgress);
        Assert.Equal("active", knowledgeStatus);
    }

    [Fact]
    public void RepositoryBacklogWriter_updates_status_in_correct_segment()
    {
        var dir = CreateTempDir();
        var file = Path.Combine(dir, "test.md");
        File.WriteAllText(file, "# First\n\n```meta\nstatus: draft\n```\n\nProse.\n\n# Second\n\n```meta\nstatus: active\n```\n\nMore.\n");

        RepositoryBacklogWriter.UpdateSegmentStatus(file, segmentIndex: 1, "done");

        var content = File.ReadAllText(file);
        // First segment must not be touched.
        Assert.Contains("status: draft", content[..content.IndexOf("# Second", StringComparison.Ordinal)], StringComparison.OrdinalIgnoreCase);
        // Second segment's status must be updated.
        Assert.Contains("status: done", content[content.IndexOf("# Second", StringComparison.Ordinal)..], StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void RepositoryBacklogWriter_updates_raw_text_in_segment()
    {
        var dir = CreateTempDir();
        var file = Path.Combine(dir, "test.md");
        File.WriteAllText(file, "# Work\n\n```meta\nstatus: draft\n```\n\nOriginal prose.\n");

        RepositoryBacklogWriter.UpdateSegment(file, segmentIndex: 0, "# Work\n`task` `*medium` `!done`\n\nUpdated prose.\n");

        var content = File.ReadAllText(file);
        // Meta block must be preserved with updated status.
        Assert.Contains("status: done", content, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Updated prose.", content, StringComparison.Ordinal);
    }

    private string CreateTempDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), "backlog-write-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        _tempDirs.Add(dir);
        return dir;
    }

    private Harness Build(string backlogMarkdown)
    {
        var root = Path.Combine(Path.GetTempPath(), "backlog-write-tests", Guid.NewGuid().ToString("n"));
        _tempDirs.Add(root);

        var store = new BacklogStore(root, Path.Combine(root, "settings.json"));
        Assert.Null(store.TryUseRoot(Path.Combine(root, "local")));

        var clone = Path.Combine(root, "clone");
        var backlogDirectory = Path.Combine(clone, ".backlog");
        Directory.CreateDirectory(backlogDirectory);
        var backlogFile = Path.Combine(backlogDirectory, "plan.md");
        File.WriteAllText(backlogFile, backlogMarkdown);

        var settings = new GitHubSettingsStore(Path.Combine(root, "github.json"));
        settings.SetRepositories([new GitHubRepositoryRef("docs", "JSdotNet", "Backlog-docs")]);
        settings.SetCloneDirectory("docs", clone);

        var integration = new GitHubIntegration(settings, new StubGitHubClient(), new StubProbe());
        var repositoryBacklog = new RepositoryBacklogSource(new KnowledgeFolderSource(settings));

        return new Harness(
            new BacklogDesktopState(store, integration, copilot: null, repositoryBacklog),
            backlogFile);
    }

    private sealed record Harness(BacklogDesktopState State, string BacklogFile);

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
            Task.FromResult(new GitHubConnection(false, "Not configured."));

        public void Invalidate()
        {
        }
    }
}
