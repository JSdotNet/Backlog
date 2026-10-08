using Backlog.Modules.Tasks.Abstractions;
using Backlog.Modules.Tasks.Abstractions.Connectors;
using Backlog.Modules.Tasks.DomainModels;
using Backlog.Modules.Tasks.Features.DeleteDisconnectedLinkedTasks;
using Backlog.Modules.Tasks.Features.SyncLinkedTasks;
using Backlog.SharedKernel.Results;

namespace Backlog.Modules.Tasks.UnitTests.LinkedTasks;

/// <summary>
/// The linked tasks a disconnected source left behind (ADR 0020, §5): which sources
/// are listed, and deleting one source's tasks. What is worth holding: only a pair
/// with no connected target is listed, its count covers every status, a pair
/// connected again is refused rather than emptied, the delete is a tombstone that
/// touches nothing else, and a tombstone keeps the sync from bringing the task
/// back once the source is connected again.
/// </summary>
public sealed class DeleteDisconnectedLinkedTasksTests
{
    private const string Repo = "JSdotNet/Backlog";
    private const string OtherRepo = "JSdotNet/Other";
    private static readonly DateTimeOffset Now = new(2026, 10, 5, 9, 0, 0, TimeSpan.Zero);

    private readonly InMemoryTaskRepository _tasks = new();
    private readonly InMemoryConnectedTargets _targets = new();

    // --- Listing ----------------------------------------------------------------

    [Fact]
    public async Task A_source_with_tasks_and_no_connected_target_is_listed_with_every_status_counted()
    {
        Seed(Linked("I_1", status: EntryStatus.Ready));
        Seed(Linked("I_2", status: EntryStatus.Done));
        Seed(Linked("I_3", status: EntryStatus.Archived));

        var source = Assert.Single(await ListAsync());

        Assert.Equal(new DisconnectedSource(StubTaskConnector.Id, Repo, TaskCount: 3, DependentCount: 0), source);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task A_connected_target_is_never_listed_whether_or_not_it_syncs(bool enabled)
    {
        Seed(Linked("I_1"));
        _targets.Save(new ConnectedTarget(StubTaskConnector.Id, "jsdotnet/backlog", enabled));

        Assert.Empty(await ListAsync());
    }

    [Fact]
    public async Task Targets_are_grouped_without_regard_to_case_but_connectors_are_told_apart_exactly()
    {
        Seed(Linked("I_1", target: "jsdotnet/backlog"));
        var latest = Seed(Linked("I_2", target: Repo));
        latest.SetDependsOn([]);
        Seed(Linked("I_3", connectorId: "Stub"));

        var sources = await ListAsync();

        Assert.Equal(
            [
                new DisconnectedSource("Stub", Repo, 1, 0),
                new DisconnectedSource(StubTaskConnector.Id, Repo, 2, 0),
            ],
            sources);
    }

    [Fact]
    public async Task Local_tasks_and_deleted_ones_are_not_counted_and_a_source_with_only_deleted_tasks_is_not_listed()
    {
        Seed(Local("Write the release notes"));
        Seed(Linked("I_1"));
        Seed(Linked("I_2")).MarkDeleted();
        Seed(Linked("I_3", target: OtherRepo)).MarkDeleted();

        var source = Assert.Single(await ListAsync());

        Assert.Equal(Repo, source.Target);
        Assert.Equal(1, source.TaskCount);
    }

    /// <summary>What the confirmation warns about: a task outside the source that
    /// waits on one of its tasks shows as blocked on a missing task afterwards. A
    /// task of the source waiting on another of its own is deleted with it, and a
    /// deleted task waits on nothing.</summary>
    [Fact]
    public async Task Tasks_outside_the_source_that_wait_on_its_tasks_are_counted_as_dependents()
    {
        var first = Seed(Linked("I_1"));
        var second = Seed(Linked("I_2"));
        second.SetDependsOn([first.Id.ToString()]);

        Seed(Local("Waits on one")).SetDependsOn([first.Id.ToString()]);
        Seed(Local("Waits on both")).SetDependsOn([first.Id.ToString(), second.Id.ToString()]);
        Seed(Local("Waits on something else")).SetDependsOn([Guid.NewGuid().ToString()]);
        var deleted = Seed(Local("Deleted, waiting"));
        deleted.SetDependsOn([first.Id.ToString()]);
        deleted.MarkDeleted();

        var source = Assert.Single(await ListAsync());

        Assert.Equal(2, source.DependentCount);
    }

    /// <summary>A finished task waits on nothing — the chain never reads a Done or
    /// Archived task's dependencies — so it is no dependent to warn about. A task of
    /// the source that is itself Done is still one a live task can wait on.</summary>
    [Fact]
    public async Task Done_and_archived_tasks_that_wait_on_the_source_are_not_counted_as_dependents()
    {
        var first = Seed(Linked("I_1", status: EntryStatus.Done));

        Seed(Local("Still open")).SetDependsOn([first.Id.ToString()]);
        Seed(Local("Finished", EntryStatus.Done)).SetDependsOn([first.Id.ToString()]);
        Seed(Local("Put away", EntryStatus.Archived)).SetDependsOn([first.Id.ToString()]);

        var source = Assert.Single(await ListAsync());

        Assert.Equal(1, source.DependentCount);
    }

    // --- Deleting ---------------------------------------------------------------

    [Fact]
    public async Task Deleting_a_source_tombstones_every_one_of_its_tasks_and_nothing_else()
    {
        var ready = Seed(Linked("I_1", status: EntryStatus.Ready));
        var done = Seed(Linked("I_2", status: EntryStatus.Done, target: "jsdotnet/backlog"));
        var archived = Seed(Linked("I_3", status: EntryStatus.Archived));
        var otherSource = Seed(Linked("I_4", target: OtherRepo));
        var local = Seed(Local("Write the release notes"));
        local.SetDependsOn([ready.Id.ToString()]);
        var connected = Seed(Linked("I_5", connectorId: "connected"));
        _targets.Save(new ConnectedTarget("connected", Repo));

        var result = await DeleteAsync(StubTaskConnector.Id, Repo);

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.ToString() : null);
        Assert.Equal(3, result.Value);
        Assert.All([ready, done, archived], task => Assert.NotNull(task.DeletedAt));
        Assert.All([otherSource, local, connected], task => Assert.Null(task.DeletedAt));

        // The dependent is left as it was: the same as deleting one task by hand.
        Assert.Equal([ready.Id.ToString()], local.DependsOn);
        Assert.Equal(3, _tasks.Writes);
    }

    [Fact]
    public async Task A_source_connected_again_before_the_delete_runs_is_refused_and_nothing_is_deleted()
    {
        var task = Seed(Linked("I_1"));
        _targets.Save(new ConnectedTarget(StubTaskConnector.Id, "jsdotnet/backlog"));

        var result = await DeleteAsync(StubTaskConnector.Id, Repo);

        Assert.True(result.IsFailure);
        Assert.Equal(DeleteDisconnectedLinkedTasksCommand.StillConnectedCode, result.Error.Code);
        Assert.Contains(Repo, result.Error.Message, StringComparison.Ordinal);
        Assert.Null(task.DeletedAt);
        Assert.Equal(0, _tasks.Writes);
    }

    [Theory]
    [InlineData("", Repo)]
    [InlineData(StubTaskConnector.Id, " ")]
    public async Task A_blank_connector_or_target_is_refused(string connectorId, string target)
    {
        Seed(Linked("I_1"));

        var result = await DeleteAsync(connectorId, target);

        Assert.True(result.IsFailure);
        Assert.Equal(0, _tasks.Writes);
    }

    /// <summary>The point of a tombstone over a removed row: connecting the source
    /// again and syncing it does not bring the deleted tasks back.</summary>
    [Fact]
    public async Task Connecting_the_source_again_does_not_bring_the_deleted_tasks_back()
    {
        var connector = new StubTaskConnector();
        connector.Items.Add(Item("I_1"));
        connector.Items.Add(Item("I_2"));
        _targets.Save(new ConnectedTarget(StubTaskConnector.Id, Repo));
        var first = await SyncAsync(connector);
        Assert.Equal(2, first.Created);

        _targets.Remove(StubTaskConnector.Id, Repo);
        Assert.Equal(2, (await DeleteAsync(StubTaskConnector.Id, Repo)).Value);

        _targets.Save(new ConnectedTarget(StubTaskConnector.Id, Repo));
        var again = await SyncAsync(connector);

        Assert.Equal(0, again.Created);
        Assert.Equal(2, again.SkippedTombstoned);
        Assert.Empty(await _tasks.ListAsync(TestContext.Current.CancellationToken));
    }

    // --- Helpers ----------------------------------------------------------------

    private async Task<IReadOnlyList<DisconnectedSource>> ListAsync() =>
        await new ListDisconnectedSourcesQueryHandler(_tasks, _targets)
            .Handle(new ListDisconnectedSourcesQuery(), TestContext.Current.CancellationToken);

    private async Task<Result<int>> DeleteAsync(string connectorId, string target) =>
        await new DeleteDisconnectedLinkedTasksCommandHandler(_tasks, _targets)
            .Handle(new DeleteDisconnectedLinkedTasksCommand(connectorId, target), TestContext.Current.CancellationToken);

    private async Task<LinkedTaskSyncSummary> SyncAsync(StubTaskConnector connector)
    {
        var result = await new SyncLinkedTasksCommandHandler([connector], _tasks, _targets, new ManualTimeProvider(Now))
            .Handle(new SyncLinkedTasksCommand(StubTaskConnector.Id, Repo), TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.ToString() : null);
        return result.Value;
    }

    private TaskItem Seed(TaskItem task)
    {
        _tasks.Entries[task.Id] = task;
        return task;
    }

    private static TaskItem Local(string title, EntryStatus status = EntryStatus.Ready) =>
        new(Guid.NewGuid(), title, string.Empty, EntryType.Task, status, Priority.Medium, null, null, null, Now);

    private static TaskItem Linked(
        string externalId,
        EntryStatus status = EntryStatus.Ready,
        string connectorId = StubTaskConnector.Id,
        string target = Repo)
    {
        var task = new TaskItem(
            LinkedTaskIds.For(connectorId, externalId), $"Item {externalId}", string.Empty, EntryType.Task, status, Priority.Medium, null, null, null, Now);
        task.SetSourceRef(new SourceRef(connectorId, target, externalId, $"https://example.test/{externalId}", $"#{externalId}", null, "open", Now));
        return task;
    }

    private static SourceItem Item(string externalId) =>
        new(
            externalId,
            $"#{externalId}",
            $"Item {externalId}",
            $"https://example.test/{externalId}",
            $"Body of {externalId}",
            NormalisedSourceState.Open,
            "open",
            Assignee: null,
            UpdatedAt: Now.AddDays(-1),
            Labels: []);
}
