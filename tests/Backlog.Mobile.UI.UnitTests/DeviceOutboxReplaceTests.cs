using Backlog.Mobile.UI.Outbox;

using Microsoft.Extensions.Time.Testing;

namespace Backlog.Mobile.UI.UnitTests;

/// <summary>
/// Changing an entry still waiting in the outbox: its payload rewritten in place,
/// or the entry taken out, so what leaves the phone is what the person last
/// said. Never the entry on its way out — that one has already been said, and the
/// caller queues the change behind it instead.
/// </summary>
public sealed class DeviceOutboxReplaceTests
{
    private static readonly DateTimeOffset Start = new(2026, 11, 18, 9, 0, 0, TimeSpan.Zero);

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_waiting_entrys_payload_is_rewritten_kept_in_its_place_and_sent_as_rewritten()
    {
        var store = new InMemoryDeviceStore();
        var kind = new GatedKind();
        using var outbox = new DeviceOutbox(store, [kind], new FakeTimeProvider(Start));
        var blocker = await EnqueueAsync(outbox, """{"n":0}""");
        var waiting = await EnqueueAsync(outbox, """{"n":1}""");
        var changes = 0;
        outbox.Changed += () => changes++;

        var replaced = await outbox.ReplacePayloadAsync(waiting, """{"n":2}""", Cancellation);

        Assert.True(replaced);
        Assert.True(changes > 0);
        Assert.Equal([blocker, waiting], outbox.Entries.Select(entry => entry.Id));
        Assert.Equal("""{"n":2}""", outbox.Entries[1].PayloadJson);
        Assert.Equal("""{"n":2}""", Assert.Single(store.ReadOutbox(), entry => entry.Id == waiting).PayloadJson);

        kind.Release();
        await outbox.WhenIdleAsync();
        Assert.Equal(["""{"n":0}""", """{"n":2}"""], kind.Sent);
    }

    [Fact]
    public async Task A_waiting_entry_is_removed_and_never_sent()
    {
        var store = new InMemoryDeviceStore();
        var kind = new GatedKind();
        using var outbox = new DeviceOutbox(store, [kind], new FakeTimeProvider(Start));
        await EnqueueAsync(outbox, """{"n":0}""");
        var waiting = await EnqueueAsync(outbox, """{"n":1}""");
        var changes = 0;
        outbox.Changed += () => changes++;

        var removed = await outbox.RemoveAsync(waiting, Cancellation);

        Assert.True(removed);
        Assert.True(changes > 0);
        Assert.DoesNotContain(outbox.Entries, entry => entry.Id == waiting);
        Assert.DoesNotContain(store.ReadOutbox(), entry => entry.Id == waiting);

        kind.Release();
        await outbox.WhenIdleAsync();
        Assert.Equal(["""{"n":0}"""], kind.Sent);
    }

    /// <summary>The entry being sent is already said: it is neither rewritten
    /// nor taken out, and the caller hears so.</summary>
    [Fact]
    public async Task The_entry_being_sent_is_neither_rewritten_nor_removed()
    {
        var kind = new GatedKind();
        using var outbox = new DeviceOutbox(new InMemoryDeviceStore(), [kind], new FakeTimeProvider(Start));
        var sending = await EnqueueAsync(outbox, """{"n":0}""");
        Assert.Equal(sending, outbox.Sending);

        Assert.False(await outbox.ReplacePayloadAsync(sending, """{"n":9}""", Cancellation));
        Assert.False(await outbox.RemoveAsync(sending, Cancellation));
        Assert.Equal("""{"n":0}""", Assert.Single(outbox.Entries).PayloadJson);

        kind.Release();
        await outbox.WhenIdleAsync();
        Assert.Equal(["""{"n":0}"""], kind.Sent);
    }

    [Fact]
    public async Task An_entry_that_is_not_there_is_neither_rewritten_nor_removed()
    {
        using var outbox = new DeviceOutbox(new InMemoryDeviceStore(), [new GatedKind()], new FakeTimeProvider(Start));

        Assert.False(await outbox.ReplacePayloadAsync(Guid.CreateVersion7(), "{}", Cancellation));
        Assert.False(await outbox.RemoveAsync(Guid.CreateVersion7(), Cancellation));
    }

    /// <summary>A set-aside entry is still the person's to change: rewriting it
    /// does not by itself send it, and removing it clears it from the list.</summary>
    [Fact]
    public async Task A_refused_entry_can_be_rewritten_or_removed()
    {
        var kind = new GatedKind { RefuseAll = true };
        using var outbox = new DeviceOutbox(new InMemoryDeviceStore(), [kind], new FakeTimeProvider(Start));
        var refused = await EnqueueAsync(outbox, """{"n":0}""");
        kind.Release();
        await outbox.WhenIdleAsync();
        Assert.True(Assert.Single(outbox.Entries).Refused);

        Assert.True(await outbox.ReplacePayloadAsync(refused, """{"n":1}""", Cancellation));
        Assert.Equal("""{"n":1}""", Assert.Single(outbox.Entries).PayloadJson);
        Assert.True(await outbox.RemoveAsync(refused, Cancellation));
        Assert.Empty(outbox.Entries);
    }

    /// <summary>The service has taken the entry and the outbox is still writing
    /// that down: the entry is already said, so it can be neither rewritten nor
    /// taken out — the caller queues its change behind it instead of changing words
    /// the service already has.</summary>
    [Fact]
    public async Task An_entry_whose_delivery_is_being_written_is_neither_rewritten_nor_removed()
    {
        var store = new HoldingStore();
        var kind = new GatedKind();
        using var outbox = new DeviceOutbox(store, [kind], new FakeTimeProvider(Start));
        var id = await EnqueueAsync(outbox, """{"n":0}""");
        store.HoldRemoves();

        kind.Release();
        await store.RemoveEntered.WaitAsync(Cancellation);

        Assert.False(await outbox.ReplacePayloadAsync(id, """{"n":1}""", Cancellation));
        Assert.False(await outbox.RemoveAsync(id, Cancellation));

        store.Release();
        await outbox.WhenIdleAsync();
        Assert.Empty(outbox.Entries);
        Assert.Null(outbox.Sending);
        Assert.Equal(["""{"n":0}"""], kind.Sent);
    }

    /// <summary>A failed send is being written down: a rewrite in that moment is
    /// refused rather than taken and then overwritten by the failure's copy of the
    /// old words, and the failure keeps the payload the entry has.</summary>
    [Fact]
    public async Task A_failure_being_written_neither_takes_nor_reverts_a_rewrite()
    {
        var store = new HoldingStore();
        var kind = new GatedKind { RefuseAll = true };
        using var outbox = new DeviceOutbox(store, [kind], new FakeTimeProvider(Start));
        var id = await EnqueueAsync(outbox, """{"n":0}""");
        store.HoldUpdates();

        kind.Release();
        await store.UpdateEntered.WaitAsync(Cancellation);

        Assert.False(await outbox.ReplacePayloadAsync(id, """{"n":1}""", Cancellation));

        store.Release();
        await outbox.WhenIdleAsync();

        var entry = Assert.Single(outbox.Entries);
        Assert.True(entry.Refused);
        Assert.Equal("""{"n":0}""", entry.PayloadJson);
        Assert.Null(outbox.Sending);

        // Once written, it is the person's to change again.
        Assert.True(await outbox.ReplacePayloadAsync(id, """{"n":1}""", Cancellation));
        Assert.Equal("""{"n":1}""", Assert.Single(store.ReadOutbox()).PayloadJson);
    }

    /// <summary>The file refuses the delete: the entry stays where it was — on the
    /// screen and on the device — so the person's second try finds it, rather than
    /// the entry vanishing now and coming back on the next start.</summary>
    [Fact]
    public async Task A_removal_the_file_refuses_leaves_the_entry_in_place_for_a_second_try()
    {
        var store = new RefusingRemoveStore();
        var kind = new GatedKind();
        using var outbox = new DeviceOutbox(store, [kind], new FakeTimeProvider(Start));
        await EnqueueAsync(outbox, """{"n":0}""");
        var waiting = await EnqueueAsync(outbox, """{"n":1}""");

        store.Refuse = true;
        await Assert.ThrowsAsync<IOException>(() => outbox.RemoveAsync(waiting, Cancellation));

        Assert.Contains(outbox.Entries, entry => entry.Id == waiting);
        Assert.Contains(store.ReadOutbox(), entry => entry.Id == waiting);

        store.Refuse = false;
        Assert.True(await outbox.RemoveAsync(waiting, Cancellation));
        Assert.DoesNotContain(outbox.Entries, entry => entry.Id == waiting);

        kind.Release();
        await outbox.WhenIdleAsync();
        Assert.Equal(["""{"n":0}"""], kind.Sent);
    }

    private sealed class RefusingRemoveStore : IDeviceStore
    {
        private readonly InMemoryDeviceStore _inner = new();

        public bool Refuse { get; set; }

        public IReadOnlyList<OutboxEntry> ReadOutbox() => _inner.ReadOutbox();

        public Task AddAsync(OutboxEntry entry, CancellationToken cancellationToken = default) => _inner.AddAsync(entry, cancellationToken);

        public Task UpdateAsync(OutboxEntry entry, CancellationToken cancellationToken = default) => _inner.UpdateAsync(entry, cancellationToken);

        public Task RemoveAsync(Guid id, CancellationToken cancellationToken = default) =>
            Refuse ? Task.FromException(new IOException("The database is locked.")) : _inner.RemoveAsync(id, cancellationToken);

        public CachedInbox? ReadInbox() => _inner.ReadInbox();

        public Task SaveInboxAsync(CachedInbox inbox, CancellationToken cancellationToken = default) => _inner.SaveInboxAsync(inbox, cancellationToken);
    }

    private static async Task<Guid> EnqueueAsync(DeviceOutbox outbox, string payload)
    {
        var id = Guid.CreateVersion7();
        await outbox.EnqueueAsync(GatedKind.Token, id, payload, Cancellation);
        return id;
    }

    /// <summary>The device's file, holding its next update or removal until the
    /// test releases it, so the outbox can be caught writing an outcome down.</summary>
    private sealed class HoldingStore : IDeviceStore
    {
        private readonly InMemoryDeviceStore _inner = new();
        private TaskCompletionSource? _held;
        private bool _holdUpdates;
        private bool _holdRemoves;

        public TaskCompletionSource UpdateEnteredSource { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource RemoveEnteredSource { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task UpdateEntered => UpdateEnteredSource.Task;

        public Task RemoveEntered => RemoveEnteredSource.Task;

        public void HoldUpdates()
        {
            _held = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            _holdUpdates = true;
        }

        public void HoldRemoves()
        {
            _held = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            _holdRemoves = true;
        }

        public void Release()
        {
            _holdUpdates = false;
            _holdRemoves = false;
            _held?.TrySetResult();
        }

        public IReadOnlyList<OutboxEntry> ReadOutbox() => _inner.ReadOutbox();

        public Task AddAsync(OutboxEntry entry, CancellationToken cancellationToken = default) => _inner.AddAsync(entry, cancellationToken);

        public async Task UpdateAsync(OutboxEntry entry, CancellationToken cancellationToken = default)
        {
            if (_holdUpdates && _held is { } held)
            {
                UpdateEnteredSource.TrySetResult();
                await held.Task.WaitAsync(cancellationToken);
            }

            await _inner.UpdateAsync(entry, cancellationToken);
        }

        public async Task RemoveAsync(Guid id, CancellationToken cancellationToken = default)
        {
            if (_holdRemoves && _held is { } held)
            {
                RemoveEnteredSource.TrySetResult();
                await held.Task.WaitAsync(cancellationToken);
            }

            await _inner.RemoveAsync(id, cancellationToken);
        }

        public CachedInbox? ReadInbox() => _inner.ReadInbox();

        public Task SaveInboxAsync(CachedInbox inbox, CancellationToken cancellationToken = default) => _inner.SaveInboxAsync(inbox, cancellationToken);
    }

    /// <summary>A kind that holds every send until the test releases it, so an
    /// entry can be caught on its way out.</summary>
    private sealed class GatedKind : IOutboxKind
    {
        public const string Token = "gated";

        private readonly TaskCompletionSource _gate = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public List<string> Sent { get; } = [];

        public bool RefuseAll { get; init; }

        public string Kind => Token;

        public void Release() => _gate.TrySetResult();

        public async Task<OutboxDelivery> SendAsync(OutboxEntry entry, CancellationToken cancellationToken)
        {
            await _gate.Task.WaitAsync(cancellationToken);
            if (RefuseAll) return OutboxDelivery.Refused("No.");

            Sent.Add(entry.PayloadJson);
            return OutboxDelivery.Delivered;
        }
    }
}
