using System.Text.Json;

using Backlog.Infrastructure.Sync.KnowledgeEvents;
using Backlog.Modules.Events.Abstractions;
using Backlog.Modules.Sync.Abstractions.DataTransferObjects;

using Microsoft.Extensions.Logging;

namespace Backlog.Infrastructure.Sync.UnitTests;

/// <summary>
/// A page of the owner's knowledge event feed reconciled with this device's events and series,
/// and the wire document both ends read: one winner per id by the replica's stamp, then the
/// writer's <c>UpdatedAt</c>, then the device id — the devbook subject merge's rule — each
/// handed to Events by its kind, and a document this device cannot read skipped and named
/// rather than thrown on.
/// </summary>
public sealed class KnowledgeEventReplicaMergeTests
{
    private static readonly DateTimeOffset Noon = new(2026, 11, 10, 12, 0, 0, TimeSpan.Zero);

    private static readonly Guid Laptop = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid Tower = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    private static KnowledgeEventChangeRecord Record(KnowledgeEventChange change, long serverTimestamp, Guid? device = null) =>
        new(change, device ?? Laptop, serverTimestamp);

    [Fact]
    public void An_event_crosses_the_wire_and_reads_back_field_for_field()
    {
        var sent = KnowledgeEventCopies.Techorama(Noon, Guid.NewGuid());

        var change = KnowledgeEventDocuments.ToChange(sent);
        var read = KnowledgeEventDocuments.ReadEvent(change);

        Assert.Equal(KnowledgeEventKinds.KnowledgeEvent, change.Kind);
        Assert.Equal(sent.Id, change.Id);
        Assert.Equal(Noon, change.UpdatedAt);
        Assert.NotNull(read);
        Assert.Equal(sent.Title, read.Title);
        Assert.Equal(sent.State, read.State);
        Assert.Equal(sent.Start, read.Start);
        Assert.Equal(sent.End, read.End);
        Assert.Equal(sent.DateSource, read.DateSource);
        Assert.Equal(sent.Venue, read.Venue);
        Assert.Equal(sent.Link, read.Link);
        Assert.Equal(sent.KeyDates, read.KeyDates);
        Assert.Equal(sent.WhatToLearn, read.WhatToLearn);
        Assert.Equal(sent.SeriesId, read.SeriesId);
        Assert.Equal(sent.Notes, read.Notes);
        Assert.Equal(sent.Ticket, read.Ticket);
        Assert.Equal(sent.Cost, read.Cost);
        Assert.Equal(sent.Tasks, read.Tasks);
    }

    [Fact]
    public void A_series_crosses_the_wire_under_its_own_kind()
    {
        var sent = KnowledgeEventCopies.Series("Techorama", Noon);

        var change = KnowledgeEventDocuments.ToChange(sent);

        Assert.Equal(KnowledgeEventKinds.EventSeries, change.Kind);
        Assert.Equal(sent, KnowledgeEventDocuments.ReadSeries(change));
        Assert.Null(KnowledgeEventDocuments.ReadEvent(change));
    }

    /// <summary>The envelope's stamp is the one the replica compared, so it is the one the copy
    /// is kept at, whatever the document says.</summary>
    [Fact]
    public void The_envelopes_stamp_is_the_one_a_read_copy_carries()
    {
        var change = KnowledgeEventDocuments.ToChange(KnowledgeEventCopies.Event("dotNET Friday", Noon)) with { UpdatedAt = Noon.AddMinutes(3) };

        Assert.Equal(Noon.AddMinutes(3), KnowledgeEventDocuments.ReadEvent(change)!.UpdatedAt);
    }

    public static TheoryData<string> Unreadable() =>
    [
        """{"title":"No id"}""",
        """{"id":"00000000-0000-0000-0000-000000000000","title":"Another id"}""",
        """{"id":"IDENTITY","title":"A state from a later build","state":"rescheduled"}""",
        """["not","an","object"]""",
    ];

    [Theory]
    [MemberData(nameof(Unreadable))]
    public void A_document_that_does_not_read_as_the_event_it_claims_is_null(string json)
    {
        var id = Guid.NewGuid();
        var change = new KnowledgeEventChange(
            id,
            KnowledgeEventKinds.KnowledgeEvent,
            Noon,
            JsonDocument.Parse(json.Replace("IDENTITY", id.ToString(), StringComparison.Ordinal)).RootElement.Clone());

        Assert.Null(KnowledgeEventDocuments.ReadEvent(change));
    }

    /// <summary>A document from an older build that left its lists out reads with none.</summary>
    [Fact]
    public void A_document_without_its_lists_reads_with_none()
    {
        var id = Guid.NewGuid();
        var change = new KnowledgeEventChange(
            id,
            KnowledgeEventKinds.KnowledgeEvent,
            Noon,
            JsonDocument.Parse($$"""{"id":"{{id}}","title":"A meetup","state":"spotted"}""").RootElement.Clone());

        var read = KnowledgeEventDocuments.ReadEvent(change);

        Assert.NotNull(read);
        Assert.Empty(read.Notes);
        Assert.Empty(read.KeyDates);
        Assert.Empty(read.Tasks);
        Assert.Equal(KnowledgeEventState.Spotted, read.State);
    }

    [Fact]
    public void The_later_record_wins_by_the_replicas_stamp_then_the_writers_then_the_device()
    {
        var id = Guid.NewGuid();
        var early = KnowledgeEventDocuments.ToChange(KnowledgeEventCopies.Event("Early", Noon, id));
        var late = KnowledgeEventDocuments.ToChange(KnowledgeEventCopies.Event("Late", Noon.AddMinutes(1), id));

        Assert.True(KnowledgeEventReplicaMerge.Wins(Record(early, 2), Record(late, 1)));
        Assert.True(KnowledgeEventReplicaMerge.Wins(Record(late, 1), Record(early, 1)));
        Assert.True(KnowledgeEventReplicaMerge.Wins(Record(early, 1, Tower), Record(early, 1, Laptop)));
        Assert.False(KnowledgeEventReplicaMerge.Wins(Record(early, 1, Laptop), Record(early, 1, Tower)));
    }

    [Fact]
    public async Task A_page_is_reduced_to_one_winner_per_id_and_each_kind_goes_to_its_own_door()
    {
        var replication = new InMemoryKnowledgeEventReplication();
        var merge = new KnowledgeEventReplicaMerge(replication);

        var id = Guid.NewGuid();
        var first = KnowledgeEventDocuments.ToChange(KnowledgeEventCopies.Event("First", Noon, id));
        var second = KnowledgeEventDocuments.ToChange(KnowledgeEventCopies.Event("Second", Noon.AddMinutes(1), id));
        var series = KnowledgeEventDocuments.ToChange(KnowledgeEventCopies.Series("Techorama", Noon));

        var outcome = await merge.ApplyAsync([Record(second, 2), Record(first, 1), Record(series, 3)], Cancellation);

        Assert.Equal(2, outcome.Applied);
        Assert.Equal(new[] { id, series.Id }.Order(), replication.Taken.Order());
        Assert.Equal("Second", replication.Event(id)!.Title);
        Assert.Equal("Techorama", replication.Series(series.Id)!.Name);
    }

    [Fact]
    public async Task A_document_this_device_cannot_read_is_skipped_and_named()
    {
        var replication = new InMemoryKnowledgeEventReplication();
        var log = new CapturingLogger<KnowledgeEventReplicaMerge>();
        var merge = new KnowledgeEventReplicaMerge(replication, log);

        var foreign = new KnowledgeEventChange(
            Guid.NewGuid(),
            KnowledgeEventKinds.KnowledgeEvent,
            Noon,
            JsonDocument.Parse("""{"title":"No id"}""").RootElement.Clone());
        var blank = KnowledgeEventDocuments.ToChange(KnowledgeEventCopies.Event(" ", Noon));
        var kept = KnowledgeEventDocuments.ToChange(KnowledgeEventCopies.Event("Kept", Noon));

        var outcome = await merge.ApplyAsync([Record(foreign, 1), Record(blank, 2), Record(kept, 3)], Cancellation);

        Assert.Equal(1, outcome.Applied);
        Assert.Equal([kept.Id], replication.Taken);
        Assert.Equal(2, log.Lines.Count(line => line.Level == LogLevel.Warning));
        Assert.Contains(log.Lines, line => line.Message.Contains(foreign.Id.ToString(), StringComparison.OrdinalIgnoreCase));
    }
}
