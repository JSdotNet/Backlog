using Backlog.Infrastructure.Devbook;

using Microsoft.Data.Sqlite;

namespace Backlog.Infrastructure.Brain.UnitTests;

/// <summary>
/// A link the writer appends to a block list is read back by the Brain's own
/// database (<c>.devbook/domain/brain/domain.invariants.md#cross-linking</c>,
/// local ADR 0052 §1): a note that writes its <c>tasks</c>, <c>roadmap</c> or
/// <c>repos</c> as <c>- item</c> lines names them in the index exactly as one that
/// writes <c>[a, b]</c> does — so Create task shows the Tasks chip, the task row
/// counts the note, and a roadmap item gathers it.
/// </summary>
[Collection(CorpusRegistryCollection.Name)]
public sealed class BrainBlockListLinkTests : IDisposable
{
    private const string Earlier = "0c8a1b4e-2f6d-4a37-9c11-5e0b7d3f2a90";
    private const string Created = "7b908425-4208-497e-a978-d3c5b3faa597";

    private readonly string _scratch = Path.Combine(Path.GetTempPath(), "backlog-brain-block-lists", Guid.NewGuid().ToString("N"));

    private string Brain => Path.Combine(_scratch, "Brain");

    private string Target => DatabaseFor(Brain)!;

    public BrainBlockListLinkTests()
    {
        Write("CONVENTIONS.md", "# Conventions\n\n## Areas\n\n| Area | Folder |\n|---|---|\n| Projects | projects |\n");
    }

    [Theory]
    [InlineData("\n")]
    [InlineData("\r\n")]
    public async Task A_task_and_a_roadmap_tag_appended_to_block_lists_are_read_back(string eol)
    {
        Write("projects/sync.md", string.Join(eol,
            "# Sync rework",
            "",
            "```meta",
            "type: note",
            "tasks:",
            $"  - {Earlier}",
            "roadmap:",
            "  - sync",
            "repos:",
            "  - backlog",
            "```",
            "",
            "The body.",
            ""));

        var writer = new BrainNoteLinkWriter(new BrainFolderSource(() => Brain));
        var task = await writer.AppendTaskAsync("projects/sync.md", Created, TestContext.Current.CancellationToken);
        var tag = await writer.AppendRoadmapTagAsync("projects/sync.md", "+gtd-para", TestContext.Current.CancellationToken);
        Assert.Equal(BrainLinkOutcome.Appended, task.Outcome);
        Assert.Equal(BrainLinkOutcome.Appended, tag.Outcome);
        Assert.Contains($"  - {Created}{eol}", File.ReadAllText(Path.Combine(Brain, "projects", "sync.md")), StringComparison.Ordinal);

        Assert.True(DevbookDatabaseBuilder.Build(Brain, Target, BrainCorpusProfile.Instance, TestContext.Current.CancellationToken));

        using (var database = DevbookDatabase.TryOpen(Target))
        {
            Assert.NotNull(database);
            Assert.Equal([Earlier, Created], BrainTaskLinkReader.TasksOf(database, "projects/sync.md").Select(link => link.TaskId));

            var note = Assert.Single(BrainRoadmapNoteReader.Notes(database), n => n is { Path: "projects/sync.md", Slug: null });
            Assert.Equal(["sync", "+gtd-para"], note.Roadmap);
        }

        var notes = await new BrainTaskLinkReader(() => Brain, DatabaseFor).NotesByTaskAsync(TestContext.Current.CancellationToken);
        Assert.Contains(notes, pair => pair.Key == Created);
    }

    [Fact]
    public void A_later_flow_value_wins_over_an_earlier_block_list_as_the_parse_reads_a_later_key()
    {
        Write("projects/twice.md", $"# Twice\n\n```meta\ntasks:\n  - {Earlier}\ntasks: [{Created}]\n```\n");

        Assert.True(DevbookDatabaseBuilder.Build(Brain, Target, BrainCorpusProfile.Instance, TestContext.Current.CancellationToken));

        using var database = DevbookDatabase.TryOpen(Target);
        Assert.NotNull(database);
        Assert.Equal([Created], BrainTaskLinkReader.TasksOf(database, "projects/twice.md").Select(link => link.TaskId));
    }

    private string? DatabaseFor(string root) =>
        DevbookDatabaseLocation.PathFor(Path.Combine(_scratch, "devbook-cache", DevbookDatabaseLocation.DatabasesFolderName), root);

    private void Write(string relativePath, string text)
    {
        var path = Path.Combine(Brain, relativePath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text);
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();

        try
        {
            if (Directory.Exists(_scratch)) Directory.Delete(_scratch, recursive: true);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
