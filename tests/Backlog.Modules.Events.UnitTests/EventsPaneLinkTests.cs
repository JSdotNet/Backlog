using Backlog.Modules.Events.Abstractions;
using Backlog.Modules.Events.DomainModels;
using Backlog.SharedKernel.Results;

namespace Backlog.Modules.Events.UnitTests;

/// <summary>Harvest note links and the event's tasks, through the pane's port.</summary>
public sealed class EventsPaneLinkTests : EventsPaneFixture
{
    private const string TechoramaNote = "areas/learning/techorama.md";

    // --- AC-6, AC-14 Brain notes --------------------------------------------------------

    [Fact]
    public async Task A_linked_note_reads_its_current_title()
    {
        Brain.Notes[TechoramaNote] = "Techorama takeaways";
        var id = await Add("Techorama Belgium");

        var linked = await Pane.LinkBrainNoteAsync(id, TechoramaNote, cancellationToken: Token);
        Brain.Notes[TechoramaNote] = "Techorama 2026 takeaways";

        var note = Assert.Single((await Pane.GetAsync(id, Token)).Value.Notes);
        Assert.True(linked.IsSuccess);
        Assert.False(note.Missing);
        Assert.Equal("Techorama 2026 takeaways", note.Title);
    }

    [Fact]
    public async Task A_note_that_moved_shows_as_missing_with_the_title_it_had()
    {
        Brain.Notes[TechoramaNote] = "Techorama takeaways";
        var id = await Add("Techorama Belgium");
        await Pane.LinkBrainNoteAsync(id, TechoramaNote, cancellationToken: Token);

        Brain.Notes.Remove(TechoramaNote);

        var note = Assert.Single((await Pane.GetAsync(id, Token)).Value.Notes);
        Assert.True(note.Missing);
        Assert.Equal("Techorama takeaways", note.Title);
    }

    [Fact]
    public async Task A_note_is_linked_once()
    {
        var id = await Add("Techorama Belgium");

        await Pane.LinkBrainNoteAsync(id, TechoramaNote, cancellationToken: Token);
        var again = await Pane.LinkBrainNoteAsync(id, @"\areas\learning\techorama.md", cancellationToken: Token);

        Assert.Equal(TechoramaNote, Assert.Single(again.Value.Notes).Reference);
    }

    [Fact]
    public async Task A_blank_note_path_is_refused()
    {
        var id = await Add("Techorama Belgium");

        var linked = await Pane.LinkBrainNoteAsync(id, " ", cancellationToken: Token);

        Assert.Equal("note_link.path_missing", linked.Error.Code);
    }

    [Fact]
    public async Task A_missing_note_is_linked_again_in_one_save()
    {
        var id = await Add("Techorama Belgium");
        await Pane.LinkBrainNoteAsync(id, TechoramaNote, "Techorama", Token);
        Brain.Notes["archive/techorama-2026.md"] = "Techorama 2026";
        var saves = Store.Saves;

        var relinked = await Pane.RelinkBrainNoteAsync(id, TechoramaNote, "archive/techorama-2026.md", cancellationToken: Token);

        var note = Assert.Single(relinked.Value.Notes);
        Assert.Equal(("archive/techorama-2026.md", "Techorama 2026", false), (note.Reference, note.Title, note.Missing));
        Assert.Equal(saves + 1, Store.Saves);
    }

    [Fact]
    public async Task A_wrong_note_can_be_unlinked()
    {
        var id = await Add("Techorama Belgium");
        await Pane.LinkBrainNoteAsync(id, TechoramaNote, cancellationToken: Token);

        var unlinked = await Pane.UnlinkNoteAsync(id, NoteLinkKind.Brain, TechoramaNote, Token);

        Assert.Empty(unlinked.Value.Notes);
    }

    [Fact]
    public async Task The_brains_unavailable_reason_and_candidates_come_from_the_brain()
    {
        Brain.UnavailableReason = "Pick a folder for your Brain in Settings › Storage.";
        Brain.Notes[TechoramaNote] = "Techorama takeaways";

        Assert.Equal("Pick a folder for your Brain in Settings › Storage.", Pane.BrainUnavailableReason);
        Assert.Equal([new EventBrainNoteDto(TechoramaNote, "Techorama takeaways")], await Pane.BrainNoteCandidatesAsync(Token));
    }

    // --- AC-8, AC-9 Prep tasks ----------------------------------------------------------

    [Fact]
    public async Task A_prep_task_is_filed_with_its_title_alone_and_listed_by_id_with_its_status()
    {
        var id = await Add("Techorama Belgium");
        await Pane.MoveToAsync(id, KnowledgeEventState.Going, Token);

        var read = await Pane.AddPrepTaskAsync(id, " Book hotel in Antwerp ", Token);

        Assert.Equal(["Book hotel in Antwerp"], Tasks.Filed);
        var task = Assert.Single(read.Value.Tasks);
        Assert.Equal(TaskPurpose.Prep, task.Purpose);
        Assert.True(task.InBacklog);
        Assert.Equal(("Book hotel in Antwerp", "Draft"), (task.Title, task.Status));
        Assert.True(Tasks.Held.ContainsKey(task.TaskId));
    }

    [Fact]
    public async Task A_blank_prep_task_title_files_nothing()
    {
        var id = await Add("Techorama Belgium");

        var read = await Pane.AddPrepTaskAsync(id, "  ", Token);

        Assert.Equal("event_task.title_missing", read.Error.Code);
        Assert.Equal(0, Tasks.Writes);
    }

    [Fact]
    public async Task A_refused_filing_links_nothing()
    {
        var id = await Add("Techorama Belgium");
        Tasks.RefuseWith = Error.Validation("entry.needs_title", "An entry needs a title line.");

        var read = await Pane.AddPrepTaskAsync(id, "Book hotel", Token);

        Assert.Equal("entry.needs_title", read.Error.Code);
        Assert.Empty((await Pane.GetAsync(id, Token)).Value.Tasks);
    }

    [Fact]
    public async Task No_task_is_filed_for_an_event_that_is_not_there()
    {
        var read = await Pane.AddPrepTaskAsync(Guid.NewGuid(), "Book hotel", Token);

        Assert.Equal("knowledge_event.not_found", read.Error.Code);
        Assert.Equal(0, Tasks.Writes);
    }

    // --- AC-10 Linking an existing task -------------------------------------------------

    [Fact]
    public async Task An_existing_task_is_linked_by_its_id_and_nothing_is_written_to_it()
    {
        var budget = Tasks.Hold("Ask about training budget", "Ready");
        var id = await Add("Techorama Belgium");
        await Pane.MoveToAsync(id, KnowledgeEventState.Considering, Token);

        var read = await Pane.LinkExistingTaskAsync(id, "{" + budget.ToUpperInvariant() + "}", Token);

        var task = Assert.Single(read.Value.Tasks);
        Assert.Equal((budget, TaskPurpose.Linked, "Ask about training budget", "Ready"), (task.TaskId, task.Purpose, task.Title, task.Status));
        Assert.Equal(0, Tasks.Writes);
    }

    [Theory]
    [InlineData("0c8a1b4e-2f6d-4a37-9c11-5e0b7d3f2a90")]
    [InlineData("not-a-task")]
    public async Task A_task_this_backlog_does_not_hold_is_refused_in_words(string taskId)
    {
        var id = await Add("Techorama Belgium");

        var read = await Pane.LinkExistingTaskAsync(id, taskId, Token);

        Assert.Equal("event_task.not_in_backlog", read.Error.Code);
        Assert.Equal("No task with that id in this backlog.", read.Error.Message);
        Assert.Empty((await Pane.GetAsync(id, Token)).Value.Tasks);
    }

    // --- AC-15 The tasks section --------------------------------------------------------

    [Fact]
    public async Task A_linked_task_this_backlog_no_longer_holds_reads_as_not_in_this_backlog()
    {
        var gone = "6f9619ff-8b86-d011-b42d-00c04fc964ff";
        var stored = KnowledgeEvent.Create(Guid.NewGuid(), "Techorama Belgium").Value;
        stored.LinkTask(EventTaskLink.Create(gone, TaskPurpose.Prep));
        await Store.SaveAsync(stored, Token);

        var task = Assert.Single((await Pane.GetAsync(stored.Id, Token)).Value.Tasks);

        Assert.Equal((gone, false, null, null), (task.TaskId, task.InBacklog, task.Title, task.Status));
    }

    [Fact]
    public void A_change_to_a_task_or_the_brain_reaches_the_pane_until_it_unsubscribes()
    {
        var heard = 0;
        void Listen() => heard++;

        Pane.Changed += Listen;
        Tasks.RaiseChanged();
        Brain.RaiseChanged();
        Pane.Changed -= Listen;
        Tasks.RaiseChanged();

        Assert.Equal(2, heard);
    }
}
