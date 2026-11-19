using Backlog.Mobile.UI.Outbox;
using Backlog.Modules.Sync.Abstractions.DataTransferObjects;

namespace Backlog.Mobile.UI.UnitTests;

/// <summary>
/// The file behind the outbox and the cached inbox: what goes in comes back
/// out, in the order it was written, after the store is opened again.
/// </summary>
public sealed class SqliteDeviceStoreTests : IDisposable
{
    private readonly TemporaryDirectory _temp = new("backlog-mobile-store");

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    private string DatabasePath => _temp.Combine("nested", "mobile-outbox.db");

    public void Dispose() => _temp.Dispose();

    /// <summary>A payload rewritten while the entry waited — the person rewording a
    /// capture, a staged kind's checkpoint — is what comes back after the app is
    /// ended, not the words it was first written with.</summary>
    [Fact]
    public async Task A_rewritten_payload_comes_back_after_a_reopen()
    {
        var at = new DateTimeOffset(2026, 11, 19, 9, 0, 0, TimeSpan.Zero);
        var entry = new OutboxEntry(Guid.CreateVersion7(), "capture", """{"title":"one"}""", 0, null, at);

        var store = new SqliteDeviceStore(DatabasePath);
        await store.AddAsync(entry, Cancellation);
        await store.UpdateAsync(entry with { PayloadJson = """{"title":"reworded"}""", Attempts = 1 }, Cancellation);

        Assert.Equal(
            [entry with { PayloadJson = """{"title":"reworded"}""", Attempts = 1 }],
            new SqliteDeviceStore(DatabasePath).ReadOutbox());
    }

    /// <summary>The whole path: the outbox rewrites a waiting entry, the app is
    /// ended, and the next run's outbox reads the rewritten words from the file.</summary>
    [Fact]
    public async Task An_outbox_rewrite_survives_the_app_being_ended()
    {
        var clock = new Microsoft.Extensions.Time.Testing.FakeTimeProvider(new DateTimeOffset(2026, 11, 19, 9, 0, 0, TimeSpan.Zero));
        var id = Guid.CreateVersion7();

        using (var outbox = new DeviceOutbox(new SqliteDeviceStore(DatabasePath), [new OfflineKind()], clock))
        {
            await outbox.EnqueueAsync("capture", id, """{"title":"one"}""", Cancellation);
            await outbox.WhenIdleAsync();
            Assert.True(await outbox.ReplacePayloadAsync(id, """{"title":"reworded"}""", Cancellation));
        }

        using var restarted = new DeviceOutbox(new SqliteDeviceStore(DatabasePath), [new OfflineKind()], clock);

        Assert.Equal("""{"title":"reworded"}""", Assert.Single(restarted.Entries).PayloadJson);
    }

    private sealed class OfflineKind : IOutboxKind
    {
        public string Kind => "capture";

        public Task<OutboxDelivery> SendAsync(OutboxEntry entry, CancellationToken cancellationToken) =>
            Task.FromResult(OutboxDelivery.Transient("No network."));
    }

    [Fact]
    public async Task The_outbox_comes_back_in_the_order_it_was_written_after_a_reopen()
    {
        var at = new DateTimeOffset(2026, 9, 25, 9, 30, 0, TimeSpan.Zero);
        var first = new OutboxEntry(Guid.CreateVersion7(), "capture", """{"title":"one"}""", 0, null, at);
        var second = new OutboxEntry(Guid.CreateVersion7(), "capture", """{"title":"two"}""", 0, null, at);
        var third = new OutboxEntry(Guid.CreateVersion7(), "capture", """{"title":"three"}""", 0, null, at.AddSeconds(1));

        var store = new SqliteDeviceStore(DatabasePath);
        await store.AddAsync(first, Cancellation);
        await store.AddAsync(second, Cancellation);
        await store.AddAsync(third, Cancellation);
        await store.UpdateAsync(second with { Attempts = 2, LastError = "No network." }, Cancellation);
        await store.UpdateAsync(third with { Refused = true, LastError = "A capture needs a title." }, Cancellation);
        await store.RemoveAsync(first.Id, Cancellation);

        var reopened = new SqliteDeviceStore(DatabasePath);

        Assert.Equal(
            [second with { Attempts = 2, LastError = "No network." }, third with { Refused = true, LastError = "A capture needs a title." }],
            reopened.ReadOutbox());
    }

    [Fact]
    public async Task The_cached_inbox_is_the_last_one_saved_with_when_it_was_pulled()
    {
        var store = new SqliteDeviceStore(DatabasePath);
        Assert.Null(store.ReadInbox());

        var pulledAt = new DateTimeOffset(2026, 9, 25, 9, 30, 0, TimeSpan.Zero);
        var item = new InboxItem(Guid.CreateVersion7(), "Ask about the offsite", "mobile", pulledAt.AddMinutes(-5), "Dates.", ["planning"], "alex");

        await store.SaveInboxAsync(new CachedInbox([new InboxItem(Guid.CreateVersion7(), "Old", "mobile", pulledAt)], pulledAt.AddHours(-1)), Cancellation);
        await store.SaveInboxAsync(new CachedInbox([item], pulledAt), Cancellation);

        var cached = new SqliteDeviceStore(DatabasePath).ReadInbox()!;

        Assert.Equal(pulledAt, cached.PulledAt);
        Assert.Equivalent(new[] { item }, cached.Items, strict: true);
    }
}
