using Backlog.Modules.Backlog.Abstractions.Services;
using Backlog.Infrastructure.GitHub;
using Backlog.Modules.Backlog.DomainModels;

namespace Backlog.Desktop.UI.UnitTests;

public class RepositoryBacklogSourceTests
{
    [Fact]
    public void Reads_a_backlog_file_the_same_way_the_quick_edit_list_reads_an_entry()
    {
        var entries = RepositoryBacklogText.ToEntries(
            """
            # Backlog Management Work

            ```meta
            status: draft
            ```

            Some prose about the concern.

            ## Add roadmap planning

            ```meta
            status: active
            ```

            As a person managing work, I want a roadmap view.
            """);

        var entry = Assert.Single(entries);
        Assert.Equal(EntryStatus.Draft, entry.Status);

        var parsed = EntryTextParser.Parse(entry.RawText);
        Assert.Equal("Backlog Management Work", parsed.Title);
        var subItem = Assert.Single(parsed.SubItems);
        Assert.Equal("Add roadmap planning", subItem.Title);
    }

    [Fact]
    public void Leaves_no_meta_fence_behind_to_render_as_a_code_block()
    {
        var entry = Assert.Single(RepositoryBacklogText.ToEntries(
            "# Work\n\n```meta\nstatus: active\nimplements: [.domain/x.md]\n```\n\nProse.\n"));

        Assert.DoesNotContain("meta", entry.RawText, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("implements", entry.RawText, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Prose.", entry.RawText, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("draft", EntryStatus.Draft)]
    [InlineData("accepted", EntryStatus.Ready)]
    [InlineData("active", EntryStatus.InProgress)]
    [InlineData("in-progress", EntryStatus.InProgress)]
    [InlineData("done", EntryStatus.Done)]
    [InlineData("superseded", EntryStatus.Archived)]
    public void Maps_knowledge_status_words_onto_entry_status(string word, EntryStatus expected)
    {
        var entry = Assert.Single(RepositoryBacklogText.ToEntries($"# Work\n\n```meta\nstatus: {word}\n```\n"));

        Assert.Equal(expected, entry.Status);
    }

    [Fact]
    public void Has_no_opinion_about_a_status_the_backlog_does_not_have_a_word_for()
    {
        var entry = Assert.Single(RepositoryBacklogText.ToEntries("# Work\n\n```meta\nstatus: pondered\n```\n"));

        Assert.Null(entry.Status);
    }

    [Fact]
    public void Splits_a_file_that_holds_more_than_one_top_level_heading()
    {
        var entries = RepositoryBacklogText.ToEntries("# First\n\nOne.\n\n# Second\n\nTwo.\n");

        Assert.Equal(2, entries.Count);
        Assert.Equal("First", EntryTextParser.Parse(entries[0].RawText).Title);
        Assert.Equal("Second", EntryTextParser.Parse(entries[1].RawText).Title);
    }

    [Fact]
    public void Reads_every_markdown_file_in_a_configured_repository_backlog_folder()
    {
        using var repo = new TemporaryRepository();
        File.WriteAllText(Path.Combine(repo.BacklogDirectory, "domain-backlog.md"), "# Domain work\n\n```meta\nstatus: active\n```\n\n## An item\n");
        File.WriteAllText(Path.Combine(repo.BacklogDirectory, "notes.txt"), "not markdown");

        var documents = new RepositoryBacklogSource(repo.BacklogStore).Load("docs");

        var document = Assert.Single(documents);
        Assert.Equal(EntryStatus.InProgress, document.Status);
        Assert.Equal("docs", document.Area);
        Assert.Equal("JSdotNet/Backlog-docs", document.RepositoryFullName);
        Assert.Equal(".backlog/domain-backlog.md", document.RelativePath);
    }

    [Fact]
    public void Shows_nothing_when_the_repository_turned_its_backlog_folder_off()
    {
        using var repo = new TemporaryRepository(backlogEnabled: false);
        File.WriteAllText(Path.Combine(repo.BacklogDirectory, "work.md"), "# Work\n");

        Assert.Empty(new RepositoryBacklogSource(repo.BacklogStore).Load("docs"));
    }

    [Fact]
    public void Shows_nothing_when_the_repository_has_no_backlog_folder_on_disk()
    {
        using var repo = new TemporaryRepository(createBacklogDirectory: false);

        Assert.Empty(new RepositoryBacklogSource(repo.BacklogStore).Load("docs"));
    }

    private sealed class TemporaryRepository : IDisposable
    {
        private readonly string _root;

        public TemporaryRepository(bool backlogEnabled = true, bool createBacklogDirectory = true)
        {
            _root = Path.Combine(Path.GetTempPath(), "backlog-repo-source-tests", Guid.NewGuid().ToString("N"));
            CloneDirectory = Path.Combine(_root, "clone");
            BacklogDirectory = Path.Combine(CloneDirectory, ".backlog");
            Directory.CreateDirectory(createBacklogDirectory ? BacklogDirectory : CloneDirectory);

            var settings = new GitHubSettingsStore(Path.Combine(_root, "github.json"));
            settings.SetRepositories([new GitHubRepositoryRef("docs", "JSdotNet", "Backlog-docs")]);
            settings.SetCloneDirectory("docs", CloneDirectory);
            settings.SetKnowledgeFolder("docs", ".backlog", backlogEnabled, null);

            BacklogStore = BacklogTestHost.BacklogStoreFor(
                new WorkspaceSettingsStore(Path.Combine(_root, "workspace")),
                new KnowledgeFolderSource(settings));
        }

        public string CloneDirectory { get; }

        public string BacklogDirectory { get; }

        public IBacklogStore BacklogStore { get; }

        public void Dispose()
        {
            if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
        }
    }
}
