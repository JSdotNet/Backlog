using Backlog.Modules.Roadmap.Abstractions.DataTransferObjects;

using Microsoft.Data.Sqlite;

namespace Backlog.Infrastructure.Brain.UnitTests;

/// <summary>
/// The Brain's notes as a roadmap item gathers them (local ADR 0053 §3 and §5): every
/// note and chapter the Brain's own database indexes, with its title, the effort its
/// <c>meta</c> block registered and the Roadmap Tags its <c>roadmap</c> list names —
/// and nothing at all for a device with no Brain, or a Brain whose index will not
/// answer.
/// </summary>
// Beside the other suite that builds and opens Brain databases: clearing the Sqlite
// pools on dispose is process-wide, so these do not run alongside it.
[Collection(CorpusRegistryCollection.Name)]
public sealed class BrainRoadmapNoteReaderTests : IDisposable
{
    private readonly string _scratch = Path.Combine(Path.GetTempPath(), "backlog-brain-roadmap-notes", Guid.NewGuid().ToString("N"));

    private string Brain => Path.Combine(_scratch, "Brain");

    private string Target => DatabaseFor(Brain)!;

    public BrainRoadmapNoteReaderTests()
    {
        Write("CONVENTIONS.md", "# Conventions\n\n## Areas\n\n| Area | Folder |\n|---|---|\n| Projects | projects |\n| Work | work |\n");
        Write("projects/sync.md", """
            # Sync rework

            ```meta
            effort: 5
            roadmap: [sync]
            ```

            The plan.

            ## Later

            ```meta
            effort: 2
            roadmap: [sync, other]
            ```

            Afterwards.

            ## Plain heading

            Nothing registered.
            """);
        Write("work/quiet.md", "# Quiet\n\nNames nothing.\n");
    }

    [Fact]
    public async Task Every_note_and_chapter_arrives_with_its_title_effort_and_roadmap_tags()
    {
        Build();

        var notes = Assert.IsAssignableFrom<IReadOnlyList<RoadmapBrainNote>>(await Reader().ReadAsync(TestContext.Current.CancellationToken));

        var sync = Assert.Single(notes, note => note is { Path: "projects/sync.md", Slug: null });
        Assert.Equal("Sync rework", sync.Title);
        Assert.Equal(5, sync.Effort);
        Assert.Equal(["sync"], sync.Roadmap);
        Assert.Equal("sync-rework", sync.TitleSlug);

        var later = Assert.Single(notes, note => note is { Path: "projects/sync.md", Slug: "later" });
        Assert.Equal("Later", later.Title);
        Assert.Equal(2, later.Effort);
        Assert.Equal(["sync", "other"], later.Roadmap);

        // A direct reference resolves against notes that name no item, so they come too.
        var plain = Assert.Single(notes, note => note is { Path: "projects/sync.md", Slug: "plain-heading" });
        Assert.Null(plain.Effort);
        Assert.Empty(plain.Roadmap);

        var quiet = Assert.Single(notes, note => note is { Path: "work/quiet.md", Slug: null });
        Assert.Equal("Quiet", quiet.Title);
        Assert.Null(quiet.Effort);
        Assert.Empty(quiet.Roadmap);
    }

    [Fact]
    public async Task No_brain_no_database_or_a_broken_one_reads_as_no_brain_on_this_device()
    {
        var none = new BrainRoadmapNoteReader(() => null);
        var missing = new BrainRoadmapNoteReader(() => Brain, _ => Path.Combine(_scratch, "missing", "devbook.db"));

        Directory.CreateDirectory(_scratch);
        var brokenPath = Path.Combine(_scratch, "broken.db");
        File.WriteAllText(brokenPath, "this is not a database");
        var broken = new BrainRoadmapNoteReader(() => Brain, _ => brokenPath);

        // Null, not empty: nothing can be said about what this Brain holds, so a ref
        // reads as "no Brain here" rather than "not in your Brain".
        Assert.Null(await none.ReadAsync(TestContext.Current.CancellationToken));
        Assert.Null(await missing.ReadAsync(TestContext.Current.CancellationToken));
        Assert.Null(await broken.ReadAsync(TestContext.Current.CancellationToken));
    }

    private BrainRoadmapNoteReader Reader() => new(() => Brain, DatabaseFor);

    private string? DatabaseFor(string root) =>
        DevbookDatabaseLocation.PathFor(Path.Combine(_scratch, "devbook-cache", DevbookDatabaseLocation.DatabasesFolderName), root);

    private void Build() =>
        Assert.True(DevbookDatabaseBuilder.Build(Brain, Target, BrainCorpusProfile.Instance, TestContext.Current.CancellationToken));

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
