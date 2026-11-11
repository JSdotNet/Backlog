using Backlog.Infrastructure.Sync.KnowledgeEvents;
using Backlog.Mobile.UI.Events;
using Backlog.Modules.Events.Abstractions;
using Backlog.Modules.Events.Abstractions.DataTransferObjects;
using Backlog.Modules.Sync.Abstractions.DataTransferObjects;

namespace Backlog.Mobile.UI.UnitTests;

/// <summary><c>event_view</c> survives the app closing: the events, the series and the cursor
/// come back from the file as they went in, beside the outbox in the same file, so the Events
/// tab draws before any pull answers.</summary>
public sealed class SqliteEventViewStoreTests : IDisposable
{
    private static readonly DateTimeOffset T0 = new(2026, 10, 27, 7, 0, 0, TimeSpan.Zero);

    private readonly string _path = Path.Combine(Path.GetTempPath(), $"event-view-{Guid.NewGuid():N}.db");

    public void Dispose()
    {
        if (File.Exists(_path)) File.Delete(_path);
    }

    [Fact]
    public async Task Events_series_and_the_cursor_survive_a_new_store_over_the_same_file()
    {
        var ct = TestContext.Current.CancellationToken;
        var series = TestEvents.Series("Techorama", T0);
        var knowledgeEvent = TestEvents.Event(
            "Techorama 2026",
            T0,
            new DateOnly(2026, 10, 26),
            new DateOnly(2026, 10, 28),
            seriesId: series.Id,
            notes: [new EventNoteLinkReplicaDto(NoteLinkKind.Talk, "6f1c2d3e-0000-0000-0000-000000000042", "Keynote")]);

        var store = new SqliteEventViewStore(_path);
        await store.SaveAsync(
            [
                new KnowledgeEventChangeRecord(KnowledgeEventDocuments.ToChange(knowledgeEvent), Guid.NewGuid(), 7),
                new KnowledgeEventChangeRecord(KnowledgeEventDocuments.ToChange(series), Guid.NewGuid(), 8),
            ],
            "c:2",
            ct);

        _ = new Outbox.SqliteDeviceStore(_path);
        var reopened = new SqliteEventViewStore(_path);

        Assert.Equal("c:2", reopened.ReadCursor());
        var rows = reopened.ReadRows();
        Assert.Equal(2, rows.Count);
        var eventRow = Assert.Single(rows, row => row.Change.Kind == KnowledgeEventKinds.KnowledgeEvent);
        Assert.Equal(7, eventRow.ServerTimestamp);
        var read = KnowledgeEventDocuments.ReadEvent(eventRow.Change);
        Assert.NotNull(read);
        Assert.Equal("Techorama 2026", read.Title);
        Assert.Equal(series.Id, read.SeriesId);
        Assert.Equal("Keynote", Assert.Single(read.Notes).Title);
        Assert.Equal("Techorama", KnowledgeEventDocuments.ReadSeries(Assert.Single(rows, row => row.Change.Kind == KnowledgeEventKinds.EventSeries).Change)!.Name);

        var later = knowledgeEvent with { Title = "Techorama 2026 (moved)", UpdatedAt = T0.AddHours(1) };
        await reopened.SaveAsync([new KnowledgeEventChangeRecord(KnowledgeEventDocuments.ToChange(later), Guid.NewGuid(), 9)], cancellationToken: ct);
        Assert.Equal("c:2", reopened.ReadCursor());
        Assert.Equal(2, reopened.ReadRows().Count);

        await reopened.ResetCursorAsync(ct);
        Assert.Null(reopened.ReadCursor());
        Assert.Equal(2, reopened.ReadRows().Count);
    }
}
