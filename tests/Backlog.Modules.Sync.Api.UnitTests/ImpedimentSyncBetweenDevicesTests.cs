using System.Net.Http.Json;

using Backlog.Modules.Progress.Abstractions;
using Backlog.Modules.Progress.Abstractions.DataTransferObjects;
using Backlog.Modules.Sync.Abstractions;
using Backlog.Modules.Sync.Abstractions.DataTransferObjects;

namespace Backlog.Modules.Sync.Api.UnitTests;

/// <summary>
/// Two paired desktops sharing their impediments through this service, end to end — the
/// twin of <see cref="GoalSyncBetweenDevicesTests"/>: each the real Progress module over
/// real <c>progress_impediments</c> rows in its own <c>backlog.db</c>, the real merge and
/// session, and the real sync API between them
/// (<c>.devbook/domain/progress/requirements.md#syncing-goals-and-impediments</c>).
/// </summary>
public sealed class ImpedimentSyncBetweenDevicesTests : IDisposable
{
    private readonly PairedDesktops _desktops = new();

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    public void Dispose()
    {
        _desktops.Dispose();
        GC.SuppressFinalize(this);
    }

    /// <summary>Scenario "An impediment reaches the other desktop": raised on A with the
    /// task it holds back and a note, it is on B after B's next pull, the same document at
    /// the same stamp, and B's screens hear it without a Refresh.</summary>
    [Fact]
    public async Task An_impediment_raised_on_one_desktop_appears_on_the_other()
    {
        var (a, b) = await _desktops.PairAsync();
        var task = Guid.NewGuid();
        var raised = await a.Impediments(impediments => impediments.RaiseAsync(
            new RaiseImpedimentRequest("Bank sample files missing", [task], "Asked the bank on Friday"), Cancellation));

        Assert.True((await a.SyncAsync()).IsSuccess);
        var heard = 0;
        b.ImpedimentsChanged += () => heard++;

        var pulled = await b.SyncAsync();

        Assert.Equal(1, pulled.Value.Applied);
        var there = await b.Impediments(impediments => impediments.GetAsync(raised.Id, Cancellation));
        Assert.Equal("Bank sample files missing", there.Title);
        Assert.Equal(ImpedimentState.New, there.State);
        Assert.Equal("Asked the bank on Friday", there.Note);
        Assert.Equal([task], there.HoldsBack);
        Assert.Equal(raised.History, there.History);
        Assert.Equal(await a.ImpedimentCopyAsync(raised.Id), await b.ImpedimentCopyAsync(raised.Id));
        Assert.True(heard > 0);
    }

    /// <summary>Both move one impediment before either syncs — A reports it, B resolves it
    /// later; once both have synced both hold B's move, and A's report is lost.</summary>
    [Fact]
    public async Task The_newer_move_wins_per_impediment_on_both_desktops()
    {
        var (a, b) = await _desktops.PairAsync();
        var raised = await a.Impediments(impediments => impediments.RaiseAsync(
            new RaiseImpedimentRequest("Bank sample files missing"), Cancellation));
        Assert.True((await a.SyncAsync()).IsSuccess);
        Assert.True((await b.SyncAsync()).IsSuccess);

        await a.Impediments(impediments => impediments.MoveAsync(raised.Id, ImpedimentState.Reported, "Finance", Cancellation));
        await b.Impediments(impediments => impediments.MoveAsync(raised.Id, ImpedimentState.Resolved, cancellationToken: Cancellation));
        var later = await b.ImpedimentCopyAsync(raised.Id);
        Assert.True(later!.UpdatedAt > (await a.ImpedimentCopyAsync(raised.Id))!.UpdatedAt);

        for (var round = 0; round < 2; round++)
        {
            Assert.True((await a.SyncAsync()).IsSuccess);
            Assert.True((await b.SyncAsync()).IsSuccess);
        }

        Assert.Equal(later, await a.ImpedimentCopyAsync(raised.Id));
        Assert.Equal(later, await b.ImpedimentCopyAsync(raised.Id));
        var onA = await a.Impediments(impediments => impediments.GetAsync(raised.Id, Cancellation));
        Assert.Equal(ImpedimentState.Resolved, onA.State);
        Assert.Null(onA.ReportedTo);
        Assert.Empty(await a.Impediments(impediments => impediments.ListAsync(cancellationToken: Cancellation)));
    }

    /// <summary>A <c>sprint-series</c> document an older build left on the replica (local
    /// ADR 0035 retires sprints) reaches a desktop that keeps impediments and is dropped:
    /// counted as nothing to do, never written as a task, and the impediment pulled beside
    /// it is taken as ever.</summary>
    [Fact]
    public async Task A_sprint_series_on_the_replica_is_still_dropped_beside_an_impediment()
    {
        var (a, b) = await _desktops.PairAsync();
        var raised = await a.Impediments(impediments => impediments.RaiseAsync(
            new RaiseImpedimentRequest("Bank sample files missing"), Cancellation));
        Assert.True((await a.SyncAsync()).IsSuccess);

        var series = SprintSeries();
        var pushed = await a.Http.PostAsJsonAsync(
            SyncRoutes.Absolute(SyncRoutes.Tasks), new PushTasksRequest([series]), Cancellation);
        pushed.EnsureSuccessStatusCode();
        Assert.Equal(1, (await pushed.Content.ReadFromJsonAsync<PushTasksResponse>(Cancellation))!.Accepted);

        var pulled = await b.SyncAsync();

        Assert.True(pulled.IsSuccess);
        Assert.Equal(2, pulled.Value.Pulled);
        Assert.Equal(1, pulled.Value.Applied);
        Assert.Equal(0, pulled.Value.Skipped);
        Assert.False(await b.HoldsTaskAsync(series.Id));
        Assert.Equal(raised.Id, Assert.Single(await b.Impediments(impediments => impediments.ListAsync(cancellationToken: Cancellation))).Id);
    }

    /// <summary>The sprint series an older desktop pushed: a whole document on the task
    /// feed under the retired <c>sprint-series</c> kind token.</summary>
    private static TaskChange SprintSeries()
    {
        var at = PairedDesktops.Morning;
        return new TaskChange(Guid.NewGuid(), at, DeletedAt: null, new TaskPayload(
            "Platform",
            """{"version":1,"name":"Platform","sprints":[]}""",
            "sprint-series",
            Status: "draft",
            Priority: "medium",
            Order: 0,
            Area: null,
            CreatedAt: at,
            SourceInboxId: null,
            RecurrenceSourceId: null,
            DueOn: null,
            RemindAt: null,
            Recurrence: null,
            InMyDayOn: null,
            View: null,
            Effort: null,
            ImportPlanId: null,
            ImportItemId: null,
            AttachmentPath: null,
            Tags: [],
            RepoIds: [],
            DependsOn: [],
            SubItems: [],
            UsageEvents: [],
            ProjectionRefs: []));
    }
}
