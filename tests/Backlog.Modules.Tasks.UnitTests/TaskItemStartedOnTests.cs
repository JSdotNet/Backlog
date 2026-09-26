using Backlog.Modules.Tasks.Abstractions;
using Backlog.Modules.Tasks.DomainModels;
using Backlog.Modules.Tasks.Features.SaveTaskFromText;

namespace Backlog.Modules.Tasks.UnitTests;

/// <summary>
/// <see cref="TaskItem.StartedOn"/> records the local day work first moved to
/// In progress, and is never moved once set.
/// <para>
/// The aggregate half is driven directly; the save half goes through the real
/// save use case, because what matters there is the order in which the parsed
/// token and the status land — a <c>started:</c> token in the text wins, and a
/// text with none still gets stamped by the status change.
/// </para>
/// </summary>
public sealed class TaskItemStartedOnTests
{
    private static DateOnly Today => DateOnly.FromDateTime(DateTime.Now);

    [Fact]
    public void A_new_entry_has_not_started()
    {
        var entry = new TaskItem("Title", string.Empty, EntryType.Task);

        Assert.Null(entry.StartedOn);
    }

    [Fact]
    public void Moving_to_in_progress_stamps_today()
    {
        var entry = new TaskItem("Title", string.Empty, EntryType.Task);

        entry.ChangeStatus(EntryStatus.Ready);
        Assert.Null(entry.StartedOn);

        entry.ChangeStatus(EntryStatus.InProgress);
        Assert.Equal(Today, entry.StartedOn);
    }

    [Fact]
    public void Setting_the_status_to_in_progress_stamps_today()
    {
        var entry = new TaskItem("Title", string.Empty, EntryType.Task);

        entry.SetStatus(EntryStatus.InProgress);

        Assert.Equal(Today, entry.StartedOn);
    }

    [Fact]
    public void Other_statuses_do_not_stamp()
    {
        var entry = new TaskItem("Title", string.Empty, EntryType.Task);

        entry.SetStatus(EntryStatus.Done);

        Assert.Null(entry.StartedOn);
    }

    /// <summary>Back to Ready and in again started on the first day, not the
    /// second — and neither status path moves a date already carried.</summary>
    [Fact]
    public void The_first_start_is_kept_through_a_round_trip_out_of_and_back_into_progress()
    {
        var first = new DateOnly(2026, 9, 2);
        var entry = new TaskItem("Title", string.Empty, EntryType.Task);
        entry.SetStartedOn(first);

        entry.ChangeStatus(EntryStatus.Ready);
        entry.ChangeStatus(EntryStatus.InProgress);
        Assert.Equal(first, entry.StartedOn);

        entry.SetStatus(EntryStatus.Draft);
        entry.SetStatus(EntryStatus.InProgress);
        Assert.Equal(first, entry.StartedOn);
    }

    [Fact]
    public async Task Saving_text_that_moves_an_entry_in_progress_stamps_it()
    {
        var store = new InMemoryTaskRepository();
        var id = await Save(store, null, "# Title\n`task` `!ready`\n");
        Assert.Null(store.Entries[id].StartedOn);

        await Save(store, id, "# Title\n`task` `!in-progress`\n");

        Assert.Equal(Today, store.Entries[id].StartedOn);
    }

    [Fact]
    public async Task Creating_an_entry_already_in_progress_stamps_it()
    {
        var store = new InMemoryTaskRepository();

        var id = await Save(store, null, "# Title\n`task` `!in-progress`\n");

        Assert.Equal(Today, store.Entries[id].StartedOn);
    }

    [Fact]
    public async Task A_started_token_in_the_text_is_kept_over_the_automatic_stamp()
    {
        var store = new InMemoryTaskRepository();

        var created = await Save(store, null, "# Title\n`task` `!in-progress` `started:2026-09-02`\n");
        Assert.Equal(new DateOnly(2026, 9, 2), store.Entries[created].StartedOn);

        var updated = await Save(store, null, "# Other\n`task` `!ready`\n");
        await Save(store, updated, "# Other\n`task` `!in-progress` `started:2026-08-30`\n");
        Assert.Equal(new DateOnly(2026, 8, 30), store.Entries[updated].StartedOn);
    }

    private static async Task<Guid> Save(InMemoryTaskRepository store, Guid? id, string rawText)
    {
        var result = await new SaveTaskFromTextCommandHandler(store, new FakeRepositoryDirectory())
            .Handle(new SaveTaskFromTextCommand(id, rawText, 0));

        Assert.True(result.IsSuccess);
        return result.Value.Entry.Id;
    }

    /// <summary>The smallest store a save needs: the aggregates themselves,
    /// because the storage format is not what is under test.</summary>
    private sealed class InMemoryTaskRepository : ITaskRepository
    {
        public Dictionary<Guid, TaskItem> Entries { get; } = [];

        public Task SaveAsync(TaskItem entry, CancellationToken cancellationToken = default)
        {
            Entries[entry.Id] = entry;
            return Task.CompletedTask;
        }

        public Task<TaskItem?> GetAsync(Guid id, CancellationToken cancellationToken = default) =>
            Task.FromResult(Entries.TryGetValue(id, out var entry) && entry.DeletedAt is null ? entry : null);

        public Task<TaskItem?> GetIncludingDeletedAsync(Guid id, CancellationToken cancellationToken = default) =>
            Task.FromResult(Entries.TryGetValue(id, out var entry) ? entry : null);

        public Task<IReadOnlyList<TaskItem>> ListAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<TaskItem>>([.. Entries.Values.Where(entry => entry.DeletedAt is null)]);

        public Task<IReadOnlyList<TaskItem>> ListChangedSinceAsync(
            DateTimeOffset since,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<TaskItem>>(
                [.. Entries.Values.Where(entry => entry.UpdatedAt > since).OrderBy(entry => entry.UpdatedAt)]);
    }
}
