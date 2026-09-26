using Backlog.Mobile.UI.Outbox;
using Backlog.Modules.Sync.Abstractions.DataTransferObjects;

using Microsoft.Extensions.Time.Testing;

namespace Backlog.Mobile.UI.UnitTests;

/// <summary>
/// The outbox on its own, against a clock the test moves: what order entries
/// leave in, when a failed one is tried again, when it stops trying, and that a
/// retry is the same capture rather than a second one.
/// </summary>
public sealed class DeviceOutboxTests
{
    private static readonly DateTimeOffset Start = new(2026, 9, 25, 9, 30, 0, TimeSpan.Zero);

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    /// <summary>Three captures made with no network leave in the order they were
    /// made once it comes back — the first one's backoff holds the other two
    /// behind it rather than letting them overtake.</summary>
    [Fact]
    public async Task Entries_made_offline_leave_oldest_first_once_the_network_is_back()
    {
        var clock = new FakeTimeProvider(Start);
        var kind = new RecordingKind(clock) { Online = false };
        using var outbox = new DeviceOutbox(new InMemoryDeviceStore(), [kind], clock);

        var first = await EnqueueAsync(outbox);
        var second = await EnqueueAsync(outbox);
        var third = await EnqueueAsync(outbox);

        // Only the head was tried: the other two wait behind its backoff.
        Assert.Equal([first], kind.Attempts.Select(attempt => attempt.Id));

        kind.Online = true;
        clock.Advance(DeviceOutbox.FirstRetryDelay);
        await outbox.WhenIdleAsync();

        Assert.Equal([first, first, second, third], kind.Attempts.Select(attempt => attempt.Id));
        Assert.Equal([first, second, third], kind.Delivered);
        Assert.Empty(outbox.Entries);
        Assert.False(outbox.Unreachable);
    }

    /// <summary>2s, 4s, 8s, 16s between the five attempts, then parked with the
    /// last error — and nothing more, however long the phone is left.</summary>
    [Fact]
    public async Task A_failing_entry_backs_off_exponentially_and_parks_after_five_attempts()
    {
        var clock = new FakeTimeProvider(Start);
        var kind = new RecordingKind(clock) { Online = false };
        using var outbox = new DeviceOutbox(new InMemoryDeviceStore(), [kind], clock);

        await EnqueueAsync(outbox);

        foreach (var wait in new[] { 2, 4, 8, 16 })
        {
            // A moment short of the wait: nothing yet.
            clock.Advance(TimeSpan.FromSeconds(wait) - TimeSpan.FromMilliseconds(1));
            await outbox.WhenIdleAsync();
            var before = kind.Attempts.Count;

            clock.Advance(TimeSpan.FromMilliseconds(1));
            await outbox.WhenIdleAsync();
            Assert.Equal(before + 1, kind.Attempts.Count);
        }

        Assert.Equal(
            [0, 2, 6, 14, 30],
            kind.Attempts.Select(attempt => (int)(attempt.At - Start).TotalSeconds));

        var parked = Assert.Single(outbox.Entries);
        Assert.True(parked.IsParked);
        Assert.Equal(DeviceOutbox.MaxAttempts, parked.Attempts);
        Assert.Equal("No network.", parked.LastError);

        clock.Advance(TimeSpan.FromHours(1));
        await outbox.WhenIdleAsync();
        Assert.Equal(DeviceOutbox.MaxAttempts, kind.Attempts.Count);
    }

    /// <summary>An entry the network parked is still first in line: what was
    /// captured after it waits rather than overtaking it, and a resume sends
    /// both, in order.</summary>
    [Fact]
    public async Task A_parked_entry_holds_the_ones_behind_it_until_a_resume_sends_them_in_order()
    {
        var clock = new FakeTimeProvider(Start);
        var kind = new RecordingKind(clock) { Online = false };
        using var outbox = new DeviceOutbox(new InMemoryDeviceStore(), [kind], clock);

        var first = await EnqueueAsync(outbox);
        for (var i = 0; i < DeviceOutbox.MaxAttempts; i++)
        {
            clock.Advance(TimeSpan.FromMinutes(1));
            await outbox.WhenIdleAsync();
        }

        kind.Online = true;
        var second = await EnqueueAsync(outbox);

        Assert.Empty(kind.Delivered);
        Assert.Equal([first, second], outbox.Entries.Select(entry => entry.Id));

        await outbox.ResumeAsync(Cancellation);

        Assert.Equal([first, second], kind.Delivered);
        Assert.Empty(outbox.Entries);
    }

    /// <summary>"Waiting — tap to retry": a tap starts a parked entry again from
    /// nothing, and it goes.</summary>
    [Fact]
    public async Task A_tap_retries_a_parked_entry()
    {
        var clock = new FakeTimeProvider(Start);
        var kind = new RecordingKind(clock) { Online = false };
        using var outbox = new DeviceOutbox(new InMemoryDeviceStore(), [kind], clock);

        var id = await EnqueueAsync(outbox);
        for (var i = 0; i < DeviceOutbox.MaxAttempts; i++)
        {
            clock.Advance(TimeSpan.FromMinutes(1));
            await outbox.WhenIdleAsync();
        }

        Assert.True(Assert.Single(outbox.Entries).IsParked);

        kind.Online = true;
        await outbox.RetryAsync(id, Cancellation);

        Assert.Empty(outbox.Entries);
        Assert.Equal([id], kind.Delivered);
    }

    /// <summary>A refusal is not a network problem: the entry parks at once with
    /// the service's reason, and the entries behind it still go.</summary>
    [Fact]
    public async Task A_refused_entry_parks_at_once_without_holding_up_the_rest()
    {
        var clock = new FakeTimeProvider(Start);
        var kind = new RecordingKind(clock);
        using var outbox = new DeviceOutbox(new InMemoryDeviceStore(), [kind], clock);

        var refused = Guid.CreateVersion7();
        kind.Refuse.Add(refused);

        await outbox.EnqueueAsync(RecordingKind.Token, refused, "{}", Cancellation);
        await outbox.WhenIdleAsync();
        var next = await EnqueueAsync(outbox);

        var parked = Assert.Single(outbox.Entries);
        Assert.Equal(refused, parked.Id);
        Assert.True(parked.Refused);
        Assert.Equal("A capture needs a title.", parked.LastError);
        Assert.Equal([next], kind.Delivered);
    }

    /// <summary>Coming back to the app does not wait out the backoff: the reason
    /// for it has most likely gone.</summary>
    [Fact]
    public async Task Resume_tries_now_rather_than_when_the_backoff_says()
    {
        var clock = new FakeTimeProvider(Start);
        var kind = new RecordingKind(clock) { Online = false };
        using var outbox = new DeviceOutbox(new InMemoryDeviceStore(), [kind], clock);

        var id = await EnqueueAsync(outbox);
        kind.Online = true;

        await outbox.ResumeAsync(Cancellation);

        Assert.Equal([id], kind.Delivered);
        Assert.Equal(Start, kind.Attempts[^1].At);
    }

    /// <summary>The capture kind over a network that fails once and then answers:
    /// both posts carry the id the entry was queued with, so the service can
    /// recognise the second as the first.</summary>
    [Fact]
    public async Task A_retried_capture_sends_the_same_id()
    {
        var clock = new FakeTimeProvider(Start);
        var service = new ScriptedInboxService { State = InboxServiceState.Unreachable };
        var sync = new CloudSyncClient(new HttpClient(new ServiceHandler(service)) { BaseAddress = new Uri("https://sync.test") });
        using var outbox = new DeviceOutbox(new InMemoryDeviceStore(), [new CaptureOutboxKind(sync)], clock);

        var capture = CaptureOutboxKind.Create("Ask about the offsite");
        await outbox.EnqueueAsync(CaptureOutboxKind.Token, capture.Id!.Value, CaptureOutboxKind.Write(capture), Cancellation);
        await outbox.WhenIdleAsync();

        service.State = InboxServiceState.Answering;
        clock.Advance(DeviceOutbox.FirstRetryDelay);
        await outbox.WhenIdleAsync();

        Assert.Equal(2, service.Received.Count);
        Assert.All(service.Received, sent => Assert.Equal(capture.Id, sent.Id));
        Assert.Equal(1, service.Created);
        Assert.Empty(outbox.Entries);
    }

    /// <summary>A restarted service rejects the token it signed before with a 401.
    /// That is the token's problem, not the capture's: it is retried like a
    /// network failure — never set aside, which would let a later capture overtake
    /// it — and goes once the service answers.</summary>
    [Fact]
    public async Task A_401_is_retried_rather_than_set_aside()
    {
        var clock = new FakeTimeProvider(Start);
        var service = new ScriptedInboxService { State = InboxServiceState.Unauthorized };
        var sync = new CloudSyncClient(new HttpClient(new ServiceHandler(service)) { BaseAddress = new Uri("https://sync.test") });
        using var outbox = new DeviceOutbox(new InMemoryDeviceStore(), [new CaptureOutboxKind(sync)], clock);

        var capture = CaptureOutboxKind.Create("Ask about the offsite");
        await outbox.EnqueueAsync(CaptureOutboxKind.Token, capture.Id!.Value, CaptureOutboxKind.Write(capture), Cancellation);
        await outbox.WhenIdleAsync();

        var waiting = Assert.Single(outbox.Entries);
        Assert.False(waiting.Refused);
        Assert.Equal(1, waiting.Attempts);

        service.State = InboxServiceState.Answering;
        clock.Advance(DeviceOutbox.FirstRetryDelay);
        await outbox.WhenIdleAsync();

        Assert.Empty(outbox.Entries);
        Assert.Equal(1, service.Created);
    }

    /// <summary>A queue survives the app being killed: a new outbox over the same
    /// store picks up where the old one stopped.</summary>
    [Fact]
    public async Task A_new_outbox_over_the_same_store_resumes_what_was_queued()
    {
        var clock = new FakeTimeProvider(Start);
        var store = new InMemoryDeviceStore();
        var kind = new RecordingKind(clock) { Online = false };

        Guid id;
        using (var before = new DeviceOutbox(store, [kind], clock))
        {
            id = await EnqueueAsync(before);
        }

        kind.Online = true;
        using var after = new DeviceOutbox(store, [kind], clock);
        Assert.Equal(id, Assert.Single(after.Entries).Id);

        await after.ResumeAsync(Cancellation);
        Assert.Empty(after.Entries);
        Assert.Contains(id, kind.Delivered);
    }

    private static async Task<Guid> EnqueueAsync(DeviceOutbox outbox)
    {
        var id = Guid.CreateVersion7();
        await outbox.EnqueueAsync(RecordingKind.Token, id, "{}", Cancellation);
        await outbox.WhenIdleAsync();
        return id;
    }

    private sealed class RecordingKind(TimeProvider clock) : IOutboxKind
    {
        public const string Token = "recording";

        public bool Online { get; set; } = true;

        public HashSet<Guid> Refuse { get; } = [];

        public List<(Guid Id, DateTimeOffset At)> Attempts { get; } = [];

        public List<Guid> Delivered { get; } = [];

        public string Kind => Token;

        public Task<OutboxDelivery> SendAsync(OutboxEntry entry, CancellationToken cancellationToken)
        {
            Attempts.Add((entry.Id, clock.GetUtcNow()));

            if (Refuse.Contains(entry.Id)) return Task.FromResult(OutboxDelivery.Refused("A capture needs a title."));
            if (!Online) return Task.FromResult(OutboxDelivery.Transient("No network."));

            Delivered.Add(entry.Id);
            return Task.FromResult(OutboxDelivery.Delivered);
        }
    }

    private sealed class ServiceHandler(ScriptedInboxService service) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            service.AnswerAsync(request, cancellationToken);
    }
}
