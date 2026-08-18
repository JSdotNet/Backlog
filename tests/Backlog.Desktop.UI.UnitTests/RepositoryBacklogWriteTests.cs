using Backlog.Infrastructure.GitHub;
using Backlog.Modules.Backlog;
using Backlog.Modules.Backlog.DomainModels;

namespace Backlog.Desktop.UI.UnitTests;

/// <summary>
/// Repository-authored <c>.backlog</c> rows are editable and persisted back to
/// their source file. Edits survive reload because the write targets the correct
/// segment in the correct file, not a local store copy.
/// </summary>
[Collection(WorkspaceSettingsCollection.Name)]
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
        await harness.State.ChangeStatusAsync(row, EntryStatus.Done);

        var fileContent = File.ReadAllText(harness.BacklogFile);
        Assert.Contains("status: done", fileContent, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Repository_row_round_trips_sigil_metadata_and_body_edits()
    {
        var harness = Build(
            "# Work\n" +
            "`idea` `*critical` `!ready` `@docs` `#alpha` `#beta`\n\n" +
            "```meta\n" +
            "status: accepted\n" +
            "```\n\n" +
            "Original prose.\n");
        await harness.State.InitializeAsync();

        var row = Assert.Single(harness.State.Rows);
        harness.State.BeginEdit(row);
        harness.State.OnRawTextInput(
            row,
            "# Work\n" +
            "`idea` `*critical` `!done` `@docs` `#alpha` `#beta`\n\n" +
            "Updated prose.\n");
        await harness.State.EndEditAsync(row);

        var fileContent = File.ReadAllText(harness.BacklogFile);
        const string sigilLine = "`idea` `*critical` `!done` `@docs` `#alpha` `#beta`";
        Assert.Contains(sigilLine, fileContent, StringComparison.Ordinal);
        Assert.True(fileContent.IndexOf(sigilLine, StringComparison.Ordinal) < fileContent.IndexOf("```meta", StringComparison.Ordinal));
        Assert.Contains("status: done", fileContent, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Updated prose.", fileContent, StringComparison.Ordinal);

        using var reloaded = harness.CreateReloadedState();
        await reloaded.InitializeAsync();
        var reloadedRow = Assert.Single(reloaded.Rows);
        Assert.Equal(EntryType.Idea, reloadedRow.PreviewType);
        Assert.Equal(Priority.Critical, reloadedRow.PreviewPriority);
        Assert.Equal(EntryStatus.Done, reloadedRow.PreviewStatus);
        Assert.Equal("docs", reloadedRow.PreviewArea);
        Assert.Equal(["alpha", "beta"], reloadedRow.PreviewMetadataTags);
        Assert.Contains("Updated prose.", reloadedRow.RawText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Toggling_a_repository_sub_item_persists_back()
    {
        var harness = Build("# Roadmap planning\n\n```meta\nstatus: active\n```\n\n## [ ] Add the view\n");
        await harness.State.InitializeAsync();

        var row = Assert.Single(harness.State.Rows);
        Assert.Single(row.PreviewSubItems);

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
        Assert.Contains("status: draft", fileContent, StringComparison.OrdinalIgnoreCase);
        var secondMeta = fileContent[(fileContent.IndexOf("# Second", StringComparison.Ordinal))..];
        Assert.Contains("status: done", secondMeta, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Meta_blocks_of_other_segments_are_preserved()
    {
        var original =
            "# First\n\n```meta\nstatus: draft\nimplements: [.domain/x.md]\n```\n\nProse.\n\n" +
            "# Second\n\n```meta\nstatus: active\n```\n\nMore prose.\n";
        var harness = Build(original);
        await harness.State.InitializeAsync();

        var row = harness.State.Rows[1];
        await harness.State.ChangeStatusAsync(row, EntryStatus.Ready);

        var fileContent = File.ReadAllText(harness.BacklogFile);
        Assert.Contains("status: draft", fileContent[..fileContent.IndexOf("# Second", StringComparison.Ordinal)], StringComparison.OrdinalIgnoreCase);
        Assert.Contains("status: accepted", fileContent[fileContent.IndexOf("# Second", StringComparison.Ordinal)..], StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Deleting_a_repository_segment_removes_it_from_the_file_and_reloads_remaining_indices()
    {
        var harness = Build(
            "# First\n\n```meta\nstatus: draft\n```\n\nFirst prose.\n\n" +
            "# Second\n\n```meta\nstatus: active\n```\n\nSecond prose.\n");
        await harness.State.InitializeAsync();

        var first = harness.State.Rows[0];
        await harness.State.DeleteRowAsync(first);

        var fileContent = File.ReadAllText(harness.BacklogFile);
        Assert.DoesNotContain("# First", fileContent, StringComparison.Ordinal);
        Assert.Contains("# Second", fileContent, StringComparison.Ordinal);

        var remaining = Assert.Single(harness.State.Rows);
        Assert.Equal("Second", remaining.PreviewTitle);
        Assert.Equal(0, remaining.Origin!.SegmentIndex);
    }

    [Fact]
    public async Task Deleting_the_only_repository_segment_clears_the_source()
    {
        var harness = Build("# Only\n\n```meta\nstatus: draft\n```\n\nOnly prose.\n");
        await harness.State.InitializeAsync();

        var row = Assert.Single(harness.State.Rows);
        await harness.State.DeleteRowAsync(row);

        Assert.Empty(harness.State.Rows);
        Assert.True(!File.Exists(harness.BacklogFile) || string.IsNullOrWhiteSpace(File.ReadAllText(harness.BacklogFile)));
    }

    [Fact]
    public async Task Unexpected_repository_write_errors_surface_as_error_state()
    {
        var harness = Build("# Work\n\n```meta\nstatus: draft\n```\n");
        await harness.State.InitializeAsync();

        var row = Assert.Single(harness.State.Rows);
        row.Origin = row.Origin! with { SegmentIndex = -1 };

        var exception = await Record.ExceptionAsync(() => harness.State.ChangeStatusAsync(row, EntryStatus.Done));

        Assert.Null(exception);
        Assert.Equal(AppSaveState.Error, harness.State.SaveState);
    }

    [Fact]
    public async Task Adding_a_new_local_entry_still_works()
    {
        var harness = Build("# Existing\n\n```meta\nstatus: draft\n```\n");
        await harness.State.InitializeAsync();

        Assert.Single(harness.State.Rows);

        harness.State.NewRow();
        Assert.Equal(2, harness.State.Rows.Count);

        var newRow = harness.State.Rows.Last();
        Assert.Null(newRow.Origin);
    }

    [Fact]
    public async Task Newly_created_local_entries_persist_after_reload()
    {
        var harness = Build("# Existing\n\n```meta\nstatus: draft\n```\n");
        await harness.State.InitializeAsync();

        harness.State.NewRow();
        var newRow = harness.State.Rows.Last();
        harness.State.OnRawTextInput(newRow, "# New local item\n`task` `*medium` `!draft` `@local`\n\nKeep me.\n");
        await harness.State.EndEditAsync(newRow);

        using var reloaded = harness.CreateReloadedState();
        await reloaded.InitializeAsync();

        var persisted = Assert.Single(reloaded.Rows, row => row.Origin is null && row.PreviewTitle == "New local item");
        Assert.Contains("Keep me.", persisted.RawText, StringComparison.Ordinal);
    }

    [Fact]
    public void RepositoryBacklogText_round_trips_status_translation()
    {
        var entries = RepositoryBacklogText.ToEntries("# Work\n\n```meta\nstatus: active\n```\n");
        var entry = Assert.Single(entries);
        Assert.Equal(EntryStatus.InProgress, entry.Status);

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
        Assert.Contains("status: draft", content[..content.IndexOf("# Second", StringComparison.Ordinal)], StringComparison.OrdinalIgnoreCase);
        Assert.Contains("status: done", content[content.IndexOf("# Second", StringComparison.Ordinal)..], StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Updating_status_in_an_unclosed_first_meta_block_does_not_corrupt_the_next_segment()
    {
        var dir = CreateTempDir();
        var file = Path.Combine(dir, "test.md");
        File.WriteAllText(
            file,
            "# First\n\n```meta\nowner: docs\n\n" +
            "# Second\n\n```meta\nstatus: active\n```\n\nSecond prose.\n");

        RepositoryBacklogWriter.UpdateSegmentStatus(file, segmentIndex: 0, "done");

        var content = File.ReadAllText(file);
        var secondSegment = content[content.IndexOf("# Second", StringComparison.Ordinal)..];
        Assert.Contains("status: done", content[..content.IndexOf("# Second", StringComparison.Ordinal)], StringComparison.OrdinalIgnoreCase);
        Assert.Contains("status: active", secondSegment, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Second prose.", secondSegment, StringComparison.Ordinal);
    }

    [Fact]
    public void RepositoryBacklogWriter_updates_raw_text_in_segment()
    {
        var dir = CreateTempDir();
        var file = Path.Combine(dir, "test.md");
        File.WriteAllText(file, "# Work\n\n```meta\nstatus: draft\n```\n\nOriginal prose.\n");

        RepositoryBacklogWriter.UpdateSegment(file, segmentIndex: 0, "# Work\n`task` `*medium` `!done`\n\nUpdated prose.\n");

        var content = File.ReadAllText(file);
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

        var store = new WorkspaceSettingsStore(root, Path.Combine(root, "settings.json"));
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
        var repositoryBacklog = new RepositoryBacklogSource(
            BacklogTestHost.BacklogStoreFor(store, new KnowledgeFolderSource(settings)));

        return new Harness(
            BacklogTestHost.StateFor(store, integration, copilot: null, repositoryBacklog: repositoryBacklog),
            backlogFile,
            root);
    }

    private sealed record Harness(BacklogDesktopState State, string BacklogFile, string Root)
    {
        public BacklogDesktopState CreateReloadedState()
        {
            var store = new WorkspaceSettingsStore(Root, Path.Combine(Root, "settings.json"));
            Assert.Null(store.TryUseRoot(Path.Combine(Root, "local")));

            var settings = new GitHubSettingsStore(Path.Combine(Root, "github.json"));
            var integration = new GitHubIntegration(settings, new StubGitHubClient(), new StubProbe());
            var repositoryBacklog = new RepositoryBacklogSource(
                BacklogTestHost.BacklogStoreFor(store, new KnowledgeFolderSource(settings)));
            return BacklogTestHost.StateFor(store, integration, copilot: null, repositoryBacklog: repositoryBacklog);
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
            Task.FromResult(new GitHubConnection(false, "Not configured."));

        public void Invalidate()
        {
        }
    }
}
