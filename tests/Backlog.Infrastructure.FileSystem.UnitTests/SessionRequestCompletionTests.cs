using Backlog.Infrastructure.FileSystem.Tasks;
using Backlog.Infrastructure.Sqlite.Sessions;
using Backlog.Modules.Sessions.Abstractions;
using Backlog.Modules.Tasks.Abstractions;
using Backlog.Modules.Tasks.Abstractions.DataTransferObjects;
using Backlog.Modules.Tasks.Abstractions.Services;

using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Time.Testing;

namespace Backlog.Infrastructure.FileSystem.UnitTests;

/// <summary>
/// A session request whose work the person finished in the app leaves the queue
/// (local ADR 0038 §3): a plan request once every entry of the plan is Done or
/// Archived, an entry request once its entry is. A Claimed one is left to its
/// dispatcher, and a plan with no entries is not finished.
/// </summary>
[Collection(SqlitePoolClearingCollection.Name)]
public sealed class SessionRequestCompletionTests : IDisposable
{
    private static readonly DateTimeOffset Noon = new(2026, 11, 18, 12, 0, 0, TimeSpan.Zero);

    private readonly TemporaryDirectory _temp = new("backlog-session-request-completion");

    private string _root => _temp.Combine("data");
    private readonly FakeTimeProvider _clock = new(Noon);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        _temp.Dispose();
    }

    private SqliteSessionRequestStore Store() => new(_root, _clock);

    private static TaskItemDto Entry(EntryStatus status, string? plan = null, IReadOnlyList<string>? tags = null, Guid? id = null) =>
        new(id ?? Guid.NewGuid(), "An entry", string.Empty, EntryType.Prompt, Priority.Medium, status, null, tags ?? [], 0, 0, 0, [], ImportPlanId: plan);

    private static Func<CancellationToken, Task<IReadOnlyList<TaskItemDto>>> Listing(params TaskItemDto[] entries) =>
        _ => Task.FromResult<IReadOnlyList<TaskItemDto>>(entries);

    private async Task<SessionRequest> StartedPlanAsync(SqliteSessionRequestStore store, string tag)
    {
        var request = await store.RequestAsync(SessionRequests.ForPlan(tag, "JSdotNet/Backlog", @"D:\Repos\Backlog", Noon), Ct);
        await store.ClaimAsync(request.Id, "manager", Ct);
        await store.ReportStartedAsync(request.Id, "manager", "local_plan-session", Ct);

        return request;
    }

    private async Task<SessionRequestState?> StateOfAsync(SqliteSessionRequestStore store, Guid id) =>
        (await store.GetAsync(id, Ct))?.StateAt(_clock.GetUtcNow());

    [Fact]
    public async Task A_started_plan_request_is_closed_once_every_entry_of_the_plan_is_done()
    {
        var store = Store();
        var request = await StartedPlanAsync(store, "gtd-para");
        var entries = Listing(
            Entry(EntryStatus.Done, plan: "+gtd-para"),
            Entry(EntryStatus.Archived, plan: "+gtd-para"),
            Entry(EntryStatus.Done, tags: ["+gtd-para"]),
            Entry(EntryStatus.InProgress, plan: "+other-plan"));

        var closed = await new SessionRequestCompletion(store, entries).SweepAsync(Ct);

        Assert.Equal(1, closed);
        Assert.Equal(SessionRequestState.Cancelled, await StateOfAsync(store, request.Id));
    }

    [Fact]
    public async Task A_plan_request_stays_while_one_entry_of_the_plan_is_open()
    {
        var store = Store();
        var request = await StartedPlanAsync(store, "gtd-para");
        var entries = Listing(
            Entry(EntryStatus.Done, plan: "+gtd-para"),
            Entry(EntryStatus.Ready, tags: ["+gtd-para"]));

        var closed = await new SessionRequestCompletion(store, entries).SweepAsync(Ct);

        Assert.Equal(0, closed);
        Assert.Equal(SessionRequestState.Started, await StateOfAsync(store, request.Id));
    }

    [Fact]
    public async Task A_plan_with_no_entries_is_not_finished()
    {
        var store = Store();
        var request = await StartedPlanAsync(store, "gtd-para");

        await new SessionRequestCompletion(store, Listing()).SweepAsync(Ct);

        Assert.Equal(SessionRequestState.Started, await StateOfAsync(store, request.Id));
    }

    [Fact]
    public async Task A_requested_plan_request_is_closed_and_a_claimed_one_is_left_to_its_dispatcher()
    {
        var store = Store();
        var waiting = await store.RequestAsync(SessionRequests.ForPlan("waiting-plan", "JSdotNet/Backlog", @"D:\Repos\Backlog", Noon), Ct);
        var claimed = await store.RequestAsync(SessionRequests.ForPlan("claimed-plan", "JSdotNet/Backlog", @"D:\Repos\Backlog", Noon), Ct);
        await store.ClaimAsync(claimed.Id, "manager", Ct);
        var entries = Listing(
            Entry(EntryStatus.Done, plan: "+waiting-plan"),
            Entry(EntryStatus.Done, plan: "+claimed-plan"));

        await new SessionRequestCompletion(store, entries).SweepAsync(Ct);

        Assert.Equal(SessionRequestState.Cancelled, await StateOfAsync(store, waiting.Id));
        Assert.Equal(SessionRequestState.Claimed, await StateOfAsync(store, claimed.Id));
    }

    [Fact]
    public async Task An_entry_request_is_closed_once_its_entry_is_done()
    {
        var store = Store();
        var doneId = Guid.NewGuid();
        var openId = Guid.NewGuid();
        var done = await store.RequestAsync(SessionRequests.ForEntry(doneId, "JSdotNet/Backlog", @"D:\Repos\Backlog", Noon), Ct);
        var open = await store.RequestAsync(SessionRequests.ForEntry(openId, "JSdotNet/Backlog", @"D:\Repos\Backlog", Noon), Ct);
        var entries = Listing(Entry(EntryStatus.Done, id: doneId), Entry(EntryStatus.InProgress, id: openId));

        await new SessionRequestCompletion(store, entries).SweepAsync(Ct);

        Assert.Equal(SessionRequestState.Cancelled, await StateOfAsync(store, done.Id));
        Assert.Equal(SessionRequestState.Requested, await StateOfAsync(store, open.Id));
    }

    [Fact]
    public async Task A_task_write_sweeps_the_queue()
    {
        var store = Store();
        var request = await StartedPlanAsync(store, "gtd-para");
        var signal = new Signal();
        using var completion = new SessionRequestCompletion(store, Listing(Entry(EntryStatus.Done, plan: "+gtd-para")), signal);

        signal.Raise();
        await completion.Idle;

        Assert.Equal(SessionRequestState.Cancelled, await StateOfAsync(store, request.Id));
    }

    private sealed class Signal : ITaskChangeSignal
    {
        public event Action? Changed;

        public void Raise() => Changed?.Invoke();

        public IDisposable Suppress() => throw new NotSupportedException();
    }
}
