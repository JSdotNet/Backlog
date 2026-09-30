using Backlog.Modules.Devbook.Abstractions;
using Backlog.Modules.Tasks.Abstractions.Services;

using ModelContextProtocol;

namespace Backlog.Infrastructure.Mcp.UnitTests;

/// <summary>
/// The devbook tools against a real folder on disk: what an ordinary read leaves
/// out, what a review read puts back, and which notes count.
/// </summary>
public sealed class DevbookToolsTests : IDisposable
{
    private const string ChapterPath = ".arc42/adr/0012-mcp.md";

    /// <summary>The same chapter's path within its folder — what the folder
    /// source is asked to prepare.</summary>
    private const string FolderRelativePath = "adr/0012-mcp.md";

    /// <summary>
    /// A chapter with one of everything the two modes have to tell apart: the
    /// record fence, the convention's own note fence, a diagram, prose, a list
    /// and a code listing.
    /// </summary>
    private const string Chapter = """
        # Backlog is an MCP server

        ```meta
        status: active
        date: 2026-09-22
        ```

        A session has nothing to ask Backlog with.

        ```annotation
        author: someone
        body: Does this still hold?
        ```

        ```mermaid
        graph TD; Session-->Backlog;
        ```

        - The server lives in the desktop process
        - Scope is a repository argument

        ```csharp
        var server = new McpServer();
        ```
        """;

    private static readonly TasksRepositoryRef Backlog = new("backlog", "JSdotNet", "Backlog");

    private readonly DirectoryInfo _root = Directory.CreateTempSubdirectory("backlog-mcp-tests");

    public DevbookToolsTests()
    {
        var folder = Directory.CreateDirectory(Path.Combine(_root.FullName, ".arc42", "adr"));

        File.WriteAllText(Path.Combine(folder.FullName, "0012-mcp.md"), Chapter);
    }

    public void Dispose() => _root.Delete(recursive: true);

    /// <summary>Only the enabled folders are listed, each with the availability
    /// the resolution decided — a folder that is not on this machine says so in
    /// the words the panels use rather than being left out.</summary>
    [Fact]
    public void Knowledge_contexts_are_the_enabled_folders_each_resolved()
    {
        var tools = Tools(out _, out _);

        var answer = tools.ListKnowledgeContextsFor("JSdotNet/Backlog");

        Assert.Equal("JSdotNet/Backlog", answer.Repository);
        Assert.Equal([".arc42", ".domain"], answer.Contexts.Select(context => context.Key));

        var architecture = answer.Contexts[0];
        Assert.True(architecture.Available);
        Assert.Null(architecture.Message);

        // Configured but not on disk: available is false and the message is the
        // reason, which is what a caller shows instead of an empty chapter list.
        var domain = answer.Contexts[1];
        Assert.False(domain.Available);
        Assert.NotNull(domain.Message);
    }

    /// <summary>An ordinary read is the chapter an author wrote: the record fence
    /// and the convention's note fence are out, the diagram and the code listing
    /// are in, and no private note travels.</summary>
    [Fact]
    public async Task An_ordinary_read_leaves_out_only_the_meta_and_annotation_fences()
    {
        var tools = Tools(out _, out _);

        var answer = await tools.ReadKnowledgeChapterAsync(
            "JSdotNet/Backlog",
            ChapterPath,
            review: false,
            TestContext.Current.CancellationToken);

        Assert.False(answer.Review);
        Assert.Equal(".arc42", answer.ContextKey);
        Assert.DoesNotContain(answer.Blocks, block => block.Language is "meta" or "annotation");
        Assert.Contains(answer.Blocks, block => block.Language == "mermaid");
        Assert.Contains(answer.Blocks, block => block.Language == "csharp");
        Assert.All(answer.Blocks, block => Assert.Empty(block.Notes));
        Assert.Empty(answer.OrphanedNotes);
    }

    /// <summary>
    /// The property the whole design rests on: an index is assigned over the full
    /// parse in both modes, so the blocks an ordinary read omits do not renumber
    /// the ones after them. Without it a note anchored to block 7 would mean two
    /// different blocks to two readers of the same chapter.
    /// </summary>
    [Fact]
    public async Task Block_indices_are_the_same_in_both_modes()
    {
        var tools = Tools(out _, out _);

        var plain = await tools.ReadKnowledgeChapterAsync(
            "JSdotNet/Backlog", ChapterPath, review: false, TestContext.Current.CancellationToken);

        var review = await tools.ReadKnowledgeChapterAsync(
            "JSdotNet/Backlog", ChapterPath, review: true, TestContext.Current.CancellationToken);

        Assert.Equal(review.BlockCount, plain.BlockCount);
        Assert.Equal(review.BlockCount, review.Blocks.Count);
        Assert.True(plain.Blocks.Count < review.Blocks.Count, "The ordinary read should omit the two private fences.");

        // Every block an ordinary read did emit is at the index the review read
        // put it at, and names the same lines of the file.
        foreach (var block in plain.Blocks)
        {
            var same = review.Blocks.Single(candidate => candidate.Index == block.Index);

            Assert.Equal(block.Kind, same.Kind);
            Assert.Equal(block.StartLine, same.StartLine);
            Assert.Equal(block.EndLineExclusive, same.EndLineExclusive);
        }

        // And the indices really are the parse's, not a renumbering that happens
        // to agree: the omitted fences leave gaps.
        Assert.Equal([0, 2, 4, 5, 6], plain.Blocks.Select(block => block.Index));
    }

    /// <summary>A review read puts the fences back and hangs each note off the
    /// block its index names.</summary>
    [Fact]
    public async Task A_review_read_interleaves_the_notes_at_their_blocks()
    {
        var anchored = Notes.Note(Backlog.Alias, ChapterPath, blockIndex: 2, body: "Still true?");

        var tools = Tools(out _, out _, anchored);

        var answer = await tools.ReadKnowledgeChapterAsync(
            "JSdotNet/Backlog", ChapterPath, review: true, TestContext.Current.CancellationToken);

        Assert.True(answer.Review);
        Assert.Contains(answer.Blocks, block => block.Language == "meta");
        Assert.Contains(answer.Blocks, block => block.Language == "annotation");

        var note = Assert.Single(answer.Blocks.Single(block => block.Index == 2).Notes);
        Assert.Equal("Still true?", note.Body);
        Assert.Equal(anchored.Id, note.Id);

        Assert.All(
            answer.Blocks.Where(block => block.Index != 2),
            block => Assert.Empty(block.Notes));
    }

    /// <summary>A note whose block has gone still lands somewhere. Dropping it
    /// would lose what somebody said; showing it at the end says it is
    /// adrift.</summary>
    [Fact]
    public async Task A_note_anchored_out_of_range_still_travels()
    {
        var adrift = Notes.Note(Backlog.Alias, ChapterPath, blockIndex: 99, body: "Anchored to a block that went away.");

        var tools = Tools(out _, out _, adrift);

        var answer = await tools.ReadKnowledgeChapterAsync(
            "JSdotNet/Backlog", ChapterPath, review: true, TestContext.Current.CancellationToken);

        var orphan = Assert.Single(answer.OrphanedNotes);
        Assert.Equal(adrift.Id, orphan.Id);
        Assert.All(answer.Blocks, block => Assert.Empty(block.Notes));
    }

    /// <summary>The notes are asked for once, with the chapter's own
    /// repository-relative path: the store canonicalizes the question, so a
    /// second spelling would only ask the same thing twice.</summary>
    [Fact]
    public void Annotations_are_asked_for_once_with_the_chapter_path()
    {
        var note = Notes.Note(Backlog.Alias, ChapterPath, blockIndex: 1, body: "Filed by the panel.");

        var tools = Tools(out _, out var store, note);

        var answer = tools.ListAnnotationsOn("JSdotNet/Backlog", ChapterPath);

        Assert.Equal(note.Id, Assert.Single(answer.Annotations).Id);
        Assert.Equal((Backlog.Alias, ChapterPath), Assert.Single(store.Listed));
    }

    /// <summary>The review read asks the same one question.</summary>
    [Fact]
    public async Task A_review_read_asks_for_the_notes_once_with_the_chapter_path()
    {
        var note = Notes.Note(Backlog.Alias, ChapterPath, blockIndex: 2, body: "Still true?");

        var tools = Tools(out _, out var store, note);

        var answer = await tools.ReadKnowledgeChapterAsync(
            "JSdotNet/Backlog", ChapterPath, review: true, TestContext.Current.CancellationToken);

        Assert.Equal(note.Id, Assert.Single(answer.Blocks.Single(block => block.Index == 2).Notes).Id);
        Assert.Equal((Backlog.Alias, ChapterPath), Assert.Single(store.Listed));
    }

    /// <summary>
    /// A note filed under one configured spelling of a chapter is found by any
    /// other, with one question. The store canonicalizes both the key and the
    /// question, so which spelling the panel held when it filed the note and
    /// which one the session asks with no longer matter.
    /// </summary>
    [Theory]
    [InlineData(".devbook/arc42/adr/0012-mcp.md", ".arc42/adr/0012-mcp.md")]
    [InlineData(".arc42/adr/0012-mcp.md", ".devbook/arc42/adr/0012-mcp.md")]
    [InlineData("docs/arch/adr/0012-mcp.md", ".arc42/adr/0012-mcp.md")]
    [InlineData(".arc42/adr/0012-mcp.md", "docs/arch/adr/0012-mcp.md")]
    [InlineData("docs/arch/adr/0012-mcp.md", ".devbook/arc42/adr/0012-mcp.md")]
    public void A_note_filed_under_another_spelling_of_the_chapter_is_found(string filedUnder, string askedWith)
    {
        var note = Notes.Note(Backlog.Alias, filedUnder, blockIndex: 1, body: "Filed on the other machine.");

        var relocated = Arc42 with { Path = "docs/arch" };
        var folders = new FakeDevbookFolderSource(_root.FullName, relocated, Domain);
        var store = new FakeDevbookAnnotationStore(note) { Folders = folders.Folders(Backlog.Alias) };
        var tools = new DevbookTools(folders, store, new FakeRepositoryDirectory([Backlog]));

        var answer = tools.ListAnnotationsOn("JSdotNet/Backlog", askedWith);

        Assert.Equal(note.Id, Assert.Single(answer.Annotations).Id);
        Assert.Single(store.Listed);
    }

    /// <summary>
    /// Live and not draft, which is what neither existing filter gives on its
    /// own: <c>List</c> keeps drafts and drops tombstones, and a draft is a
    /// remark nobody has typed into yet.
    /// </summary>
    [Fact]
    public void Annotations_leave_out_drafts_and_tombstones()
    {
        var said = Notes.Note(Backlog.Alias, ChapterPath, blockIndex: 1, body: "Something said.");
        var draft = Notes.Note(Backlog.Alias, ChapterPath, blockIndex: 2, body: "   ");
        var deleted = Notes.Note(
            Backlog.Alias,
            ChapterPath,
            blockIndex: 3,
            body: "Taken back.",
            deletedAt: new DateTimeOffset(2026, 9, 22, 10, 0, 0, TimeSpan.Zero));

        var tools = Tools(out _, out _, said, draft, deleted);

        var answer = tools.ListAnnotationsOn("JSdotNet/Backlog", ChapterPath);

        var note = Assert.Single(answer.Annotations);
        Assert.Equal(said.Id, note.Id);
    }

    /// <summary>A resolved note is still a note: it stays visible and quiet
    /// rather than disappearing, because the reason a paragraph reads the way it
    /// does is usually in the remark that got it there.</summary>
    [Fact]
    public void A_resolved_annotation_still_travels_and_says_so()
    {
        var resolved = Notes.Note(Backlog.Alias, ChapterPath, blockIndex: 1, body: "Dealt with.", resolved: true);

        var tools = Tools(out _, out _, resolved);

        var answer = tools.ListAnnotationsOn("JSdotNet/Backlog", ChapterPath);

        Assert.True(Assert.Single(answer.Annotations).Resolved);
    }

    /// <summary>A chapter read names the one file rather than the whole area: for
    /// a branch snapshot, asking for the area would download a folder to read a
    /// page of it.</summary>
    [Fact]
    public async Task A_chapter_read_prepares_only_that_chapter()
    {
        var tools = Tools(out var folders, out _);

        await tools.ReadKnowledgeChapterAsync(
            "JSdotNet/Backlog", ChapterPath, review: false, TestContext.Current.CancellationToken);

        var prepared = Assert.Single(folders.Prepared);
        Assert.Equal(".arc42", prepared.Key);
        Assert.Equal(FolderRelativePath, prepared.Path);
    }

    /// <summary>A path that climbs out of the folder is not a chapter. The
    /// argument arrives from a client, so containment is checked on the resolved
    /// path rather than on what was asked for.</summary>
    [Fact]
    public async Task A_path_that_escapes_the_folder_is_refused()
    {
        var tools = Tools(out _, out _);

        var failure = await Assert.ThrowsAsync<McpException>(() => tools.ReadKnowledgeChapterAsync(
            "JSdotNet/Backlog",
            ".arc42/../../elsewhere/secrets.md",
            review: false,
            TestContext.Current.CancellationToken));

        Assert.Contains("chapter", failure.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// A chapter under a switched-off area is refused as switched off. Without
    /// the check it would fall through to the folder whose root is the repository
    /// itself, find the file, and answer for an area somebody turned off.
    /// </summary>
    [Fact]
    public async Task A_chapter_under_a_switched_off_area_is_refused()
    {
        var tools = Tools(out _, out _);

        var failure = await Assert.ThrowsAsync<McpException>(() => tools.ReadKnowledgeChapterAsync(
            "JSdotNet/Backlog",
            ".tech/graph.md",
            review: false,
            TestContext.Current.CancellationToken));

        Assert.Contains("chapter.context_disabled", failure.Message, StringComparison.Ordinal);
    }

    /// <summary>An unknown repository refuses here too, and registers
    /// nothing.</summary>
    [Fact]
    public async Task An_unknown_repository_never_reaches_a_folder()
    {
        var directory = new FakeRepositoryDirectory([Backlog]);
        var folders = new FakeDevbookFolderSource(_root.FullName, Arc42, Domain);
        var tools = new DevbookTools(folders, new FakeDevbookAnnotationStore(), directory);

        await Assert.ThrowsAsync<McpException>(() => tools.ReadKnowledgeChapterAsync(
            "JSdotNet/Nowhere", ChapterPath, review: false, TestContext.Current.CancellationToken));

        Assert.Empty(directory.Registered);
        Assert.Empty(folders.Prepared);
    }

    private static DevbookFolderSetting Arc42 => new(".arc42", "Architecture", ".arc42");

    private static DevbookFolderSetting Domain => new(".domain", "Domain", ".domain");

    private DevbookTools Tools(
        out FakeDevbookFolderSource folders,
        out FakeDevbookAnnotationStore store,
        params DevbookAnnotation[] annotations)
    {
        folders = new FakeDevbookFolderSource(
            _root.FullName,
            Arc42,
            Domain,
            new DevbookFolderSetting(".tech", "Technology", ".tech") { Enabled = false });

        store = new FakeDevbookAnnotationStore(annotations);

        return new DevbookTools(folders, store, new FakeRepositoryDirectory([Backlog]));
    }
}
