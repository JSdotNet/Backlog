using Backlog.Modules.Tasks.Abstractions;
using Backlog.Modules.Tasks.DomainModels;
using Backlog.Modules.Tasks.Features.ReorderTasks;

using Xunit;

namespace Backlog.Modules.Tasks.UnitTests;

/// <summary>
/// A drag writes the new ranking. The list it reads is whole aggregates, so the
/// entries that moved are written back from it — together, as one write — and
/// never fetched again one by one.
/// </summary>
public sealed class ReorderTasksTests
{
    [Fact]
    public async Task Only_the_entries_that_moved_are_written_and_in_one_write()
    {
        var first = Ranked("First", 0);
        var second = Ranked("Second", 1);
        var third = Ranked("Third", 2);
        var store = new RecordingTaskRepository(first, second, third);

        await new ReorderTasksCommandHandler(store).Handle(
            new ReorderTasksCommand([second.Id, first.Id, third.Id]),
            TestContext.Current.CancellationToken);

        var batch = Assert.Single(store.Batches);
        Assert.Equal([second.Id, first.Id], batch.Select(entry => entry.Id));
        Assert.Equal(0, second.Order);
        Assert.Equal(1, first.Order);
        Assert.Equal(2, third.Order);
        Assert.Equal(0, store.Gets);
        Assert.Equal(0, store.SingleSaves);
    }

    [Fact]
    public async Task An_unchanged_ranking_writes_nothing()
    {
        var first = Ranked("First", 0);
        var second = Ranked("Second", 1);
        var store = new RecordingTaskRepository(first, second);

        await new ReorderTasksCommandHandler(store).Handle(
            new ReorderTasksCommand([first.Id, second.Id]),
            TestContext.Current.CancellationToken);

        Assert.Empty(store.Batches);
        Assert.Equal(0, store.SingleSaves);
    }

    /// <summary>An id sent twice ends at the first position that moves it, as it
    /// did when each row was fetched afresh, and is written once.</summary>
    [Fact]
    public async Task An_id_sent_twice_ends_where_it_always_did_and_is_written_once()
    {
        var first = Ranked("First", 0);
        var second = Ranked("Second", 1);
        var store = new RecordingTaskRepository(first, second);

        await new ReorderTasksCommandHandler(store).Handle(
            new ReorderTasksCommand([second.Id, second.Id]),
            TestContext.Current.CancellationToken);

        var batch = Assert.Single(store.Batches);
        Assert.Equal([second.Id], batch.Select(entry => entry.Id));
        Assert.Equal(0, second.Order);
    }

    /// <summary>An id the list does not have — deleted while the drag was in the
    /// air, or tombstoned — is skipped rather than resurrected or thrown on.</summary>
    [Fact]
    public async Task An_id_the_list_does_not_have_is_skipped()
    {
        var first = Ranked("First", 0);
        var gone = Ranked("Gone", 1);
        gone.MarkDeleted();
        var store = new RecordingTaskRepository(first, gone);

        await new ReorderTasksCommandHandler(store).Handle(
            new ReorderTasksCommand([Guid.NewGuid(), gone.Id, first.Id]),
            TestContext.Current.CancellationToken);

        var batch = Assert.Single(store.Batches);
        Assert.Equal([first.Id], batch.Select(entry => entry.Id));
        Assert.Equal(2, first.Order);
    }

    private static TaskItem Ranked(string title, int order)
    {
        var task = new TaskItem(title, string.Empty, EntryType.Task);
        task.SetOrder(order);
        return task;
    }

    private sealed class RecordingTaskRepository(params TaskItem[] entries) : ITaskRepository
    {
        public List<IReadOnlyCollection<TaskItem>> Batches { get; } = [];

        public int Gets { get; private set; }

        public int SingleSaves { get; private set; }

        public Task SaveAsync(TaskItem task, CancellationToken cancellationToken = default)
        {
            SingleSaves++;
            return Task.CompletedTask;
        }

        public Task SaveManyAsync(IReadOnlyCollection<TaskItem> tasks, CancellationToken cancellationToken = default)
        {
            Batches.Add([.. tasks]);
            return Task.CompletedTask;
        }

        public Task<TaskItem?> GetAsync(Guid id, CancellationToken cancellationToken = default)
        {
            Gets++;
            return Task.FromResult(entries.FirstOrDefault(entry => entry.Id == id && entry.DeletedAt is null));
        }

        public Task<TaskItem?> GetIncludingDeletedAsync(Guid id, CancellationToken cancellationToken = default)
        {
            Gets++;
            return Task.FromResult(entries.FirstOrDefault(entry => entry.Id == id));
        }

        public Task<IReadOnlyList<TaskItem>> ListAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<TaskItem>>([.. entries.Where(entry => entry.DeletedAt is null)]);

        public Task<IReadOnlyList<TaskItem>> ListChangedSinceAsync(DateTimeOffset since, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<TaskItem>>([.. entries.Where(entry => entry.UpdatedAt > since)]);
    }
}
