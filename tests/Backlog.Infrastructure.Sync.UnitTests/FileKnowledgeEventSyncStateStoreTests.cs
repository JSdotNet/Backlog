using Backlog.Infrastructure.Sync.KnowledgeEvents;

namespace Backlog.Infrastructure.Sync.UnitTests;

/// <summary>This device's knowledge event progress and its <c>events</c> switch across a restart,
/// and the update the worker and a cycle both write through without writing back each other's
/// old value (local ADR 0050 §6).</summary>
public sealed class FileKnowledgeEventSyncStateStoreTests : IDisposable
{
    private static readonly DateTimeOffset Noon = new(2026, 11, 18, 12, 0, 0, TimeSpan.Zero);

    private readonly TemporaryDirectory _temp = new("backlog-knowledge-event-sync-state-tests");
    private string _root => _temp.Path;

    private string StatePath => Path.Combine(_root, "knowledge-event-sync-state.json");

    [Fact]
    public void A_saved_state_and_its_switch_are_read_back_by_the_next_run()
    {
        var owner = Guid.NewGuid();
        var area = new EventsAreaSwitch(false, Noon, Sent: false);

        new FileKnowledgeEventSyncStateStore(StatePath).Save(new KnowledgeEventSyncState(Noon, "cursor-1", owner, null, area));

        var reopened = new FileKnowledgeEventSyncStateStore(StatePath);

        Assert.Equal(new KnowledgeEventSyncState(Noon, "cursor-1", owner, null, area), reopened.Current);
    }

    [Fact]
    public void A_file_from_before_the_switch_reads_with_none()
    {
        Directory.CreateDirectory(_root);
        File.WriteAllText(StatePath, """{"pushWatermark":"2026-11-18T12:00:00+00:00","pullCursor":"cursor-1"}""");

        var store = new FileKnowledgeEventSyncStateStore(StatePath);

        Assert.Equal("cursor-1", store.Current.PullCursor);
        Assert.Null(store.Current.Area);
    }

    [Fact]
    public void An_update_that_changes_nothing_writes_nothing()
    {
        var store = new FileKnowledgeEventSyncStateStore(StatePath);
        var raised = 0;
        store.Changed += () => raised++;

        var answered = store.Update(state => state);

        Assert.Same(store.Current, answered);
        Assert.Equal(0, raised);
        Assert.False(File.Exists(StatePath));
    }

    /// <summary>The race the review found: a switch recorded while a cycle saves its cursor. Each
    /// update reads what the other wrote, so neither value is lost.</summary>
    [Fact]
    public async Task Updates_from_two_threads_keep_both_writers_values()
    {
        var store = new FileKnowledgeEventSyncStateStore(StatePath);
        using var start = new ManualResetEventSlim();

        var cursors = Task.Run(() =>
        {
            start.Wait(TestContext.Current.CancellationToken);
            for (var page = 1; page <= 50; page++) store.Update(state => state with { PullCursor = $"cursor-{page}" });
        }, TestContext.Current.CancellationToken);

        var switches = Task.Run(() =>
        {
            start.Wait(TestContext.Current.CancellationToken);
            for (var move = 1; move <= 50; move++)
            {
                var at = Noon.AddMinutes(move);
                store.Update(state => state with { Area = new EventsAreaSwitch(move % 2 == 0, at, Sent: false) });
            }
        }, TestContext.Current.CancellationToken);

        start.Set();
        await Task.WhenAll(cursors, switches);

        Assert.Equal("cursor-50", store.Current.PullCursor);
        Assert.Equal(new EventsAreaSwitch(true, Noon.AddMinutes(50), Sent: false), store.Current.Area);
        Assert.Equal(store.Current, new FileKnowledgeEventSyncStateStore(StatePath).Current);
    }

    public void Dispose() => _temp.Dispose();
}
