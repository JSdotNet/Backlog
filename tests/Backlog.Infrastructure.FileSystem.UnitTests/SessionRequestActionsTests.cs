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

    private readonly string _root = Path.Combine(Path.GetTempPath(), "backlog-session-request-actions", Guid.NewGuid().ToString("n"));
    private readonly FakeTimeProvider _clock = new(Noon);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { }
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
    public async Task Cancel_is_refused_once_the_session_has_started()
    {
        var store = Store();
        IEntrySessionRequests actions = new SessionRequestActions(store, clock: _clock);
        var request = await actions.RequestAsync(Guid.NewGuid(), "JSdotNet/Backlog", @"D:\Repos\Backlog", Ct);
        await store.ClaimAsync(request.Id, "manager", Ct);
        await store.ReportStartedAsync(request.Id, "manager", "session-1", Ct);

        Assert.False(await actions.CancelAsync(request.Id, Ct));
    }
}
