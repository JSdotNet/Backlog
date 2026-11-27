using Backlog.Modules.Events.Abstractions;
using Backlog.Modules.Events.DomainModels;
using Backlog.SharedKernel.Results;

namespace Backlog.Modules.Events.UnitTests;

/// <summary>
/// The Harvest Task Policy's second job on the Harvested transition (local ADR 0073 §8;
/// <c>.devbook/domain/events/domain.md#harvest-task-policy</c>): each Talk note the event links is
/// written into the Brain's inbox with its words unchanged, its link becomes a Brain link to that
/// path, and the Inbox note is archived. Through the pane's move, over the Inbox and the Brain at
/// the boundary (<see cref="FakeEventTalkNotes"/>).
/// </summary>
public sealed class HarvestTalkNotesTests : EventsPaneFixture
{
    private static readonly Guid Keynote = Guid.Parse("6f1c2d3e-0000-4000-8000-000000000001");

    private static readonly Guid Workshop = Guid.Parse("6f1c2d3e-0000-4000-8000-000000000002");

    [Fact]
    public async Task Each_talk_note_is_written_into_the_brains_inbox_with_its_words_unchanged()
    {
        TalkNotes.Inbox[Keynote] = ("Keynote Aspire", "Dashboards *first*.\n\n# not a heading for us");
        TalkNotes.Inbox[Workshop] = ("Workshop", "Bring a laptop.");
        var id = await Attended(Talk(Keynote, "Keynote Aspire"), Talk(Workshop, "Workshop"));

        await Pane.MoveToAsync(id, KnowledgeEventState.Harvested, Token);

        Assert.Equal(("Keynote Aspire", "Dashboards *first*.\n\n# not a heading for us"), TalkNotes.Written["_inbox/keynote-aspire.md"]);
        Assert.Equal(("Workshop", "Bring a laptop."), TalkNotes.Written["_inbox/workshop.md"]);
    }

    [Fact]
    public async Task The_talk_link_becomes_a_brain_link_to_that_path_in_its_place()
    {
        TalkNotes.Inbox[Keynote] = ("Keynote Aspire", "Dashboards first.");
        Brain.Notes["areas/aspire.md"] = "Aspire";
        Brain.Notes["_inbox/keynote-aspire.md"] = "Keynote Aspire";
        var id = await Attended(
            NoteLink.Create(NoteLinkKind.Brain, "areas/aspire.md", "Aspire"),
            Talk(Keynote, "Keynote Aspire"),
            NoteLink.Create(NoteLinkKind.Brain, "areas/later.md", "Later"));

        var moved = await Pane.MoveToAsync(id, KnowledgeEventState.Harvested, Token);

        var notes = Assert.IsType<Ok<KnowledgeEventDto>>(moved.Value).Value.Notes;
        Assert.Equal(
            [(NoteLinkKind.Brain, "areas/aspire.md"), (NoteLinkKind.Brain, "_inbox/keynote-aspire.md"), (NoteLinkKind.Brain, "areas/later.md")],
            notes.Select(note => (note.Kind, note.Reference)));
        Assert.Equal("Keynote Aspire", notes[1].Title);
        Assert.False(notes[1].Missing);
        Assert.DoesNotContain((await Store.GetAsync(id, Token))!.Notes, note => note.Kind == NoteLinkKind.Talk);
    }

    [Fact]
    public async Task The_inbox_note_is_archived_once_the_brain_link_is_saved()
    {
        TalkNotes.Inbox[Keynote] = ("Keynote Aspire", "Dashboards first.");
        var id = await Attended(Talk(Keynote, "Keynote Aspire"));
        NoteLinkKind? linkedAtArchive = null;
        TalkNotes.OnArchive = async () => linkedAtArchive = (await Store.GetAsync(id, Token))!.Notes.Single().Kind;

        await Pane.MoveToAsync(id, KnowledgeEventState.Harvested, Token);

        Assert.Equal([$"file:{Keynote:D}", $"archive:{Keynote:D}"], TalkNotes.Calls);
        Assert.Equal([Keynote], TalkNotes.Archived);
        Assert.Equal(NoteLinkKind.Brain, linkedAtArchive);
    }

    [Fact]
    public async Task With_no_brain_set_the_talk_note_and_its_link_stay()
    {
        TalkNotes.BrainSet = false;
        TalkNotes.Inbox[Keynote] = ("Keynote Aspire", "Dashboards first.");
        var id = await Attended(Talk(Keynote, "Keynote Aspire"));

        var moved = await Pane.MoveToAsync(id, KnowledgeEventState.Harvested, Token);

        var held = Assert.IsType<Ok<KnowledgeEventDto>>(moved.Value).Value;
        Assert.Equal(KnowledgeEventState.Harvested, held.State);
        Assert.Equal((NoteLinkKind.Talk, Keynote.ToString("D")), (Assert.Single(held.Notes).Kind, held.Notes[0].Reference));
        Assert.Empty(TalkNotes.Written);
        Assert.Empty(TalkNotes.Archived);
    }

    [Fact]
    public async Task A_talk_note_the_inbox_no_longer_holds_keeps_its_link()
    {
        TalkNotes.Inbox[Workshop] = ("Workshop", "Bring a laptop.");
        var id = await Attended(Talk(Keynote, "Keynote Aspire"), Talk(Workshop, "Workshop"));

        await Pane.MoveToAsync(id, KnowledgeEventState.Harvested, Token);

        var notes = (await Store.GetAsync(id, Token))!.Notes;
        Assert.Equal([(NoteLinkKind.Talk, Keynote.ToString("D")), (NoteLinkKind.Brain, "_inbox/workshop.md")], notes.Select(note => (note.Kind, note.Reference)));
        Assert.Equal([Workshop], TalkNotes.Archived);
    }

    [Fact]
    public async Task A_filing_that_throws_keeps_that_link_and_the_other_notes_still_move()
    {
        TalkNotes.Inbox[Keynote] = ("Keynote Aspire", "Dashboards first.");
        TalkNotes.Inbox[Workshop] = ("Workshop", "Bring a laptop.");
        TalkNotes.ThrowOnFiling = Keynote;
        var id = await Attended(Talk(Keynote, "Keynote Aspire"), Talk(Workshop, "Workshop"));

        var moved = await Pane.MoveToAsync(id, KnowledgeEventState.Harvested, Token);

        Assert.Equal(KnowledgeEventState.Harvested, Assert.IsType<Ok<KnowledgeEventDto>>(moved.Value).Value.State);
        var notes = (await Store.GetAsync(id, Token))!.Notes;
        Assert.Equal([(NoteLinkKind.Talk, Keynote.ToString("D")), (NoteLinkKind.Brain, "_inbox/workshop.md")], notes.Select(note => (note.Kind, note.Reference)));
        Assert.Equal([Workshop], TalkNotes.Archived);
    }

    [Fact]
    public async Task A_refused_archive_still_links_the_brain_note()
    {
        TalkNotes.Inbox[Keynote] = ("Keynote Aspire", "Dashboards first.");
        TalkNotes.RefuseArchiveWith = Error.Conflict("inbox.item.invalid_transition", "Already archived.");
        var id = await Attended(Talk(Keynote, "Keynote Aspire"));

        var moved = await Pane.MoveToAsync(id, KnowledgeEventState.Harvested, Token);

        var held = Assert.IsType<Ok<KnowledgeEventDto>>(moved.Value).Value;
        Assert.Equal(KnowledgeEventState.Harvested, held.State);
        Assert.Equal((NoteLinkKind.Brain, "_inbox/keynote-aspire.md"), (Assert.Single(held.Notes).Kind, held.Notes[0].Reference));
    }

    [Fact]
    public async Task A_brain_link_that_is_not_saved_leaves_the_talk_link_and_the_inbox_note()
    {
        TalkNotes.Inbox[Keynote] = ("Keynote Aspire", "Dashboards first.");
        var id = await Attended(Talk(Keynote, "Keynote Aspire"));
        TalkNotes.OnFiling = () => Store.FailNextSave = true;

        var moved = await Pane.MoveToAsync(id, KnowledgeEventState.Harvested, Token);

        var held = Assert.IsType<Ok<KnowledgeEventDto>>(moved.Value).Value;
        Assert.Equal(KnowledgeEventState.Harvested, held.State);
        Assert.Equal(NoteLinkKind.Talk, Assert.Single(held.Notes).Kind);
        Assert.Equal(NoteLinkKind.Talk, Assert.Single((await Store.GetAsync(id, Token))!.Notes).Kind);
        Assert.Empty(TalkNotes.Archived);
    }

    [Fact]
    public async Task A_cancel_after_the_first_note_is_written_still_finishes_the_pass()
    {
        using var cancel = CancellationTokenSource.CreateLinkedTokenSource(Token);
        TalkNotes.Inbox[Keynote] = ("Keynote Aspire", "Dashboards first.");
        TalkNotes.Inbox[Workshop] = ("Workshop", "Bring a laptop.");
        var id = await Attended(Talk(Keynote, "Keynote Aspire"), Talk(Workshop, "Workshop"));
        TalkNotes.OnFiling = () => { if (TalkNotes.Written.Count == 1) cancel.Cancel(); };

        await Pane.MoveToAsync(id, KnowledgeEventState.Harvested, cancel.Token);

        Assert.Equal([Keynote, Workshop], TalkNotes.Archived);
        Assert.All((await Store.GetAsync(id, Token))!.Notes, note => Assert.Equal(NoteLinkKind.Brain, note.Kind));
    }

    [Fact]
    public async Task Another_move_files_no_talk_note()
    {
        TalkNotes.Inbox[Keynote] = ("Keynote Aspire", "Dashboards first.");
        var id = await Attended(Talk(Keynote, "Keynote Aspire"));

        await Pane.SetVerdictAsync(id, "Worth it.", Token);

        Assert.Empty(TalkNotes.Calls);
        Assert.Equal(NoteLinkKind.Talk, Assert.Single((await Store.GetAsync(id, Token))!.Notes).Kind);
    }

    [Fact]
    public async Task A_brain_path_filed_away_later_shows_as_missing()
    {
        TalkNotes.Inbox[Keynote] = ("Keynote Aspire", "Dashboards first.");
        var id = await Attended(Talk(Keynote, "Keynote Aspire"));
        await Pane.MoveToAsync(id, KnowledgeEventState.Harvested, Token);

        // tend-brain filed the note away from _inbox/, so nothing is at the linked path.
        var read = await Pane.GetAsync(id, Token);

        var note = Assert.Single(Assert.IsType<Ok<KnowledgeEventDto>>(read.Value).Value.Notes);
        Assert.Equal((NoteLinkKind.Brain, "_inbox/keynote-aspire.md", true), (note.Kind, note.Reference, note.Missing));
    }

    [Fact]
    public async Task The_harvest_task_is_still_completed_beside_the_talk_notes()
    {
        TalkNotes.Inbox[Keynote] = ("Keynote Aspire", "Dashboards first.");
        var harvest = Tasks.Hold("Harvest Techorama Belgium");
        var id = await Attended(Talk(Keynote, "Keynote Aspire"));
        var e = (await Store.GetAsync(id, Token))!;
        Assert.IsType<Ok>(e.LinkTask(EventTaskLink.Create(harvest, TaskPurpose.Harvest)).Value);
        await Store.SaveAsync(e, Token);

        await Pane.MoveToAsync(id, KnowledgeEventState.Harvested, Token);

        Assert.Equal("Done", Tasks.Held[harvest].Status);
        Assert.Equal([Keynote], TalkNotes.Archived);
    }

    private static NoteLink Talk(Guid noteId, string title) => NoteLink.Create(NoteLinkKind.Talk, noteId.ToString("D"), title);

    /// <summary>An Attended event saved straight into the store, linking <paramref name="notes"/>
    /// in that order.</summary>
    private async Task<Guid> Attended(params NoteLink[] notes)
    {
        var e = Assert.IsType<Ok<KnowledgeEvent>>(KnowledgeEvent.Create(Guid.NewGuid(), "Techorama Belgium").Value).Value;
        Assert.IsType<Ok>(e.MoveTo(KnowledgeEventState.Attended).Value);
        foreach (var note in notes) Assert.IsType<Ok>(e.LinkNote(note).Value);

        await Store.SaveAsync(e, Token);
        return e.Id;
    }
}
