using Backlog.Infrastructure.FileSystem;
using Backlog.Modules.Devbook.Abstractions;
using Backlog.Modules.Tasks.Abstractions.Services;
using Backlog.SharedKernel;

using ModelContextProtocol;

namespace Backlog.Infrastructure.Mcp.UnitTests;

/// <summary>
/// The devbook tools on the Brain (<c>.devbook/domain/devbook/features.md#mcp-brain-scope</c>):
/// the reserved scope <c>#brain</c> beside the repositories, and <c>capture_note</c>.
/// <para>
/// Over the real <see cref="BrainFolderSource"/> and <see cref="BrainNoteWriter"/>
/// and a Brain folder on disk, because what these tools promise about the Brain —
/// where a capture lands, that nothing is overwritten, which words a refusal
/// carries — is those two classes' rule, and the tool is the crossing.
/// </para>
/// </summary>
public sealed class BrainScopeToolsTests : IDisposable
{
    private const string NotePath = "projects/plan.md";

    private const string Note = """
        # The plan

        First paragraph.

        Second paragraph.
        """;

    private static readonly TasksRepositoryRef Backlog = new("backlog", "JSdotNet", "Backlog");

    private readonly DirectoryInfo _root = Directory.CreateTempSubdirectory("backlog-mcp-brain");

    public BrainScopeToolsTests()
    {
        Directory.CreateDirectory(Path.Combine(BrainRoot, "projects"));
        File.WriteAllText(Path.Combine(BrainRoot, "projects", "plan.md"), Note);
        File.WriteAllText(Path.Combine(_root.FullName, "outside.md"), "# Not the Brain");
    }

    private string BrainRoot => Path.Combine(_root.FullName, "brain");

    public void Dispose() => _root.Delete(recursive: true);

    [Theory]
    [InlineData("#brain")]
    [InlineData("#Brain")]
    [InlineData("  #BRAIN ")]
    public void The_brain_scope_lists_the_brain_as_its_one_context(string scope)
    {
        var tools = Tools(out _, out var directory);

        var answer = tools.ListKnowledgeContextsFor(scope);

        Assert.Equal("#brain", answer.Repository);
        Assert.Equal(BrainScope.Value, answer.RepositoryAlias);

        var context = Assert.Single(answer.Contexts);
        Assert.Equal(BrainConventions.FolderKey, context.Key);
        Assert.True(context.Available);
        Assert.Null(context.Message);

        // Matched before the directory is asked, so no registration can shadow it.
        Assert.Empty(directory.Resolved);
    }

    [Fact]
    public void A_brain_nobody_set_lists_unavailable_in_the_sources_words()
    {
        var tools = Tools(out _, out _, brainFolder: null);

        var context = Assert.Single(tools.ListKnowledgeContextsFor("#brain").Contexts);

        Assert.False(context.Available);
        Assert.Equal(BrainFolderSource.NoBrainFolder, context.Message);
    }

    [Fact]
    public void A_brain_folder_that_is_missing_lists_unavailable()
    {
        var tools = Tools(out _, out _, brainFolder: Path.Combine(_root.FullName, "gone"));

        var context = Assert.Single(tools.ListKnowledgeContextsFor("#brain").Contexts);

        Assert.False(context.Available);
        Assert.Contains("not found", context.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task A_host_that_composed_no_brain_lists_it_unavailable_and_refuses_to_read_or_capture()
    {
        var tools = new DevbookTools(
            new FakeDevbookFolderSource(_root.FullName, new DevbookFolderSetting(".arc42", "Architecture", ".arc42")),
            new FakeDevbookAnnotationStore(),
            new FakeRepositoryDirectory([Backlog]));

        var context = Assert.Single(tools.ListKnowledgeContextsFor("#brain").Contexts);
        Assert.Equal(BrainConventions.FolderKey, context.Key);
        Assert.False(context.Available);
        Assert.False(string.IsNullOrWhiteSpace(context.Message));

        var read = await Assert.ThrowsAsync<McpException>(() => tools.ReadKnowledgeChapterAsync(
            "#brain", NotePath, review: false, TestContext.Current.CancellationToken));
        Assert.Contains("brain.unavailable", read.Message, StringComparison.Ordinal);

        var capture = await Assert.ThrowsAsync<McpException>(() => tools.CaptureNoteAsync(
            "An idea", body: null, TestContext.Current.CancellationToken));
        Assert.Contains("brain.unavailable", capture.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Reading_a_brain_note_answers_its_text()
    {
        var tools = Tools(out _, out _);

        var answer = await tools.ReadKnowledgeChapterAsync(
            "#brain", NotePath, review: false, TestContext.Current.CancellationToken);

        Assert.Equal("#brain", answer.Repository);
        Assert.Equal(BrainScope.Value, answer.RepositoryAlias);
        Assert.Equal(BrainConventions.FolderKey, answer.ContextKey);
        Assert.Equal(NotePath, answer.ChapterPath);
        Assert.Contains("Second paragraph.", answer.Markdown, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_review_read_interleaves_the_remarks_left_on_the_brain()
    {
        var onBrain = Notes.Note(BrainScope.Value, NotePath, blockIndex: 1, body: "Is this still the plan?");
        var onRepository = Notes.Note(Backlog.Alias, NotePath, blockIndex: 1, body: "A repository's remark.");
        var tools = Tools(out _, out _, notes: [onBrain, onRepository]);

        var answer = await tools.ReadKnowledgeChapterAsync(
            "#brain", NotePath, review: true, TestContext.Current.CancellationToken);

        var notes = answer.Blocks.SelectMany(block => block.Notes).Concat(answer.OrphanedNotes).ToList();
        Assert.Equal(onBrain.Id, Assert.Single(notes).Id);
    }

    [Theory]
    [InlineData("projects/plan.txt", "chapter.not_a_chapter")]
    [InlineData(".obsidian/workspace.md", "chapter.not_a_chapter")]
    [InlineData("projects/.hidden/plan.md", "chapter.not_a_chapter")]
    [InlineData("node_modules/readme.md", "chapter.not_a_chapter")]
    [InlineData("../outside.md", "chapter.not_a_chapter")]
    [InlineData("projects/../../outside.md", "chapter.not_a_chapter")]
    [InlineData("projects/plan.md:x.md", "chapter.not_a_chapter")]
    [InlineData("projects/missing.md", "chapter.not_found")]
    [InlineData("", "chapter.required")]
    public async Task A_path_that_is_not_a_brain_note_is_refused(string path, string code)
    {
        var tools = Tools(out _, out _);

        var failure = await Assert.ThrowsAsync<McpException>(() => tools.ReadKnowledgeChapterAsync(
            "#brain", path, review: false, TestContext.Current.CancellationToken));

        Assert.Contains(code, failure.Message, StringComparison.Ordinal);
    }

    /// <summary>A drive-rooted path carries a colon and is refused by its
    /// spelling; a UNC path loses its leading slashes to normalization and is
    /// then looked for inside the Brain, where it is not.</summary>
    [Fact]
    public async Task A_path_rooted_somewhere_else_is_refused()
    {
        var tools = Tools(out _, out _);
        var outside = Path.Combine(_root.FullName, "outside.md");

        var failure = await Assert.ThrowsAsync<McpException>(() => tools.ReadKnowledgeChapterAsync(
            "#brain", outside, review: false, TestContext.Current.CancellationToken));

        Assert.DoesNotContain("Not the Brain", failure.Message, StringComparison.Ordinal);
        Assert.Contains("chapter.not_a_chapter", failure.Message, StringComparison.Ordinal);

        var unc = await Assert.ThrowsAsync<McpException>(() => tools.ReadKnowledgeChapterAsync(
            "#brain", "//server/share/x.md", review: false, TestContext.Current.CancellationToken));

        Assert.Contains("chapter.not_found", unc.Message, StringComparison.Ordinal);
    }

    /// <summary>A folder inside the Brain that links somewhere else passes the
    /// containment check on its spelling, so the read refuses it on what it is.
    /// Skipped where this machine will not make a link without elevation.</summary>
    [Fact]
    public async Task A_note_reached_through_a_link_is_refused()
    {
        var elsewhere = Directory.CreateDirectory(Path.Combine(_root.FullName, "elsewhere"));
        File.WriteAllText(Path.Combine(elsewhere.FullName, "secret.md"), "# Not the Brain");

        try
        {
            Directory.CreateSymbolicLink(Path.Combine(BrainRoot, "linked"), elsewhere.FullName);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or PlatformNotSupportedException)
        {
            Assert.Skip($"This machine cannot create a symbolic link without elevation: {ex.Message}");
        }

        var tools = Tools(out _, out _);

        var failure = await Assert.ThrowsAsync<McpException>(() => tools.ReadKnowledgeChapterAsync(
            "#brain", "linked/secret.md", review: false, TestContext.Current.CancellationToken));

        Assert.Contains("chapter.not_a_chapter", failure.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("Not the Brain", failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Listing_brain_remarks_answers_the_written_live_ones_on_the_brain()
    {
        var written = Notes.Note(BrainScope.Value, NotePath, blockIndex: 1, body: "Worth a second look.");
        var draft = Notes.Note(BrainScope.Value, NotePath, blockIndex: 1, body: "");
        var deleted = Notes.Note(BrainScope.Value, NotePath, blockIndex: 1, deletedAt: DateTimeOffset.UnixEpoch);
        var repository = Notes.Note(Backlog.Alias, NotePath, blockIndex: 1);
        var tools = Tools(out _, out _, notes: [written, draft, deleted, repository]);

        var answer = tools.ListAnnotationsOn("#brain", NotePath);

        Assert.Equal("#brain", answer.Repository);
        Assert.Equal(BrainScope.Value, answer.RepositoryAlias);
        Assert.Equal(written.Id, Assert.Single(answer.Annotations).Id);
    }

    [Fact]
    public void A_brain_remark_is_resolved_under_the_brain_scope()
    {
        var note = Notes.Note(BrainScope.Value, NotePath, blockIndex: 1);
        var tools = Tools(out var store, out _, notes: [note]);

        var answer = tools.ResolveAnnotationFor("#brain", note.Id);

        Assert.True(answer.Resolved);
        Assert.Equal((note.Id, true), Assert.Single(store.Resolved));
    }

    [Fact]
    public void A_brain_remark_asked_under_a_repository_is_refused()
    {
        var note = Notes.Note(BrainScope.Value, NotePath, blockIndex: 1);
        var tools = Tools(out var store, out _, notes: [note]);

        var failure = Assert.Throws<McpException>(() => tools.ResolveAnnotationFor("JSdotNet/Backlog", note.Id));

        Assert.Contains("annotation.otherRepository", failure.Message, StringComparison.Ordinal);
        Assert.Empty(store.Resolved);
    }

    [Fact]
    public void A_repository_remark_asked_under_the_brain_is_refused()
    {
        var note = Notes.Note(Backlog.Alias, ".arc42/adr/0012-mcp.md", blockIndex: 1);
        var tools = Tools(out var store, out _, notes: [note]);

        var failure = Assert.Throws<McpException>(() => tools.ResolveAnnotationFor("#brain", note.Id));

        Assert.Contains("annotation.otherRepository", failure.Message, StringComparison.Ordinal);
        Assert.Empty(store.Resolved);
    }

    [Fact]
    public async Task A_capture_lands_in_the_inbox_area_and_reads_back_as_written()
    {
        var tools = Tools(out _, out _);

        var captured = await tools.CaptureNoteAsync(
            "Ideas for the sync", "Batch the uploads.", TestContext.Current.CancellationToken);

        Assert.Equal("_inbox/ideas-for-the-sync.md", captured.Path);
        Assert.Equal("Ideas for the sync", captured.Title);

        var text = await File.ReadAllTextAsync(
            Path.Combine(BrainRoot, "_inbox", "ideas-for-the-sync.md"), TestContext.Current.CancellationToken);
        Assert.StartsWith("# Ideas for the sync\n", text, StringComparison.Ordinal);
        Assert.Contains("source: manual\n", text, StringComparison.Ordinal);
        Assert.Contains("Batch the uploads.", text, StringComparison.Ordinal);

        // What capture answers is a path the read tool takes as it is.
        var read = await tools.ReadKnowledgeChapterAsync(
            "#brain", captured.Path, review: false, TestContext.Current.CancellationToken);
        Assert.Contains("Batch the uploads.", read.Markdown, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_second_capture_under_the_same_title_never_overwrites_the_first()
    {
        var tools = Tools(out _, out _);

        var first = await tools.CaptureNoteAsync("Same title", "First.", TestContext.Current.CancellationToken);
        var second = await tools.CaptureNoteAsync("Same title", "Second.", TestContext.Current.CancellationToken);

        Assert.NotEqual(first.Path, second.Path);

        var firstText = await File.ReadAllTextAsync(Path.Combine(BrainRoot, first.Path), TestContext.Current.CancellationToken);
        Assert.Contains("First.", firstText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_capture_with_no_title_is_refused_in_the_writers_words()
    {
        var tools = Tools(out _, out _);

        var failure = await Assert.ThrowsAsync<McpException>(() => tools.CaptureNoteAsync(
            "   ", "A body.", TestContext.Current.CancellationToken));

        Assert.Contains("brain.capture_refused", failure.Message, StringComparison.Ordinal);
        Assert.Contains(BrainNoteWriter.NoTitle, failure.Message, StringComparison.Ordinal);
        Assert.False(Directory.Exists(Path.Combine(BrainRoot, "_inbox")));
    }

    [Fact]
    public async Task A_capture_with_no_brain_set_is_refused_in_the_sources_words()
    {
        var tools = Tools(out _, out _, brainFolder: null);

        var failure = await Assert.ThrowsAsync<McpException>(() => tools.CaptureNoteAsync(
            "An idea", body: null, TestContext.Current.CancellationToken));

        Assert.Contains("brain.capture_refused", failure.Message, StringComparison.Ordinal);
        Assert.Contains(BrainFolderSource.NoBrainFolder, failure.Message, StringComparison.Ordinal);
    }

    /// <summary>The Brain is never what an empty argument means, on any of the
    /// four tools: a session that forgot its repository is told so.</summary>
    [Theory]
    [InlineData(DevbookTools.ListKnowledgeContexts, "")]
    [InlineData(DevbookTools.ListKnowledgeContexts, "  ")]
    [InlineData(DevbookTools.ReadKnowledgeChapter, "")]
    [InlineData(DevbookTools.ReadKnowledgeChapter, "  ")]
    [InlineData(DevbookTools.ListAnnotations, "")]
    [InlineData(DevbookTools.ListAnnotations, "  ")]
    [InlineData(DevbookTools.ResolveAnnotation, "")]
    [InlineData(DevbookTools.ResolveAnnotation, "  ")]
    public async Task A_blank_repository_is_still_required_rather_than_read_as_the_brain(string tool, string repository)
    {
        var note = Notes.Note(BrainScope.Value, NotePath, blockIndex: 1);
        var tools = Tools(out var store, out _, notes: [note]);

        Func<Task> call = tool switch
        {
            DevbookTools.ListKnowledgeContexts => () => Task.FromResult(tools.ListKnowledgeContextsFor(repository)),
            DevbookTools.ReadKnowledgeChapter => () => tools.ReadKnowledgeChapterAsync(
                repository, NotePath, review: false, TestContext.Current.CancellationToken),
            DevbookTools.ListAnnotations => () => Task.FromResult(tools.ListAnnotationsOn(repository, NotePath)),
            DevbookTools.ResolveAnnotation => () => Task.FromResult(tools.ResolveAnnotationFor(repository, note.Id)),
            _ => throw new ArgumentOutOfRangeException(nameof(tool), tool, null)
        };

        var failure = await Assert.ThrowsAsync<McpException>(call);

        Assert.Contains("repository.required", failure.Message, StringComparison.Ordinal);
        Assert.Empty(store.Resolved);
    }

    private DevbookTools Tools(
        out FakeDevbookAnnotationStore store,
        out FakeRepositoryDirectory directory,
        DevbookAnnotation[]? notes = null) =>
        Tools(out store, out directory, BrainRoot, notes);

    private DevbookTools Tools(
        out FakeDevbookAnnotationStore store,
        out FakeRepositoryDirectory directory,
        string? brainFolder,
        DevbookAnnotation[]? notes = null)
    {
        store = new FakeDevbookAnnotationStore(notes ?? []);
        directory = new FakeRepositoryDirectory([Backlog]);

        var brain = new BrainFolderSource(() => brainFolder);

        return new DevbookTools(
            new FakeDevbookFolderSource(_root.FullName, new DevbookFolderSetting(".arc42", "Architecture", ".arc42")),
            store,
            directory,
            brain,
            new BrainNoteWriter(brain));
    }
}
