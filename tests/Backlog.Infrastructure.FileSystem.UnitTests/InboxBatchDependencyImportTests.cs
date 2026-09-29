using Backlog.Infrastructure.FileSystem.Inbox;
using Backlog.Infrastructure.Sqlite;
using Backlog.Modules.Inbox.Abstractions.DataTransferObjects;
using Backlog.Modules.Tasks;
using Backlog.Modules.Tasks.Abstractions.Services;
using Backlog.Modules.Tasks.Extensions;

using Microsoft.Extensions.DependencyInjection;

namespace Backlog.Infrastructure.FileSystem.UnitTests;

/// <summary>
/// The <c>after:</c> tokens a batch writes, carried through Tasks' real import
/// into a real store. The adapter's own tests pin the text; these pin what the
/// import makes of it — ADR 0007's resolution scope: a name inside the document
/// resolves there, a name of an earlier batch's item resolves to the entry that
/// item became, and a name of nothing stays written, so the task reads Blocked.
/// </summary>
public sealed class InboxBatchDependencyImportTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "backlog-inbox-batch-dependencies", Guid.NewGuid().ToString("n"));

    private readonly ServiceProvider _provider;

    public InboxBatchDependencyImportTests()
    {
        Directory.CreateDirectory(_root);

        _provider = new ServiceCollection()
            .AddSingleton<ITaskRepository>(new RootedSqliteTaskRepository(() => _root))
            .AddSingleton<IRepositoryDirectory, NoRepositories>()
            .AddTasksModule()
            .BuildServiceProvider();
    }

    private ITaskItems Tasks => _provider.GetRequiredService<ITaskItems>();

    private InboxBacklogTarget Target => new(Tasks, new NoRepositories());

    [Fact]
    public async Task A_dependency_inside_the_batch_resolves_to_the_entry_made_in_the_same_import()
    {
        var first = Guid.CreateVersion7();
        var second = Guid.CreateVersion7();

        var result = await Target.CreateBatchTasksAsync(
            new InboxBatchRouteRequestDto(
                [
                    new InboxRouteRequestDto(first, "Set up the pipeline", string.Empty, null, [], []),
                    new InboxRouteRequestDto(second, "Deploy the preview", string.Empty, null, [], [], null, [first]),
                ],
                "+inbox-batch-1a2b3c4d"),
            TestContext.Current.CancellationToken);

        Assert.Null(result.DocumentRefused);
        var made = result.Routed.ToDictionary(routed => routed.InboxItemId, routed => Assert.Single(routed.TaskIds));
        var waiting = await EntryAsync(made[second]);
        Assert.Equal([made[first].ToString()], waiting.DependsOn, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task A_dependency_on_an_earlier_batchs_item_resolves_to_the_task_it_became()
    {
        var earlier = Guid.CreateVersion7();
        var later = Guid.CreateVersion7();

        var firstBatch = await Target.CreateBatchTasksAsync(
            new InboxBatchRouteRequestDto([new InboxRouteRequestDto(earlier, "Read the contract", string.Empty, null, [], [])], "+inbox-batch-00000001"),
            TestContext.Current.CancellationToken);
        var earlierTask = Assert.Single(Assert.Single(firstBatch.Routed).TaskIds);

        // What the proposal hands the route for a task an Inbox batch made: the
        // id: it was imported under, which is the item's own id.
        var stored = await EntryAsync(earlierTask);
        var after = DependencyTarget.ForTask(new InboxTaskReferenceDto(stored.Id, stored.ImportItemId, stored.Title, [], [], [])).After!;
        Assert.Equal(earlier.ToString("D"), after);

        var secondBatch = await Target.CreateBatchTasksAsync(
            new InboxBatchRouteRequestDto([new InboxRouteRequestDto(later, "Sign it", string.Empty, null, [], [], null, null, [after])], "+inbox-batch-00000002"),
            TestContext.Current.CancellationToken);

        var waiting = await EntryAsync(Assert.Single(Assert.Single(secondBatch.Routed).TaskIds));
        Assert.Equal([earlierTask.ToString()], waiting.DependsOn, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task A_dependency_on_a_task_that_no_longer_exists_stays_on_the_entry()
    {
        var item = Guid.CreateVersion7();
        var gone = Guid.CreateVersion7().ToString("D");

        var result = await Target.CreateBatchTasksAsync(
            new InboxBatchRouteRequestDto([new InboxRouteRequestDto(item, "Waits on nothing there", string.Empty, null, [], [], null, null, [gone])], "+inbox-batch-00000003"),
            TestContext.Current.CancellationToken);

        // Written as given and kept: a value naming no task is one the chain
        // reads as still waiting (TaskChain.Resolve), so the task is Blocked.
        var waiting = await EntryAsync(Assert.Single(Assert.Single(result.Routed).TaskIds));
        Assert.Equal([gone], waiting.DependsOn);
    }

    private async Task<Backlog.Modules.Tasks.Abstractions.DataTransferObjects.TaskItemDto> EntryAsync(Guid id) =>
        Assert.Single(await Tasks.ListAsync(TestContext.Current.CancellationToken), entry => entry.Id == id);

    public void Dispose()
    {
        _provider.Dispose();

        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
            // SQLite's pool can hold the file a moment past the last call; the
            // folder is under the temp directory and harmless if it stays.
        }
    }

    /// <summary>A workspace with no repositories: these batches name none.</summary>
    private sealed class NoRepositories : IRepositoryDirectory
    {
        public IReadOnlyList<TasksRepositoryRef> Repositories => [];

        public TasksRepositoryRef? Resolve(string name) => null;

        public TasksRepositoryRef Register(string name) => throw new InvalidOperationException("A batch never registers a repository.");
    }
}
