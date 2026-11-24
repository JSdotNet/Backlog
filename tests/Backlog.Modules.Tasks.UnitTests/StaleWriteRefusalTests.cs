using Backlog.Modules.Tasks.Abstractions;
using Backlog.Modules.Tasks.Abstractions.DataTransferObjects;
using Backlog.Modules.Tasks.Abstractions.Services;
using Backlog.Modules.Tasks.DomainModels;
using Backlog.Modules.Tasks.Extensions;
using Backlog.SharedKernel.Results;

using Microsoft.Extensions.DependencyInjection;

namespace Backlog.Modules.Tasks.UnitTests;

/// <summary>
/// The published port answers a save the store refused as stale with
/// <c>tasks.stale_write</c>, so the editor and the MCP tools can act on it — and
/// nothing reached the store (SqliteTaskStaleWriteTests proves the row is untouched).
/// </summary>
public sealed class StaleWriteRefusalTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_text_save_the_store_refuses_answers_stale_write()
    {
        var (entries, _, id) = Port();

        var saved = await entries.SaveFromTextAsync(id, "# Renamed\n", 0, cancellationToken: Token);

        AssertStale(saved.Value);
    }

    [Fact]
    public async Task A_link_the_store_refuses_answers_stale_write()
    {
        var (entries, _, id) = Port();

        var linked = await entries.LinkToIssueAsync(id, "JSdotNet/Backlog", "7", EntryProjectionDto.PullRequestTargetType, Token);

        AssertStale(linked.Value);
    }

    [Fact]
    public async Task A_comment_the_store_refuses_answers_stale_write()
    {
        var (entries, _, id) = Port();

        var commented = await TaskComments.CommentAsync(entries, id, "A note.", new DateOnly(2026, 11, 24), "test", Token);

        AssertStale(commented.Value);
    }

    /// <summary>A usage record refused because somebody saved the entry in between is
    /// recorded on the version read again — the comment tool records usage after its
    /// comment landed, and must not report the comment as failed.</summary>
    [Fact]
    public async Task A_usage_record_refused_once_is_recorded_on_a_fresh_read()
    {
        var (entries, store, id) = Port(refusals: 1);

        await entries.RecordUsageAsync(id, "mcp-comment", Token);

        Assert.Contains(store.Entries[id].UsageEvents, usage => usage.Action == "mcp-comment");
    }

    private static void AssertStale(object? outcome)
    {
        var error = Assert.IsType<Error>(outcome);
        Assert.Equal(TaskEntryErrorCodes.StaleWrite, error.Code);
        Assert.Equal(ErrorType.Conflict, error.Type);
        Assert.Contains("Read the entry again", error.Message, StringComparison.Ordinal);
    }

    private static (ITaskItems Entries, InMemoryTaskRepository Store, Guid Id) Port(int refusals = int.MaxValue)
    {
        var entry = new TaskItem("Shared entry", string.Empty, EntryType.Task);
        var store = new InMemoryTaskRepository(entry);

        var services = new ServiceCollection();
        services.AddTasksModule();
        services.AddSingleton<ITaskRepository>(new RefusingStore(store, refusals));
        services.AddSingleton<IRepositoryDirectory>(new FakeRepositoryDirectory("JSdotNet/Backlog"));

        var provider = services.BuildServiceProvider();
        var scope = provider.CreateScope();

        return (scope.ServiceProvider.GetRequiredService<ITaskItems>(), store, entry.Id);
    }

    /// <summary>A store that reads like the in-memory one and refuses its first
    /// <paramref name="refusals"/> saves as stale, the way SqliteTaskRepository
    /// refuses one when another writer saved first.</summary>
    // Differs from InMemoryTaskRepository: reads through one and refuses saves as stale.
    private sealed class RefusingStore(InMemoryTaskRepository inner, int refusals) : ITaskRepository
    {
        private int _refusals = refusals;

        public Task SaveAsync(TaskItem task, CancellationToken cancellationToken = default)
        {
            if (_refusals-- > 0) throw new StaleTaskWriteException(task.Id);
            return inner.SaveAsync(task, cancellationToken);
        }

        public Task<TaskItem?> GetAsync(Guid id, CancellationToken cancellationToken = default) =>
            inner.GetAsync(id, cancellationToken);

        public Task<TaskItem?> GetIncludingDeletedAsync(Guid id, CancellationToken cancellationToken = default) =>
            inner.GetIncludingDeletedAsync(id, cancellationToken);

        public Task<IReadOnlyList<TaskItem>> ListAsync(CancellationToken cancellationToken = default) =>
            inner.ListAsync(cancellationToken);

        public Task<IReadOnlyList<TaskItem>> ListChangedSinceAsync(DateTimeOffset since, CancellationToken cancellationToken = default) =>
            inner.ListChangedSinceAsync(since, cancellationToken);
    }
}
