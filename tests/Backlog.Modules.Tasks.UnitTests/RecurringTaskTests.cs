using System.Globalization;
using Backlog.Modules.Tasks.Abstractions;
using Backlog.Modules.Tasks.Abstractions.DataTransferObjects;
using Backlog.Modules.Tasks.DomainModels;
using Backlog.Modules.Tasks.Features.SaveTaskFromText;

namespace Backlog.Modules.Tasks.UnitTests;

/// <summary>
/// Completing a repeating entry leaves it completed and creates the next
/// occurrence beside it. These tests drive the real save use case, because the
/// spawn is deliberately part of saving rather than a policy behind an event: this
/// context publishes no domain events yet, and ADR 0006 already declined to put a
/// mediator between a caller and the use case it means.
/// </summary>
public sealed class RecurringTaskTests
{
    [Fact]
    public async Task Completing_a_repeating_entry_creates_exactly_one_successor()
    {
        var store = new InMemoryTaskRepository();

        var id = await Save(store, null, Text("!in-progress"));
        await Save(store, id, Text("!done"));

        var completed = store.Entries[id];
        var successor = store.Successor(id);

        // The finished occurrence stays finished: it is the record of what was
        // done, not a slot to roll forward.
        Assert.Equal(EntryStatus.Done, completed.Status);
        Assert.Equal(EntryStatus.Ready, successor.Status);
        Assert.Equal(new DateOnly(2026, 8, 28), successor.DueOn);
        Assert.Equal(completed.Id, successor.RecurrenceSourceId);
        Assert.Equal(new Recurrence(1, RecurrenceUnit.Week), successor.Recurrence);
    }

    /// <summary>
    /// The save says a successor was created, and which one.
    /// <para>
    /// It has to say so, because nothing else can. The completed entry is the one
    /// the caller already has and nothing on it changed; <c>recurrence_source_id</c>
    /// points backwards and lives on the successor, which is precisely the entry
    /// the caller has not got. A screen that inferred the spawn instead — from a
    /// status reaching done and a repeat being set — would be the spawn rule
    /// reimplemented somewhere it can drift from this one.
    /// </para>
    /// </summary>
    [Fact]
    public async Task The_save_reports_the_successor_it_created()
    {
        var store = new InMemoryTaskRepository();

        var id = await Save(store, null, Text("!in-progress"));
        var completion = await SaveResult(store, id, Text("!done"));

        Assert.Equal(store.Successor(id).Id, completion.SpawnedOccurrenceId);

        // And it is not the entry that was saved, which is the whole point.
        Assert.NotEqual(completion.Entry.Id, completion.SpawnedOccurrenceId);
    }

    [Fact]
    public async Task A_save_that_spawned_nothing_reports_nothing()
    {
        var store = new InMemoryTaskRepository();

        // A create, an ordinary edit, a completion with no repeat on it, and a
        // second save of an entry that was already done: four saves, no spawn, and
        // nothing for a caller to act on.
        var created = await SaveResult(store, null, Text("!in-progress"));
        Assert.Null(created.SpawnedOccurrenceId);

        Assert.Null((await SaveResult(store, created.Entry.Id, Text("!ready"))).SpawnedOccurrenceId);

        var plain = await SaveResult(store, null, "# Water the plants\n`task` `!in-progress`\n");
        Assert.Null((await SaveResult(store, plain.Entry.Id, "# Water the plants\n`task` `!done`\n")).SpawnedOccurrenceId);

        await SaveResult(store, created.Entry.Id, Text("!done"));
        Assert.Null((await SaveResult(store, created.Entry.Id, Text("!done"))).SpawnedOccurrenceId);
    }

    [Fact]
    public async Task A_late_completion_still_advances_from_the_original_due_date()
    {
        var store = new InMemoryTaskRepository();

        // Due years ago and only now ticked off. The repeat is anchored to the due
        // date rather than to the completion, so a schedule does not drift by
        // however late the person was — and the successor's date says nothing
        // about today.
        const string overdue = "# Weekly review\n`task` `{0}` `due:2020-01-03` `repeat:weekly`\n";

        var id = await Save(store, null, string.Format(CultureInfo.InvariantCulture, overdue, "!in-progress"));
        await Save(store, id, string.Format(CultureInfo.InvariantCulture, overdue, "!done"));

        Assert.Equal(new DateOnly(2020, 1, 10), store.Successor(id).DueOn);
    }

    [Fact]
    public async Task Completing_a_non_repeating_entry_creates_nothing()
    {
        var store = new InMemoryTaskRepository();

        var id = await Save(store, null, "# Water the plants\n`task` `!in-progress` `due:2026-08-21`\n");
        await Save(store, id, "# Water the plants\n`task` `!done` `due:2026-08-21`\n");

        Assert.Single(store.Entries);
    }

    /// <summary>
    /// The guard is on the step, not on the state: an entry that was already Done
    /// spawns nothing. Without that, every keystroke on a finished repeating entry
    /// would leave another successor behind it.
    /// </summary>
    [Fact]
    public async Task Saving_an_entry_that_is_already_done_does_not_spawn_a_second_successor()
    {
        var store = new InMemoryTaskRepository();

        var id = await Save(store, null, Text("!in-progress"));
        await Save(store, id, Text("!done"));
        await Save(store, id, Text("!done"));
        await Save(store, id, Text("!done") + "\nAnd a note typed afterwards.\n");

        Assert.Equal(2, store.Entries.Count);
    }

    [Fact]
    public async Task A_successor_carries_the_repeat_and_leaves_the_occurrence_behind()
    {
        var store = new InMemoryTaskRepository();

        const string body =
            "# Weekly review\n"
            + "`task` `*high` `{0}` `@repos` `#review` `due:2026-08-21` "
            + "`remind:2026-08-21T09:00` `repeat:weekly` `myday:2026-08-19` `after:a1b2c3`\n"
            + "\n"
            + "## Read last week's notes\n";

        var id = await Save(store, null, string.Format(CultureInfo.InvariantCulture, body, "!in-progress"));
        store.Entries[id].RecordUsage("copy");
        store.Entries[id].AddProjectionRef(new ProjectionRef("org/repo", "42", "issue"));

        await Save(store, id, string.Format(CultureInfo.InvariantCulture, body, "!done"));

        var successor = store.Successor(id);

        // What the repeat is of comes across.
        Assert.Equal("Weekly review", successor.Title);
        Assert.Equal(Priority.High, successor.Priority);
        Assert.Equal("repos", successor.Area);
        Assert.Equal(["review"], successor.Tags);
        Assert.Equal(["a1b2c3"], successor.DependsOn);
        Assert.Equal("Read last week's notes", Assert.Single(successor.SubItems).Title);
        Assert.Equal(SubItemStatus.Pending, Assert.Single(successor.SubItems).Status);

        // What was about the occurrence rather than the repeat does not.
        Assert.Null(successor.RemindAt);
        Assert.Null(successor.InMyDayOn);
        Assert.Empty(successor.ProjectionRefs);
        Assert.Empty(successor.UsageEvents);
    }

    [Fact]
    public async Task A_completed_repeat_with_no_due_date_spawns_an_undated_successor()
    {
        var store = new InMemoryTaskRepository();

        // Nothing to anchor the repeat to. Substituting today would make the
        // schedule depend on the moment somebody happened to tick the entry off.
        var id = await Save(store, null, "# Tidy up\n`task` `!in-progress` `repeat:weekly`\n");
        await Save(store, id, "# Tidy up\n`task` `!done` `repeat:weekly`\n");

        var successor = store.Successor(id);

        Assert.Null(successor.DueOn);
        Assert.Equal(EntryStatus.Ready, successor.Status);
    }

    [Fact]
    public async Task An_entry_typed_in_as_already_done_spawns_nothing()
    {
        var store = new InMemoryTaskRepository();

        // A create has no previous status for the save to have moved the entry
        // from, so nothing was completed here — an entry arriving finished is a
        // record of something done rather than an occurrence just now finishing.
        await Save(store, null, Text("!done"));

        Assert.Single(store.Entries);
    }

    [Theory]
    [InlineData("daily", "2026-08-22")]
    [InlineData("weekly", "2026-08-28")]
    [InlineData("monthly", "2026-09-21")]
    [InlineData("yearly", "2027-08-21")]
    [InlineData("2w", "2026-09-04")]
    // 21 August 2026 is a Friday, so the next working day is the Monday.
    [InlineData("weekdays", "2026-08-24")]
    public async Task The_successor_falls_due_on_the_next_date_the_repeat_produces(string repeat, string expected)
    {
        var store = new InMemoryTaskRepository();

        var id = await Save(store, null, Text("!in-progress", repeat));
        await Save(store, id, Text("!done", repeat));

        Assert.Equal(DateOnly.Parse(expected, CultureInfo.InvariantCulture), store.Successor(id).DueOn);
    }

    /// <summary>The last day of a month repeating monthly clamps to the last day
    /// of a shorter one, because the alternative is a date that does not
    /// exist.</summary>
    [Fact]
    public async Task A_monthly_repeat_on_the_thirty_first_clamps_to_a_shorter_month()
    {
        var store = new InMemoryTaskRepository();

        var id = await Save(store, null, "# Pay the invoice\n`task` `!in-progress` `due:2026-01-31` `repeat:monthly`\n");
        await Save(store, id, "# Pay the invoice\n`task` `!done` `due:2026-01-31` `repeat:monthly`\n");

        Assert.Equal(new DateOnly(2026, 2, 28), store.Successor(id).DueOn);
    }

    private static string Text(string statusToken, string repeat = "weekly") =>
        $"# Weekly review\n`task` `{statusToken}` `due:2026-08-21` `repeat:{repeat}`\n";

    private static async Task<Guid> Save(InMemoryTaskRepository store, Guid? id, string rawText) =>
        (await SaveResult(store, id, rawText)).Entry.Id;

    private static async Task<SavedTaskDto> SaveResult(InMemoryTaskRepository store, Guid? id, string rawText)
    {
        var result = await new SaveTaskFromTextCommandHandler(store)
            .Handle(new SaveTaskFromTextCommand(id, rawText, 0));

        Assert.True(result.IsSuccess);
        return result.Value;
    }

    /// <summary>The store a host would supply, small enough to read. Entries are
    /// held as the aggregates themselves rather than as serialized text, because
    /// what is under test is the spawn rather than the storage format.</summary>
    private sealed class InMemoryTaskRepository : ITaskRepository
    {
        public Dictionary<Guid, TaskItem> Entries { get; } = [];

        /// <summary>The one entry that is not the one saved — which is what a
        /// successor is, and asserting there is exactly one of them is half of
        /// what these tests are for.</summary>
        public TaskItem Successor(Guid completedId) =>
            Assert.Single(Entries.Values, entry => entry.Id != completedId);

        public Task SaveAsync(TaskItem entry, CancellationToken cancellationToken = default)
        {
            Entries[entry.Id] = entry;
            return Task.CompletedTask;
        }

        public Task<TaskItem?> GetAsync(Guid id, CancellationToken cancellationToken = default) =>
            Task.FromResult(Entries.TryGetValue(id, out var entry) ? entry : null);

        public Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
        {
            Entries.Remove(id);
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<TaskItem>> ListAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<TaskItem>>([.. Entries.Values]);
    }
}
