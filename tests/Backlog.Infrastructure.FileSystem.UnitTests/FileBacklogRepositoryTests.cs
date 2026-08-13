using Backlog.Modules.Backlog;
using Backlog.Modules.Backlog.DomainModels;
using Backlog.Infrastructure.FileSystem;
using Xunit;

namespace Backlog.Infrastructure.FileSystem.UnitTests;

public class FileBacklogRepositoryTests : IDisposable
{
    private readonly string _dir;
    private readonly FileBacklogRepository _repo;

    public FileBacklogRepositoryTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "backlog-tests-" + Guid.NewGuid().ToString("N"));
        _repo = new FileBacklogRepository(_dir);
    }

    [Fact]
    public async Task Save_Then_Get_RoundTripsFullAggregate()
    {
        var entry = new BacklogEntry("Persisted", "# hello\nbody", EntryType.Prompt,
            Priority.High, repoIds: new[] { "org/repo" }, tags: new[] { "x", "y" });
        entry.ChangeStatus(EntryStatus.Ready);
        var s1 = entry.AddSubItem("step 1", "note");
        entry.AddSubItem("step 2");
        entry.ToggleSubItem(s1.Id);
        entry.RecordUsage("copy");
        entry.AddProjectionRef(new ProjectionRef("org/repo", "42", "github-issue"));

        await _repo.SaveAsync(entry);
        var loaded = await _repo.GetAsync(entry.Id);

        Assert.NotNull(loaded);
        Assert.Equal(entry.Title, loaded!.Title);
        Assert.Equal(EntryStatus.Ready, loaded.Status);
        Assert.Equal(Priority.High, loaded.Priority);
        Assert.Equal(EntryType.Prompt, loaded.Type);
        Assert.Equal(new[] { "org/repo" }, loaded.RepoIds);
        Assert.Equal(new[] { "x", "y" }, loaded.Tags);
        Assert.Equal("# hello\nbody", loaded.ContentMd.TrimEnd('\n'));
        Assert.Equal(2, loaded.TotalSubItemCount);
        Assert.Equal(1, loaded.CompletedSubItemCount);
        Assert.Single(loaded.UsageEvents);
        Assert.Single(loaded.ProjectionRefs);
    }

    [Fact]
    public async Task List_ReturnsDerivedSummaries()
    {
        var a = new BacklogEntry("A", "b", EntryType.Task);
        var b = new BacklogEntry("B", "b", EntryType.Idea);
        b.AddSubItem("s");
        await _repo.SaveAsync(a);
        await _repo.SaveAsync(b);

        var list = await _repo.ListAsync();

        Assert.Equal(2, list.Count);
        Assert.Contains(list, x => x.Title == "A" && x.Type == "task");
        Assert.Contains(list, x => x.Title == "B" && x.TotalSubItems == 1);
    }

    [Fact]
    public async Task Delete_RemovesEntryAndIndexRow()
    {
        var a = new BacklogEntry("A", "b", EntryType.Task);
        await _repo.SaveAsync(a);

        await _repo.DeleteAsync(a.Id);

        Assert.Null(await _repo.GetAsync(a.Id));
        Assert.Empty(await _repo.ListAsync());
    }

    [Fact]
    public async Task MarkdownFile_IsCanonicalSourceWithFrontmatter()
    {
        var a = new BacklogEntry("A", "body text", EntryType.Task);
        await _repo.SaveAsync(a);

        var file = Path.Combine(_dir, "_backlog", $"{a.Id}.md");
        var text = await File.ReadAllTextAsync(file);
        Assert.StartsWith("---", text);
        Assert.Contains("title: A", text);
        Assert.Contains("body text", text);
    }


    [Fact]
    public async Task Existing_entries_folder_is_migrated_to_backlog_folder()
    {
        var dir = Path.Combine(Path.GetTempPath(), "backlog-tests-" + Guid.NewGuid().ToString("N"));
        try
        {
            var legacy = Path.Combine(dir, "entries");
            Directory.CreateDirectory(legacy);
            var marker = Path.Combine(legacy, "legacy.md");
            await File.WriteAllTextAsync(marker, "legacy");

            _ = new FileBacklogRepository(dir);

            Assert.False(Directory.Exists(legacy));
            Assert.True(File.Exists(Path.Combine(dir, "_backlog", "legacy.md")));
            Assert.True(Directory.Exists(Path.Combine(dir, "_inbox")));
        }
        finally
        {
            try { if (Directory.Exists(dir)) Directory.Delete(dir, true); } catch { }
        }
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_dir)) Directory.Delete(_dir, true); } catch { }
    }
}
