using Backlog.Infrastructure.Sqlite.Events;
using Backlog.Modules.Events;
using Backlog.Modules.Events.DomainModels;

using Microsoft.Extensions.Time.Testing;

namespace Backlog.Infrastructure.Sqlite.UnitTests;

/// <summary>
/// The knowledge event and event series tables as replication reads and writes them (local ADR
/// 0050 §2): what changed since a watermark, oldest first, each with its stamp; and a copy from
/// another desktop written only when it is later than the row, at the stamp it arrived with, so
/// the next push does not offer it back as a newer change.
/// </summary>
public sealed class SqliteKnowledgeEventReplicaStoreTests : IDisposable
{
    private static readonly DateTimeOffset Noon = new(2026, 11, 10, 12, 0, 0, TimeSpan.Zero);

    private readonly string _dir = Path.Combine(Path.GetTempPath(), "knowledge-event-replica-tests-" + Guid.NewGuid().ToString("N"));
    private readonly FakeTimeProvider _clock = new(Noon);
    private readonly SqliteKnowledgeEventRepository _events;
    private readonly SqliteEventSeriesRepository _series;

    public SqliteKnowledgeEventReplicaStoreTests()
    {
        _events = new SqliteKnowledgeEventRepository(_dir, _clock);
        _series = new SqliteEventSeriesRepository(_dir, _clock);
    }

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    private static KnowledgeEvent Event(string title) => KnowledgeEvent.Create(Guid.NewGuid(), title).Value;

    [Fact]
    public async Task Only_events_saved_after_the_watermark_are_listed_oldest_first_with_their_stamps()
    {
        var before = Event("Before");
        var later = Event("Later");
        var earlier = Event("Earlier");

        await _events.SaveAsync(before, Cancellation);
        _clock.SetUtcNow(Noon.AddMinutes(10));
        await _events.SaveAsync(earlier, Cancellation);
        _clock.SetUtcNow(Noon.AddMinutes(20));
        await _events.SaveAsync(later, Cancellation);

        var changed = await _events.ListChangedSinceAsync(Noon, Cancellation);

        Assert.Equal(
            [(earlier.Id, Noon.AddMinutes(10)), (later.Id, Noon.AddMinutes(20))],
            changed.Select(stored => (stored.Event.Id, stored.UpdatedAt)));
    }

    [Fact]
    public async Task A_copy_for_an_event_this_machine_lacks_is_written_at_the_stamp_it_arrived_with()
    {
        var arriving = Event("Techorama 2026");

        Assert.True(await _events.SaveReplicatedAsync(arriving, Noon.AddHours(-3), Cancellation));

        Assert.Equal("Techorama 2026", (await _events.GetAsync(arriving.Id, Cancellation))!.Title);
        var stored = Assert.Single(await _events.ListChangedSinceAsync(DateTimeOffset.MinValue, Cancellation));
        Assert.Equal(Noon.AddHours(-3), stored.UpdatedAt);
        Assert.Empty(await _events.ListChangedSinceAsync(Noon.AddHours(-3), Cancellation));
    }

    [Fact]
    public async Task A_later_copy_replaces_the_event_whole()
    {
        var local = Event("dotNET Friday");
        await _events.SaveAsync(local, Cancellation);

        var arriving = KnowledgeEvent.Restore(
            local.Id, "dotNET Friday Q4", null, null, null, null, Place.Online, null,
            Backlog.Modules.Events.Abstractions.KnowledgeEventState.Going, null, null, [], null, null, [], [], null);

        Assert.True(await _events.SaveReplicatedAsync(arriving, Noon.AddMinutes(1), Cancellation));

        var read = await _events.GetAsync(local.Id, Cancellation);
        Assert.Equal("dotNET Friday Q4", read!.Title);
        Assert.Equal(Backlog.Modules.Events.Abstractions.KnowledgeEventState.Going, read.State);
        Assert.Same(Place.Online, read.Place);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task A_copy_no_later_than_the_row_changes_nothing(int minutes)
    {
        var local = Event("Kept");
        await _events.SaveAsync(local, Cancellation);

        var arriving = KnowledgeEvent.Create(local.Id, "Overwritten").Value;

        Assert.False(await _events.SaveReplicatedAsync(arriving, Noon.AddMinutes(minutes), Cancellation));

        Assert.Equal("Kept", (await _events.GetAsync(local.Id, Cancellation))!.Title);
        Assert.Equal(Noon, Assert.Single(await _events.ListChangedSinceAsync(DateTimeOffset.MinValue, Cancellation)).UpdatedAt);
    }

    [Fact]
    public async Task Series_are_listed_and_replicated_on_the_same_terms()
    {
        var local = EventSeries.Create(Guid.NewGuid(), "Techorama", "Techorama BV", null).Value;
        await _series.SaveAsync(local, Cancellation);

        Assert.Equal(local.Id, Assert.Single(await _series.ListChangedSinceAsync(Noon.AddTicks(-1), Cancellation)).Series.Id);
        Assert.Empty(await _series.ListChangedSinceAsync(Noon, Cancellation));

        var older = EventSeries.Restore(local.Id, "Older name", null, null);
        Assert.False(await _series.SaveReplicatedAsync(older, Noon.AddMinutes(-5), Cancellation));

        var later = EventSeries.Restore(local.Id, "Techorama NL", "Techorama BV", "https://techorama.nl");
        Assert.True(await _series.SaveReplicatedAsync(later, Noon.AddMinutes(5), Cancellation));

        var read = await _series.GetAsync(local.Id, Cancellation);
        Assert.Equal("Techorama NL", read!.Name);
        Assert.Equal("https://techorama.nl", read.Link);
        Assert.Equal(Noon.AddMinutes(5), Assert.Single(await _series.ListChangedSinceAsync(Noon, Cancellation)).UpdatedAt);
    }

    /// <summary>A copy from a desktop whose clock runs ahead holds a stamp later than this
    /// machine's now: an edit saved over it must still land after it, or the next push lists
    /// nothing past the watermark and the replica refuses the edit as no later.</summary>
    [Fact]
    public async Task A_local_save_over_a_copy_stamped_ahead_of_this_clock_lands_one_tick_after_it()
    {
        var pulled = Event("Techorama 2026");
        var ahead = Noon.AddMinutes(2);
        Assert.True(await _events.SaveReplicatedAsync(pulled, ahead, Cancellation));

        var edited = KnowledgeEvent.Restore(
            pulled.Id, "Techorama 2026 (edited)", null, null, null, null, Place.Online, null,
            Backlog.Modules.Events.Abstractions.KnowledgeEventState.Going, null, null, [], null, null, [], [], null);
        await _events.SaveAsync(edited, Cancellation);

        var stored = Assert.Single(await _events.ListChangedSinceAsync(ahead, Cancellation));
        Assert.Equal("Techorama 2026 (edited)", stored.Event.Title);
        Assert.Equal(ahead.AddTicks(1), stored.UpdatedAt);
    }

    [Fact]
    public async Task A_local_series_save_over_a_copy_stamped_ahead_of_this_clock_lands_one_tick_after_it()
    {
        var pulled = EventSeries.Create(Guid.NewGuid(), "Techorama", null, null).Value;
        var ahead = Noon.AddMinutes(2);
        Assert.True(await _series.SaveReplicatedAsync(pulled, ahead, Cancellation));

        await _series.SaveAsync(EventSeries.Restore(pulled.Id, "Techorama NL", null, null), Cancellation);

        var stored = Assert.Single(await _series.ListChangedSinceAsync(ahead, Cancellation));
        Assert.Equal("Techorama NL", stored.Series.Name);
        Assert.Equal(ahead.AddTicks(1), stored.UpdatedAt);
    }

    [Fact]
    public async Task A_local_save_over_an_older_row_is_stamped_now()
    {
        var local = Event("dotNET Friday");
        await _events.SaveAsync(local, Cancellation);

        _clock.SetUtcNow(Noon.AddMinutes(30));
        await _events.SaveAsync(local, Cancellation);

        Assert.Equal(Noon.AddMinutes(30), Assert.Single(await _events.ListChangedSinceAsync(Noon, Cancellation)).UpdatedAt);
    }

    /// <summary>The storage-folder-following adapters the host registers answer the replication
    /// ports too, over the same file.</summary>
    [Fact]
    public async Task The_rooted_adapters_answer_the_replication_ports_over_the_same_file()
    {
        IKnowledgeEventReplicaStore events = new RootedSqliteKnowledgeEventRepository(() => _dir, _clock);
        IEventSeriesReplicaStore series = new RootedSqliteEventSeriesRepository(() => _dir, _clock);

        var arriving = Event("Build 2027");
        Assert.True(await events.SaveReplicatedAsync(arriving, Noon, Cancellation));
        Assert.True(await series.SaveReplicatedAsync(EventSeries.Create(Guid.NewGuid(), "Build", null, null).Value, Noon, Cancellation));

        Assert.Equal(arriving.Id, Assert.Single(await _events.ListChangedSinceAsync(DateTimeOffset.MinValue, Cancellation)).Event.Id);
        Assert.Single(await series.ListChangedSinceAsync(DateTimeOffset.MinValue, Cancellation));
    }

    public void Dispose()
    {
        SqliteDatabaseFile.ReleasePool(SqliteTaskRepository.DatabasePathFor(_dir));

        if (!Directory.Exists(_dir)) return;
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }
}
