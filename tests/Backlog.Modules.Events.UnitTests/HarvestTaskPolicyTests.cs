using Backlog.Modules.Events.Abstractions;
using Backlog.Modules.Events.DomainModels;
using Backlog.Modules.Events.Services;

namespace Backlog.Modules.Events.UnitTests;

/// <summary>
/// The Harvest Task Policy (<c>.devbook/domain/events/domain.md#harvest-task-policy</c>), at its
/// two enforcement points in <c>domain.invariants.md#harvest-task-policy</c>: <c>FileDue()</c>,
/// over the in-memory store and the task target at the boundary, on the fixture's day, Tuesday
/// 10 November 2026; and <c>OnHarvested()</c>, through the pane's move into Harvested, which is
/// the transition that triggers it.
/// </summary>
public sealed class HarvestTaskPolicyTests : EventsPaneFixture
{
    private static readonly DateOnly Today = new(2026, 11, 10);

    private HarvestTaskPolicy Policy => new(Store, Tasks);

    // --- Only a Going event past its end day is due --------------------------------------

    [Fact]
    public async Task A_going_event_gets_its_harvest_task_the_day_after_it_ends()
    {
        var id = await Saved("Techorama Belgium", KnowledgeEventState.Going, new(2026, 11, 7), new(2026, 11, 9));

        await Policy.FileDueAsync(Today, Token);

        var link = Assert.Single((await Store.GetAsync(id, Token))!.Tasks);
        Assert.Equal(TaskPurpose.Harvest, link.Purpose);
        Assert.Equal("Harvest Techorama Belgium", Tasks.Held[link.TaskId].Title);
    }

    [Fact]
    public async Task A_going_event_is_not_due_on_its_last_day()
    {
        await Saved("Techorama Belgium", KnowledgeEventState.Going, new(2026, 11, 8), new(2026, 11, 10));

        await Policy.FileDueAsync(Today, Token);

        Assert.Empty(Tasks.Filed);
    }

    [Fact]
    public async Task A_going_event_without_dates_is_never_due()
    {
        await Saved("dotNET Friday", KnowledgeEventState.Going, start: null);

        await Policy.FileDueAsync(Today, Token);

        Assert.Empty(Tasks.Filed);
    }

    [Fact]
    public async Task An_event_with_only_a_start_is_due_the_day_after_its_start()
    {
        await Saved("dotNET Friday", KnowledgeEventState.Going, new(2026, 11, 9));

        await Policy.FileDueAsync(Today, Token);

        Assert.Equal(["Harvest dotNET Friday"], Tasks.Filed);
    }

    [Theory]
    [InlineData(KnowledgeEventState.Spotted)]
    [InlineData(KnowledgeEventState.Considering)]
    [InlineData(KnowledgeEventState.Attended)]
    [InlineData(KnowledgeEventState.Harvested)]
    public async Task Only_a_going_event_is_due(KnowledgeEventState state)
    {
        await Saved("Techorama Belgium", state, new(2026, 11, 7), new(2026, 11, 9));

        await Policy.FileDueAsync(Today, Token);

        Assert.Empty(Tasks.Filed);
    }

    // --- One harvest task per event ------------------------------------------------------

    [Fact]
    public async Task A_second_check_files_no_second_harvest_task()
    {
        await Saved("Techorama Belgium", KnowledgeEventState.Going, new(2026, 11, 7), new(2026, 11, 9));

        await Policy.FileDueAsync(Today, Token);
        await Policy.FileDueAsync(Today.AddDays(1), Token);

        Assert.Equal(["Harvest Techorama Belgium"], Tasks.Filed);
    }

    [Fact]
    public async Task An_event_already_holding_a_harvest_task_gets_none_filed()
    {
        var held = Tasks.Hold("Write up Techorama");
        await Saved("Techorama Belgium", KnowledgeEventState.Going, new(2026, 11, 7), new(2026, 11, 9), EventTaskLink.Create(held, TaskPurpose.Harvest));

        await Policy.FileDueAsync(Today, Token);

        Assert.Empty(Tasks.Filed);
    }

    // --- One harvest task id per event ---------------------------------------------------

    /// <summary>Worked out apart from the code, over <c>backlog:harvest-task:</c> and the event
    /// id: the first sixteen bytes of the SHA-256, marked as an RFC 9562 version 8 UUID.</summary>
    private static readonly Guid Techorama = new("3f2b8a51-6c1e-4f1e-9d7a-0c4b1a2e9f10");

    private static readonly Guid TechoramaHarvest = new("2eb4e388-490c-8d44-b79d-a2fba293a2f1");

    [Fact]
    public void The_harvest_task_id_is_derived_from_the_event_id_alone()
    {
        var id = HarvestTaskPolicy.HarvestTaskId(Techorama);

        Assert.Equal(TechoramaHarvest, id);
        Assert.Equal(8, id.Version);
        Assert.Equal(0x8, id.Variant & 0xC);
    }

    /// <summary>Its own namespace: not the id Occurrence Spawning derives from the same id
    /// (<c>1f2cb991-517d-8446-85a7-4e7eaf7f8cfe</c>, worked out the same way over
    /// <c>backlog:occurrence-successor:</c>).</summary>
    [Fact]
    public void The_harvest_task_id_is_not_an_occurrence_successor_id()
    {
        Assert.NotEqual(new Guid("1f2cb991-517d-8446-85a7-4e7eaf7f8cfe"), HarvestTaskPolicy.HarvestTaskId(Techorama));
    }

    [Fact]
    public async Task The_harvest_task_is_filed_under_the_id_derived_from_the_event()
    {
        await SavedAs(Techorama, "Techorama Belgium", KnowledgeEventState.Going, new(2026, 11, 7), new(2026, 11, 9));

        var filed = await Policy.FileDueAsync(Today, Token);

        Assert.Equal(1, filed);
        Assert.Equal(TechoramaHarvest.ToString("D"), Assert.Single((await Store.GetAsync(Techorama, Token))!.Tasks).TaskId);
        Assert.Equal("Harvest Techorama Belgium", Tasks.Held[TechoramaHarvest.ToString("D")].Title);
    }

    /// <summary>A task pulled before its event, or one filed on an earlier check whose link was
    /// not saved: held here, so it is linked and not filed again.</summary>
    [Fact]
    public async Task A_derived_task_already_held_is_linked_and_not_filed_again()
    {
        var harvest = TechoramaHarvest.ToString("D");
        Tasks.Held[harvest] = new EventTaskFactsDto(harvest, "Harvest Techorama Belgium", "Draft");
        await SavedAs(Techorama, "Techorama Belgium", KnowledgeEventState.Going, new(2026, 11, 7), new(2026, 11, 9));

        var filed = await Policy.FileDueAsync(Today, Token);

        Assert.Equal(0, filed);
        Assert.Empty(Tasks.Filed);
        var link = Assert.Single((await Store.GetAsync(Techorama, Token))!.Tasks);
        Assert.Equal((harvest, TaskPurpose.Harvest), (link.TaskId, link.Purpose));
    }

    /// <summary>Deleting the harvest task is how the person said no: it is not filed again, and
    /// the event links its id, so every desktop's copy names the same one.</summary>
    [Fact]
    public async Task A_derived_task_deleted_here_is_linked_and_not_filed_again()
    {
        var harvest = TechoramaHarvest.ToString("D");
        Tasks.Deleted.Add(harvest);
        await SavedAs(Techorama, "Techorama Belgium", KnowledgeEventState.Going, new(2026, 11, 7), new(2026, 11, 9));

        await Policy.FileDueAsync(Today, Token);

        Assert.Empty(Tasks.Filed);
        Assert.Equal(harvest, Assert.Single((await Store.GetAsync(Techorama, Token))!.Tasks).TaskId);
    }

    [Fact]
    public async Task A_refused_filing_links_nothing_and_the_other_events_still_file()
    {
        var refused = await Saved("Techorama Belgium", KnowledgeEventState.Going, new(2026, 11, 7), new(2026, 11, 9));
        await Saved("dotNET Friday", KnowledgeEventState.Going, new(2026, 11, 6));
        Tasks.RefuseTitles.Add("Harvest Techorama Belgium");

        await Policy.FileDueAsync(Today, Token);

        Assert.Empty((await Store.GetAsync(refused, Token))!.Tasks);
        Assert.Equal(["Harvest dotNET Friday"], Tasks.Filed);
    }

    [Fact]
    public async Task A_filing_that_throws_does_not_stop_the_other_events()
    {
        await Saved("Techorama Belgium", KnowledgeEventState.Going, new(2026, 11, 7), new(2026, 11, 9));
        await Saved("dotNET Friday", KnowledgeEventState.Going, new(2026, 11, 6));
        Tasks.ThrowTitles.Add("Harvest Techorama Belgium");

        await Policy.FileDueAsync(Today, Token);

        Assert.Equal(["Harvest dotNET Friday"], Tasks.Filed);
    }

    /// <summary>The link is saved on the event as it stands once the task is filed, so a change
    /// the person made in the pane meanwhile is not written over.</summary>
    [Fact]
    public async Task A_change_made_while_the_task_files_is_kept()
    {
        var id = await Saved("Techorama", KnowledgeEventState.Going, new(2026, 11, 7), new(2026, 11, 9));
        Tasks.FileGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var filing = Policy.FileDueAsync(Today, Token);
        Assert.True((await Pane.RenameAsync(id, "Techorama Belgium", Token)).IsSuccess);
        Tasks.FileGate.SetResult();
        await filing;

        var after = (await Store.GetAsync(id, Token))!;
        Assert.Equal("Techorama Belgium", after.Title);
        Assert.Equal(TaskPurpose.Harvest, Assert.Single(after.Tasks).Purpose);
    }

    /// <summary>
    /// The requirement's scenario Two desktops
    /// (<c>.devbook/domain/events/requirements.md#requirement-one-harvest-task-per-event</c>): two
    /// desktops each hold their own copy of one Going event past its end day, and their own tasks,
    /// and both check the same morning before either syncs. Folded as the replicas fold them — the
    /// tasks by id, the event by its latest whole write — in either order, one harvest task is left,
    /// and the event that survives links it.
    /// </summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Two_desktops_file_one_harvest_task_between_them(bool firstWritesLast)
    {
        var techorama = KnowledgeEvent.Create(Guid.NewGuid(), "Techorama Belgium").Value;
        Assert.True(techorama.SetDates(EventDate.Create(new(2026, 11, 7), null, null).Value, EventDate.Create(new(2026, 11, 9), null, null).Value, DateSource.Typed).IsSuccess);
        Assert.True(techorama.MoveTo(KnowledgeEventState.Going).IsSuccess);
        var (firstEvents, firstTasks) = (new InMemoryKnowledgeEvents(), new FakeEventTaskTarget());
        var (secondEvents, secondTasks) = (new InMemoryKnowledgeEvents(), new FakeEventTaskTarget());
        await firstEvents.SaveAsync(techorama, Token);
        await secondEvents.SaveAsync(techorama, Token);

        await new HarvestTaskPolicy(firstEvents, firstTasks).FileDueAsync(Today, Token);
        await new HarvestTaskPolicy(secondEvents, secondTasks).FileDueAsync(Today, Token);

        var (earlier, later) = firstWritesLast ? (secondTasks, firstTasks) : (firstTasks, secondTasks);
        var tasks = new Dictionary<string, EventTaskFactsDto>(earlier.Held, StringComparer.Ordinal);
        foreach (var (id, task) in later.Held) tasks[id] = task;
        var firstLink = Assert.Single((await firstEvents.GetAsync(techorama.Id, Token))!.Tasks).TaskId;
        var secondLink = Assert.Single((await secondEvents.GetAsync(techorama.Id, Token))!.Tasks).TaskId;
        var surviving = firstWritesLast ? firstLink : secondLink;

        var harvest = Assert.Single(tasks.Values, task => task.Title == "Harvest Techorama Belgium");
        Assert.Equal(firstLink, secondLink);
        Assert.Equal(harvest.TaskId, surviving);
    }

    // --- The policy never changes an event's state ----------------------------------------

    [Fact]
    public async Task Filing_a_harvest_task_leaves_the_event_going()
    {
        var id = await Saved("Techorama Belgium", KnowledgeEventState.Going, new(2026, 11, 7), new(2026, 11, 9));

        await Policy.FileDueAsync(Today, Token);

        Assert.Equal(KnowledgeEventState.Going, (await Store.GetAsync(id, Token))!.State);
    }

    [Fact]
    public async Task Finishing_the_harvest_task_in_tasks_leaves_the_event_going()
    {
        var id = await Saved("Techorama Belgium", KnowledgeEventState.Going, new(2026, 11, 7), new(2026, 11, 9));
        await Policy.FileDueAsync(Today, Token);
        var harvest = Assert.Single((await Store.GetAsync(id, Token))!.Tasks).TaskId;

        await Tasks.CompleteAsync(harvest, Token);
        await Policy.FileDueAsync(Today.AddDays(1), Token);

        var after = (await Store.GetAsync(id, Token))!;
        Assert.Equal(KnowledgeEventState.Going, after.State);
        Assert.Equal(harvest, Assert.Single(after.Tasks).TaskId);
    }

    // --- Harvested completes only the event's own harvest task --------------------------

    [Fact]
    public async Task Marking_the_event_harvested_completes_its_open_harvest_task()
    {
        var harvest = Tasks.Hold("Harvest Techorama Belgium");
        var id = await Saved("Techorama Belgium", KnowledgeEventState.Attended, new(2026, 11, 7), new(2026, 11, 9), EventTaskLink.Create(harvest, TaskPurpose.Harvest));

        await Pane.MoveToAsync(id, KnowledgeEventState.Harvested, Token);

        Assert.Equal("Done", Tasks.Held[harvest].Status);
    }

    [Fact]
    public async Task The_harvested_event_reads_its_harvest_task_as_done()
    {
        var harvest = Tasks.Hold("Harvest Techorama Belgium");
        var id = await Saved("Techorama Belgium", KnowledgeEventState.Attended, new(2026, 11, 7), new(2026, 11, 9), EventTaskLink.Create(harvest, TaskPurpose.Harvest));

        var moved = await Pane.MoveToAsync(id, KnowledgeEventState.Harvested, Token);

        Assert.Equal("Done", Assert.Single(moved.Value.Tasks).Status);
    }

    [Theory]
    [InlineData("Done")]
    [InlineData("Archived")]
    public async Task A_finished_harvest_task_is_left_as_it_is(string status)
    {
        var harvest = Tasks.Hold("Harvest Techorama Belgium", status);
        var id = await Saved("Techorama Belgium", KnowledgeEventState.Attended, new(2026, 11, 7), new(2026, 11, 9), EventTaskLink.Create(harvest, TaskPurpose.Harvest));

        await Pane.MoveToAsync(id, KnowledgeEventState.Harvested, Token);

        Assert.Equal(status, Tasks.Held[harvest].Status);
    }

    [Fact]
    public async Task Harvested_completes_no_prep_or_linked_task()
    {
        var prep = Tasks.Hold("Book hotel");
        var linked = Tasks.Hold("Ask about training budget");
        var harvest = Tasks.Hold("Harvest Techorama Belgium");
        var id = await Saved(
            "Techorama Belgium", KnowledgeEventState.Attended, new(2026, 11, 7), new(2026, 11, 9),
            EventTaskLink.Create(prep, TaskPurpose.Prep),
            EventTaskLink.Create(linked, TaskPurpose.Linked),
            EventTaskLink.Create(harvest, TaskPurpose.Harvest));

        await Pane.MoveToAsync(id, KnowledgeEventState.Harvested, Token);

        Assert.Equal([harvest], Tasks.Completed);
    }

    [Fact]
    public async Task An_event_without_a_harvest_task_completes_nothing_when_harvested()
    {
        var prep = Tasks.Hold("Book hotel");
        var id = await Saved("Techorama Belgium", KnowledgeEventState.Attended, new(2026, 11, 7), new(2026, 11, 9), EventTaskLink.Create(prep, TaskPurpose.Prep));

        await Pane.MoveToAsync(id, KnowledgeEventState.Harvested, Token);

        Assert.Empty(Tasks.Completed);
    }

    [Fact]
    public async Task Another_move_completes_no_harvest_task()
    {
        var harvest = Tasks.Hold("Harvest Techorama Belgium");
        var id = await Saved("Techorama Belgium", KnowledgeEventState.Going, new(2026, 11, 7), new(2026, 11, 9), EventTaskLink.Create(harvest, TaskPurpose.Harvest));

        await Pane.MoveToAsync(id, KnowledgeEventState.Attended, Token);

        Assert.Empty(Tasks.Completed);
    }

    [Fact]
    public async Task A_failed_completion_still_moves_the_event_to_harvested()
    {
        var harvest = Tasks.Hold("Harvest Techorama Belgium");
        var id = await Saved("Techorama Belgium", KnowledgeEventState.Attended, new(2026, 11, 7), new(2026, 11, 9), EventTaskLink.Create(harvest, TaskPurpose.Harvest));
        Tasks.RefuseCompletionWith = EventsErrors.TaskNotInBacklog();

        var moved = await Pane.MoveToAsync(id, KnowledgeEventState.Harvested, Token);

        Assert.True(moved.IsSuccess);
        Assert.Equal(KnowledgeEventState.Harvested, (await Store.GetAsync(id, Token))!.State);
    }

    [Fact]
    public async Task A_completion_that_throws_still_moves_the_event_to_harvested()
    {
        var harvest = Tasks.Hold("Harvest Techorama Belgium");
        var id = await Saved("Techorama Belgium", KnowledgeEventState.Attended, new(2026, 11, 7), new(2026, 11, 9), EventTaskLink.Create(harvest, TaskPurpose.Harvest));
        Tasks.ThrowOnComplete = new InvalidOperationException("The Tasks store could not be written.");

        var moved = await Pane.MoveToAsync(id, KnowledgeEventState.Harvested, Token);

        Assert.Equal(KnowledgeEventState.Harvested, moved.Value.State);
    }

    /// <summary>An event saved straight into the store, in <paramref name="state"/>, dated
    /// <paramref name="start"/> to <paramref name="end"/>, holding <paramref name="tasks"/>.</summary>
    private Task<Guid> Saved(string title, KnowledgeEventState state, DateOnly? start, DateOnly? end = null, params EventTaskLink[] tasks) =>
        SavedAs(Guid.NewGuid(), title, state, start, end, tasks);

    /// <summary>As <see cref="Saved"/>, under <paramref name="id"/>.</summary>
    private async Task<Guid> SavedAs(Guid id, string title, KnowledgeEventState state, DateOnly? start, DateOnly? end = null, params EventTaskLink[] tasks)
    {
        var e = KnowledgeEvent.Create(id, title).Value;

        if (start is { } from)
        {
            var to = end is { } last ? EventDate.Create(last, null, null).Value : null;
            Assert.True(e.SetDates(EventDate.Create(from, null, null).Value, to, DateSource.Typed).IsSuccess);
        }

        if (state != KnowledgeEventState.Spotted) Assert.True(e.MoveTo(state).IsSuccess);

        foreach (var task in tasks) Assert.True(e.LinkTask(task).IsSuccess);

        await Store.SaveAsync(e, Token);
        return e.Id;
    }
}
