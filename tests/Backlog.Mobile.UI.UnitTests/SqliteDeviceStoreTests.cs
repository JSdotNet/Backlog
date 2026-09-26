using Backlog.Mobile.UI.Outbox;
using Backlog.Modules.Sync.Abstractions.DataTransferObjects;

namespace Backlog.Mobile.UI.UnitTests;

/// <summary>
/// The file behind the outbox and the cached inbox: what goes in comes back
/// out, in the order it was written, after the store is opened again.
/// </summary>
public sealed class SqliteDeviceStoreTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "backlog-mobile-store-" + Guid.NewGuid().ToString("N"));

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    private string DatabasePath => Path.Combine(_directory, "nested", "mobile-outbox.db");

    public void Dispose()
    {
        if (Directory.Exists(_directory)) Directory.Delete(_directory, recursive: true);
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
