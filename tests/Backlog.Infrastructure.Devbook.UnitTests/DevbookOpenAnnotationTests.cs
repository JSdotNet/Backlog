using Backlog.Infrastructure.Devbook.Building;

namespace Backlog.Infrastructure.Devbook.UnitTests;

/// <summary>
/// <c>chapter.open_annotations</c>: the open review notes addressed to each
/// chapter row, counted the way the devbook generator's annotation index counts
/// them and the Node writer places them.
///
/// <para>A note is an <c>annotation</c> fence, open unless its <c>status</c>
/// resolves to something else (<c>open</c> is the default). Its address is the
/// nearest heading above it, found with fences skipped; it counts on the first
/// row with that slug, and a note above the first heading counts on the file's
/// first row.</para>
/// </summary>
public sealed class DevbookOpenAnnotationTests : IDisposable
{
    private const string Path = ".devbook/domain/inbox/domain.md";

    private const string Markdown = """
        A note can sit above the first heading.

        ```annotation
        author: ada
        date: 2026-09-01
        body: A note on the file as a whole.
        ```

        # Inbox

        ## Capture

        ```meta
        status: draft
        ```

        ```annotation
        author: ada
        date: 2026-09-01
        body: |
          The body mentions a status,
          status: resolved
          but only at the top level does it count.
        ```

        ```annotation
        status: resolved
        author: bo
        date: 2026-09-02
        body: Settled.
        ```

        ## Capture

        ```annotation
        kind: question
        author: cy
        date: 2026-09-03
        body: A second heading with the same slug counts on the first.
        ```

        ## Fenced

        ```mermaid
        flowchart TB
        # Not a heading
        ```

        ```annotation
        status:
        author: di
        date: 2026-09-04
        body: An empty status is the default, and the fenced line above is no chapter.
        replies:
          - author: ada
            date: 2026-09-05
            body: Agreed.
        ```

        ````markdown
        ```annotation
        author: ex
        body: An example of a note inside another fence is not a note.
        ```
        ````
        """;

    private readonly string _root = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "backlog-open-annotations", Guid.NewGuid().ToString("N"));

    [Fact]
    public void Each_open_note_counts_on_the_chapter_row_it_addresses()
    {
        var chapters = DevbookMarkdown.Parse(Markdown).Chapters;

        var counts = DevbookAnnotations.OpenCounts(Markdown, chapters);

        // The parse finds the fenced `# Not a heading` as a chapter, as the
        // generator's does; the note after the diagram still belongs to Fenced.
        Assert.Equal(
            [("inbox", 1), ("capture", 2), ("capture", 0), ("fenced", 1), ("not-a-heading", 0)],
            chapters.Select((chapter, index) => (chapter.Slug, counts[index])));
    }

    [Fact]
    public void A_file_without_notes_counts_none()
    {
        const string text = "# Inbox\n\n## Capture\n\nNothing to say.\n";

        Assert.Equal([0, 0], DevbookAnnotations.OpenCounts(text, DevbookMarkdown.Parse(text).Chapters));
    }

    [Fact]
    public void A_note_in_a_file_without_headings_is_counted_nowhere()
    {
        const string text = "```annotation\nauthor: ada\nbody: x\n```\n";

        Assert.Empty(DevbookAnnotations.OpenCounts(text, DevbookMarkdown.Parse(text).Chapters));
    }

    [Fact]
    public void The_builder_writes_the_count_into_the_chapter_table()
    {
        var file = System.IO.Path.Combine(_root, Path.Replace('/', System.IO.Path.DirectorySeparatorChar));
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(file)!);
        File.WriteAllText(file, Markdown);
        var target = System.IO.Path.Combine(_root, "out", "devbook.db");

        Assert.True(DevbookDatabaseBuilder.Build(_root, target, TestContext.Current.CancellationToken));

        using var database = DevbookDatabase.TryOpen(target);
        Assert.NotNull(database);
        Assert.Equal(
            [("inbox", 1), ("capture", 2), ("capture", 0), ("fenced", 1), ("not-a-heading", 0)],
            database.Chapters(Path).Select(chapter => (chapter.Slug, chapter.OpenAnnotations)));
    }

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or DirectoryNotFoundException)
        {
            // A temp folder that outlives the run is not a test failure.
        }
    }
}
