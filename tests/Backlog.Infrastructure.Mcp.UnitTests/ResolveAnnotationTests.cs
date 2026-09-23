using Backlog.Modules.Devbook.Abstractions;
using Backlog.Modules.Tasks.Abstractions.Services;

using ModelContextProtocol;

namespace Backlog.Infrastructure.Mcp.UnitTests;

/// <summary>
/// <c>resolve_annotation</c> — the one write this library publishes.
/// <para>
/// It is how a session closes the loop local ADR 0012 §6 describes: the answer
/// goes into the chapter as a devbook fence, written by the session in its own
/// checkout, and then the private note is marked answered here. So what these
/// tests are about is mostly what it refuses, because a write that resolves the
/// wrong note is worse than one that refuses a right one.
/// </para>
/// </summary>
public sealed class ResolveAnnotationTests
{
    private const string ChapterPath = ".arc42/adr/0012-mcp.md";

    private static readonly TasksRepositoryRef Backlog = new("backlog", "JSdotNet", "Backlog");

    private static DevbookTools Tools(out FakeDevbookAnnotationStore store, params DevbookAnnotation[] notes)
    {
        store = new FakeDevbookAnnotationStore(notes);

        return new DevbookTools(
            new FakeDevbookFolderSource(Path.GetTempPath(), new DevbookFolderSetting(".arc42", "Architecture", ".arc42")),
            store,
            new FakeRepositoryDirectory([Backlog]));
    }

    [Fact]
    public void A_note_that_was_answered_is_marked_resolved()
    {
        var note = Notes.Note(Backlog.Alias, ChapterPath, blockIndex: 2, body: "Does this still hold?");
        var tools = Tools(out var store, note);

        var answer = tools.ResolveAnnotationFor("JSdotNet/Backlog", note.Id);

        Assert.Equal((note.Id, true), Assert.Single(store.Resolved));

        // The caller is told what the store now holds, so it does not have to ask
        // again to find out whether the write took.
        Assert.True(answer.Resolved);
        Assert.Equal(note.Id, answer.Id);
        Assert.Equal("Does this still hold?", answer.Body);
    }

    [Fact]
    public void Resolving_a_note_that_is_already_resolved_writes_nothing()
    {
        // Idempotent is what the tool advertises, and a second call is what a
        // session that lost its place will make.
        var note = Notes.Note(Backlog.Alias, ChapterPath, blockIndex: 2, resolved: true);
        var tools = Tools(out var store, note);

        var answer = tools.ResolveAnnotationFor("JSdotNet/Backlog", note.Id);

        Assert.Empty(store.Resolved);
        Assert.True(answer.Resolved);
    }

    [Fact]
    public void An_id_the_store_does_not_hold_is_refused()
    {
        var tools = Tools(out var store);

        var failure = Assert.Throws<McpException>(() => tools.ResolveAnnotationFor("JSdotNet/Backlog", Guid.NewGuid()));

        Assert.Contains("annotation.notFound", failure.Message, StringComparison.Ordinal);
        Assert.Empty(store.Resolved);
    }

    [Fact]
    public void A_deleted_note_is_refused_rather_than_quietly_resolved()
    {
        // A tombstone is still in the store, so Find answers — and resolving one
        // would be writing to something the person took back.
        var note = Notes.Note(
            Backlog.Alias,
            ChapterPath,
            blockIndex: 2,
            deletedAt: new DateTimeOffset(2026, 9, 22, 10, 0, 0, TimeSpan.Zero));

        var tools = Tools(out var store, note);

        var failure = Assert.Throws<McpException>(() => tools.ResolveAnnotationFor("JSdotNet/Backlog", note.Id));

        Assert.Contains("annotation.notFound", failure.Message, StringComparison.Ordinal);
        Assert.Empty(store.Resolved);
    }

    [Fact]
    public void An_empty_draft_is_refused()
    {
        // Nobody has written it yet, so there is nothing that could have been
        // answered. Resolving it would retire a blank note off the person's screen.
        var draft = Notes.Note(Backlog.Alias, ChapterPath, blockIndex: 2, body: "   ");
        var tools = Tools(out var store, draft);

        var failure = Assert.Throws<McpException>(() => tools.ResolveAnnotationFor("JSdotNet/Backlog", draft.Id));

        Assert.Contains("annotation.draft", failure.Message, StringComparison.Ordinal);
        Assert.Empty(store.Resolved);
    }

    [Fact]
    public void A_note_belonging_to_another_repository_is_refused()
    {
        // The repository is an argument and the note carries its own alias. A
        // session working in one checkout resolving another repository's note is a
        // mistake, not a shortcut.
        var elsewhere = Notes.Note("other-repo", ChapterPath, blockIndex: 2, body: "Somewhere else.");
        var tools = Tools(out var store, elsewhere);

        var failure = Assert.Throws<McpException>(() => tools.ResolveAnnotationFor("JSdotNet/Backlog", elsewhere.Id));

        Assert.Contains("annotation.otherRepository", failure.Message, StringComparison.Ordinal);
        Assert.Empty(store.Resolved);
    }

    [Fact]
    public void A_repository_the_directory_does_not_know_is_refused()
    {
        var note = Notes.Note(Backlog.Alias, ChapterPath, blockIndex: 2);
        var tools = Tools(out var store, note);

        Assert.Throws<McpException>(() => tools.ResolveAnnotationFor("JSdotNet/Unknown", note.Id));
        Assert.Empty(store.Resolved);
    }
}
