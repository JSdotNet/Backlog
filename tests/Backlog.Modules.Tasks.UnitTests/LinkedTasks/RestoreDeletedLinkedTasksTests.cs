using Backlog.Modules.Tasks.Abstractions;
using Backlog.Modules.Tasks.Abstractions.Connectors;
using Backlog.Modules.Tasks.DomainModels;
using Backlog.Modules.Tasks.Features.RestoreDeletedLinkedTasks;
using Backlog.Modules.Tasks.Features.SyncLinkedTasks;
using Backlog.SharedKernel.Results;

namespace Backlog.Modules.Tasks.UnitTests.LinkedTasks;

/// <summary>
/// The deleted linked tasks of a connected target, and bringing them back (ADR 0020,
/// §5) — the settings page's "Restore deleted tasks". What is worth holding: the
/// count covers tombstones only, per source; the restore brings back every deleted
/// task of exactly that connector and target, whether it went with its source or on
/// its own, and nothing else; and a pair no longer connected is refused, because
/// what was confirmed was restoring a connected target's tasks.
/// </summary>
public sealed class RestoreDeletedLinkedTasksTests
{
    private const string Repo = "JSdotNet/Backlog";
    private const string OtherRepo = "JSdotNet/Other";
    private static readonly DateTimeOffset Now = new(2026, 10, 5, 9, 0, 0, TimeSpan.Zero);

    private readonly InMemoryTaskRepository _tasks = new();
    private readonly InMemoryConnectedTargets _targets = new(new ConnectedTarget(StubTaskConnector.Id, Repo));

    // --- Listing ----------------------------------------------------------------

    [Fact]
    public async Task Each_source_with_deleted_tasks_is_listed_with_how_many_and_live_and_local_tasks_are_not_counted()
    {
        // Shown in the spelling of the one deleted last, as a disconnected source is.
        Seed(Linked("I_1", target: "jsdotnet/backlog")).MarkDeleted();
        Seed(Linked("I_2")).MarkDeleted();
        Seed(Linked("I_3"));
        Seed(Linked("I_4", target: OtherRepo)).MarkDeleted();
        Seed(Linked("I_5", target: "Live/Only"));
        Seed(Local("Deleted, but local")).MarkDeleted();

        var sources = await ListAsync();

        Assert.Equal(
            [
                new DeletedLinkedSource(StubTaskConnector.Id, Repo, 2),
                new DeletedLinkedSource(StubTaskConnector.Id, OtherRepo, 1),
            ],
            sources);
    }

    // --- Restoring --------------------------------------------------------------

    [Fact]
    public async Task Restoring_brings_back_every_deleted_task_of_the_target_and_nothing_else()
    {
        var withSource = Seed(Linked("I_1"));
        withSource.SetSourceRef(withSource.SourceRef!.WithFlag(LinkedTaskFlags.DeletedWithSource, set: true));
        withSource.MarkDeleted();
        var onItsOwn = Seed(Linked("I_2", target: "jsdotnet/backlog"));
        onItsOwn.MarkDeleted();
        var otherTarget = Seed(Linked("I_3", target: OtherRepo));
        otherTarget.MarkDeleted();
        var otherConnector = Seed(Linked("I_4", connectorId: "other"));
        otherConnector.MarkDeleted();
        var local = Seed(Local("Deleted, but local"));
        local.MarkDeleted();
        var live = Seed(Linked("I_5"));
        var liveStamp = live.UpdatedAt;
        var tombstonedAt = withSource.UpdatedAt;

        var result = await RestoreAsync(StubTaskConnector.Id, Repo);

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.ToString() : null);
        Assert.Equal(2, result.Value);
        Assert.All([withSource, onItsOwn], task => Assert.Null(task.DeletedAt));
        Assert.All([otherTarget, otherConnector, local], task => Assert.NotNull(task.DeletedAt));
        Assert.True(withSource.UpdatedAt > tombstonedAt);
        Assert.False(withSource.SourceRef!.HasFlag(LinkedTaskFlags.DeletedWithSource));
        Assert.Equal(liveStamp, live.UpdatedAt);
        Assert.Equal(2, _tasks.Writes);
        Assert.DoesNotContain(await ListAsync(), source => source.ConnectorId == StubTaskConnector.Id && source.Target == Repo);
    }

    /// <summary>Restoring is offered on a connected target's card. A pair removed
    /// between the card and the confirmation is refused rather than restored: its
    /// tasks would come back as a disconnected source's, which is not what was
    /// confirmed.</summary>
    [Fact]
    public async Task A_target_no_longer_connected_is_refused_and_nothing_is_restored()
    {
        var task = Seed(Linked("I_1", target: OtherRepo));
        task.MarkDeleted();

        var result = await RestoreAsync(StubTaskConnector.Id, OtherRepo);

        Assert.True(result.IsFailure);
        Assert.Equal(RestoreDeletedLinkedTasksCommand.NotConnectedCode, result.Error.Code);
        Assert.Contains(OtherRepo, result.Error.Message, StringComparison.Ordinal);
        Assert.NotNull(task.DeletedAt);
        Assert.Equal(0, _tasks.Writes);
    }

    [Theory]
    [InlineData("", Repo)]
    [InlineData(StubTaskConnector.Id, " ")]
    public async Task A_blank_connector_or_target_is_refused(string connectorId, string target)
    {
        Seed(Linked("I_1")).MarkDeleted();

        var result = await RestoreAsync(connectorId, target);

        Assert.True(result.IsFailure);
        Assert.Equal(0, _tasks.Writes);
    }

    // --- Helpers ----------------------------------------------------------------

    private async Task<IReadOnlyList<DeletedLinkedSource>> ListAsync() =>
        await new ListDeletedLinkedSourcesQueryHandler(_tasks)
            .Handle(new ListDeletedLinkedSourcesQuery(), TestContext.Current.CancellationToken);

    private async Task<Result<int>> RestoreAsync(string connectorId, string target) =>
        await new RestoreDeletedLinkedTasksCommandHandler(_tasks, _targets)
            .Handle(new RestoreDeletedLinkedTasksCommand(connectorId, target), TestContext.Current.CancellationToken);

    private TaskItem Seed(TaskItem task)
    {
        _tasks.Entries[task.Id] = task;
        return task;
    }

    private static TaskItem Local(string title) =>
        new(Guid.NewGuid(), title, string.Empty, EntryType.Task, EntryStatus.Ready, Priority.Medium, null, null, null, Now);

    private static TaskItem Linked(string externalId, string connectorId = StubTaskConnector.Id, string target = Repo)
    {
        var task = new TaskItem(
            LinkedTaskIds.For(connectorId, externalId), $"Item {externalId}", string.Empty, EntryType.Task, EntryStatus.Ready, Priority.Medium, null, null, null, Now);
        task.SetSourceRef(new SourceRef(connectorId, target, externalId, $"https://example.test/{externalId}", $"#{externalId}", null, "open", Now));
        return task;
    }
}
