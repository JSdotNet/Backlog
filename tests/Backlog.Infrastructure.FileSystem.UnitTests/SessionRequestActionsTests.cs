using Backlog.Infrastructure.FileSystem.Tasks;
using Backlog.Infrastructure.Sqlite.Sessions;
using Backlog.Modules.Roadmap.Abstractions.Services;
using Backlog.Modules.Sessions.Abstractions;
using Backlog.Modules.Tasks.Abstractions.Services;

using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Time.Testing;

namespace Backlog.Infrastructure.FileSystem.UnitTests;

/// <summary>
/// The queue icons' adapter over the Sessions context's request queue
/// (local ADR 0038): each port sees its own kind of request, only open ones, a lapsed
/// claim as Requested, and Linked or Cancelled as nothing at all.
/// </summary>
[Collection(SqlitePoolClearingCollection.Name)]
public sealed class SessionRequestActionsTests : IDisposable
{
    private static readonly DateTimeOffset Noon = new(2026, 10, 22, 12, 0, 0, TimeSpan.Zero);

    private readonly TemporaryDirectory _temp = new("backlog-session-request-actions");

    private string _root => _temp.Combine("data");
    private readonly FakeTimeProvider _clock = new(Noon);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        _temp.Dispose();
    }

    private SqliteSessionRequestStore Store() => new(_root, _clock);

    [Fact]
    public async Task An_entry_request_is_queued_with_the_repository_and_clone_folder_and_reads_as_Requested()
    {
        var store = Store();
        IEntrySessionRequests actions = new SessionRequestActions(store, clock: _clock);
        var entryId = Guid.NewGuid();

        var request = await actions.RequestAsync(entryId, "JSdotNet/Backlog", @"D:\Repos\Backlog", Ct);

        Assert.Equal(EntrySessionRequestState.Requested, request.State);
        var held = Assert.Single(await store.ListAsync(cancellationToken: Ct));
        Assert.Equal((SessionRequestKind.Entry, entryId, "JSdotNet/Backlog", @"D:\Repos\Backlog"), (held.Kind, held.EntryId!.Value, held.Repository, held.CloneFolder));
        Assert.Equal(request, (await actions.OpenAsync(Ct))[entryId]);
    }

    [Fact]
    public async Task A_second_press_answers_the_open_request()
    {
        IEntrySessionRequests actions = new SessionRequestActions(Store(), clock: _clock);
        var entryId = Guid.NewGuid();

        var first = await actions.RequestAsync(entryId, "JSdotNet/Backlog", @"D:\Repos\Backlog", Ct);
        var second = await actions.RequestAsync(entryId, "JSdotNet/Backlog", @"D:\Repos\Backlog", Ct);

        Assert.Equal(first.Id, second.Id);
    }

    [Fact]
    public async Task Each_port_sees_only_its_own_kind_of_request()
    {
        var actions = new SessionRequestActions(Store(), clock: _clock);
        var entryId = Guid.NewGuid();

        await ((IEntrySessionRequests)actions).RequestAsync(entryId, "JSdotNet/Backlog", @"D:\Repos\Backlog", Ct);
        await ((IPlanSessionRequests)actions).RequestAsync("+session-dispatch", "JSdotNet/Backlog", @"D:\Repos\Backlog", Ct);

        var entries = await ((IEntrySessionRequests)actions).OpenAsync(Ct);
        var plans = await ((IPlanSessionRequests)actions).OpenAsync(Ct);

        Assert.Equal([entryId], entries.Keys);
        Assert.Equal(["session-dispatch"], plans.Keys);
        Assert.True(plans.ContainsKey("SESSION-DISPATCH"));
    }

    [Fact]
    public async Task Claimed_and_Started_read_as_such_and_a_lapsed_claim_reads_as_Requested()
    {
        var store = Store();
        IEntrySessionRequests actions = new SessionRequestActions(store, clock: _clock);
        var entryId = Guid.NewGuid();
        var request = await actions.RequestAsync(entryId, "JSdotNet/Backlog", @"D:\Repos\Backlog", Ct);

        await store.ClaimAsync(request.Id, "manager", Ct);
        Assert.Equal(EntrySessionRequestState.Claimed, (await actions.OpenAsync(Ct))[entryId].State);

        _clock.Advance(SessionRequests.Lease + TimeSpan.FromSeconds(1));
        Assert.Equal(EntrySessionRequestState.Requested, (await actions.OpenAsync(Ct))[entryId].State);

        await store.ClaimAsync(request.Id, "manager", Ct);
        await store.ReportStartedAsync(request.Id, "manager", "session-1", Ct);
        Assert.Equal(EntrySessionRequestState.Started, (await actions.OpenAsync(Ct))[entryId].State);
    }

    [Fact]
    public async Task A_Linked_or_Cancelled_request_is_no_longer_waiting()
    {
        var store = Store();
        IEntrySessionRequests actions = new SessionRequestActions(store, clock: _clock);
        var linkedEntry = Guid.NewGuid();
        var cancelledEntry = Guid.NewGuid();

        var linked = await actions.RequestAsync(linkedEntry, "JSdotNet/Backlog", @"D:\Repos\Backlog", Ct);
        await store.ClaimAsync(linked.Id, "manager", Ct);
        await store.ReportStartedAsync(linked.Id, "manager", "session-1", Ct);
        await store.LinkAsync(linkedEntry, [], "session-1", Ct);

        var cancelled = await actions.RequestAsync(cancelledEntry, "JSdotNet/Backlog", @"D:\Repos\Backlog", Ct);
        Assert.True(await actions.CancelAsync(cancelled.Id, Ct));

        Assert.Empty(await actions.OpenAsync(Ct));
    }

    [Fact]
    public async Task Cancel_clears_a_started_request_and_is_refused_once_it_is_linked()
    {
        var store = Store();
        IEntrySessionRequests actions = new SessionRequestActions(store, clock: _clock);
        var started = await actions.RequestAsync(Guid.NewGuid(), "JSdotNet/Backlog", @"D:\Repos\Backlog", Ct);
        await store.ClaimAsync(started.Id, "manager", Ct);
        await store.ReportStartedAsync(started.Id, "manager", "session-1", Ct);

        Assert.True(await actions.CancelAsync(started.Id, Ct));
        Assert.Empty(await actions.OpenAsync(Ct));

        var entryId = Guid.NewGuid();
        var linked = await actions.RequestAsync(entryId, "JSdotNet/Backlog", @"D:\Repos\Backlog", Ct);
        await store.ClaimAsync(linked.Id, "manager", Ct);
        await store.ReportStartedAsync(linked.Id, "manager", "session-2", Ct);
        await store.LinkAsync(entryId, [], "session-2", Ct);

        Assert.False(await actions.CancelAsync(linked.Id, Ct));
    }

    [Fact]
    public async Task An_open_entry_request_carries_when_it_was_requested()
    {
        IEntrySessionRequests actions = new SessionRequestActions(Store(), clock: _clock);
        var entryId = Guid.NewGuid();

        var request = await actions.RequestAsync(entryId, "JSdotNet/Backlog", @"D:\Repos\Backlog", Ct);
        _clock.Advance(TimeSpan.FromMinutes(4));

        Assert.Equal(Noon, request.RequestedAt);
        Assert.Equal(Noon, (await actions.OpenAsync(Ct))[entryId].RequestedAt);
    }

    [Fact]
    public async Task An_open_plan_request_carries_when_it_was_requested()
    {
        IPlanSessionRequests actions = new SessionRequestActions(Store(), clock: _clock);

        var request = await actions.RequestAsync("+session-dispatch", "JSdotNet/Backlog", @"D:\Repos\Backlog", Ct);
        _clock.Advance(TimeSpan.FromMinutes(4));

        Assert.Equal(Noon, request.RequestedAt);
        Assert.Equal(Noon, (await actions.OpenAsync(Ct))["session-dispatch"].RequestedAt);
    }

    [Fact]
    public async Task Open_requests_carry_their_place_in_one_queue_across_entries_and_plans()
    {
        var store = Store();
        var actions = new SessionRequestActions(store, clock: _clock);
        var firstEntry = Guid.NewGuid();
        var secondEntry = Guid.NewGuid();

        await ((IEntrySessionRequests)actions).RequestAsync(firstEntry, "JSdotNet/Backlog", @"D:\Repos\Backlog", Ct);
        _clock.Advance(TimeSpan.FromSeconds(1));
        await ((IPlanSessionRequests)actions).RequestAsync("+session-dispatch", "JSdotNet/Backlog", @"D:\Repos\Backlog", Ct);
        _clock.Advance(TimeSpan.FromSeconds(1));
        var claimed = await ((IEntrySessionRequests)actions).RequestAsync(secondEntry, "JSdotNet/Backlog", @"D:\Repos\Backlog", Ct);
        await store.ClaimAsync(claimed.Id, "manager", Ct);

        var entries = await ((IEntrySessionRequests)actions).OpenAsync(Ct);
        var plans = await ((IPlanSessionRequests)actions).OpenAsync(Ct);

        Assert.Equal(0, entries[firstEntry].QueuePosition);
        Assert.Equal(1, plans["session-dispatch"].QueuePosition);
        Assert.Null(entries[secondEntry].QueuePosition);
    }

    [Fact]
    public async Task Moving_a_plan_request_above_an_entry_request_reorders_the_one_queue()
    {
        var store = Store();
        var actions = new SessionRequestActions(store, clock: _clock);
        var entryId = Guid.NewGuid();

        var entry = await ((IEntrySessionRequests)actions).RequestAsync(entryId, "JSdotNet/Backlog", @"D:\Repos\Backlog", Ct);
        _clock.Advance(TimeSpan.FromSeconds(1));
        var plan = await ((IPlanSessionRequests)actions).RequestAsync("+session-dispatch", "JSdotNet/Backlog", @"D:\Repos\Backlog", Ct);

        Assert.True(await ((ISessionQueueOrder)actions).MoveAsync(plan.Id, 0, Ct));

        Assert.Equal(0, (await ((IPlanSessionRequests)actions).OpenAsync(Ct))["session-dispatch"].QueuePosition);
        Assert.Equal(1, (await ((IEntrySessionRequests)actions).OpenAsync(Ct))[entryId].QueuePosition);
        Assert.Equal(plan.Id, (await store.NextAsync(Ct))!.Id);
        Assert.NotEqual(entry.Id, (await store.NextAsync(Ct))!.Id);
    }

    [Fact]
    public async Task A_move_the_queue_refuses_answers_false()
    {
        var store = Store();
        var actions = new SessionRequestActions(store, clock: _clock);
        var request = await ((IEntrySessionRequests)actions).RequestAsync(Guid.NewGuid(), "JSdotNet/Backlog", @"D:\Repos\Backlog", Ct);
        await store.ClaimAsync(request.Id, "manager", Ct);

        Assert.False(await ((ISessionQueueOrder)actions).MoveAsync(request.Id, 0, Ct));
        Assert.False(await ((ISessionQueueOrder)SessionRequestActions.NotOffered).MoveAsync(request.Id, 0, Ct));
    }

    [Fact]
    public void The_entry_port_reads_dispatcher_contact_from_the_record_the_wait_route_keeps()
    {
        var contact = new DispatcherContact(_clock);
        IEntrySessionRequests actions = new SessionRequestActions(Store(), clock: _clock, contact: contact);

        _clock.Advance(TimeSpan.FromSeconds(20));
        contact.Heard();
        _clock.Advance(DispatcherContact.Threshold + TimeSpan.FromSeconds(1));

        Assert.Equal(new DispatcherContactReading(Noon.AddSeconds(20), IsSilent: true), actions.DispatcherContact);
    }

    [Fact]
    public void A_dispatcher_heard_recently_is_not_silent()
    {
        var contact = new DispatcherContact(_clock);
        IEntrySessionRequests actions = new SessionRequestActions(Store(), clock: _clock, contact: contact);
        _clock.Advance(TimeSpan.FromMinutes(10));

        contact.Heard();

        Assert.Equal(new DispatcherContactReading(Noon.AddMinutes(10), IsSilent: false), actions.DispatcherContact);
    }

    [Fact]
    public void A_host_without_a_queue_hears_no_dispatcher_and_warns_of_none()
    {
        Assert.Equal(new DispatcherContactReading(null, IsSilent: false), ((IEntrySessionRequests)SessionRequestActions.NotOffered).DispatcherContact);
    }

    [Fact]
    public void A_queue_with_no_contact_record_hears_no_dispatcher_and_warns_of_none()
    {
        IEntrySessionRequests actions = new SessionRequestActions(Store(), clock: _clock);
        _clock.Advance(TimeSpan.FromHours(1));

        Assert.Equal(new DispatcherContactReading(null, IsSilent: false), actions.DispatcherContact);
    }

    // --- pausing the queue -----------------------------------------------------------

    [Fact]
    public async Task Pausing_through_the_port_pauses_the_queue_and_resuming_runs_it_again()
    {
        var store = Store();
        IEntrySessionRequests actions = new SessionRequestActions(store, clock: _clock);

        Assert.False(await actions.IsPausedAsync(Ct));

        await actions.SetPausedAsync(true, Ct);

        Assert.True(await actions.IsPausedAsync(Ct));
        Assert.True(await store.IsPausedAsync(Ct));

        await actions.SetPausedAsync(false, Ct);

        Assert.False(await store.IsPausedAsync(Ct));
    }

    [Fact]
    public async Task A_request_queued_while_paused_reads_as_Requested()
    {
        IEntrySessionRequests actions = new SessionRequestActions(Store(), clock: _clock);
        await actions.SetPausedAsync(true, Ct);

        var request = await actions.RequestAsync(Guid.NewGuid(), "JSdotNet/Backlog", @"D:\Repos\Backlog", Ct);

        Assert.Equal(EntrySessionRequestState.Requested, request.State);
        Assert.True(await actions.CancelAsync(request.Id, Ct));
    }

    [Fact]
    public async Task A_host_without_a_queue_reads_as_running_and_refuses_a_pause()
    {
        IEntrySessionRequests actions = SessionRequestActions.NotOffered;

        Assert.False(actions.IsOffered);
        Assert.False(await actions.IsPausedAsync(Ct));
        await Assert.ThrowsAsync<InvalidOperationException>(() => actions.SetPausedAsync(true, Ct));
    }
}
