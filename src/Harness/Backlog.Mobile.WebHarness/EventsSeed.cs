using Backlog.Infrastructure.Sync.KnowledgeEvents;
using Backlog.Mobile.UI.Events;
using Backlog.Modules.Events.Abstractions;
using Backlog.Modules.Events.Abstractions.DataTransferObjects;
using Backlog.Modules.Sync.Abstractions.DataTransferObjects;

namespace Backlog.Mobile.WebHarness;

/// <summary>
/// Knowledge events for the browser harness to show without a desktop that has its events
/// feature on: a series and five events — two coming up, one with no dates yet, one that is over
/// and one Skipped — dated from today, so the Events tab, its list, the detail and the Notes tab's
/// attach picker always have something in them (<c>.devbook/domain/events/requirements.md#events-on-the-phone</c>).
/// <para>
/// Written straight into the phone's event view as the feed carries them, and only while it holds
/// nothing: a real pull later folds over the rows by the usual last-write-wins, and a harness that
/// already holds events is left alone. No cursor is written, so the first real pull starts from
/// the beginning. Harness only, and only while <c>Harness:SeedEvents</c> is on. It writes before
/// anything reads the view, because the projection reads its rows once.
/// </para>
/// </summary>
internal static class EventsSeed
{
    /// <summary>Seeds the view when the harness's configuration asks for it and it holds nothing yet.</summary>
    public static async Task SeedAsync(IServiceProvider services, TimeProvider clock, CancellationToken cancellationToken = default)
    {
        var store = services.GetRequiredService<IEventViewStore>();
        if (store.ReadRows().Count > 0) return;

        var now = clock.GetUtcNow();
        var today = DateOnly.FromDateTime(clock.GetLocalNow().DateTime);
        var series = new EventSeriesReplicaDto(Guid.CreateVersion7(now), "Techorama", "Techorama BV", "https://techorama.nl", now);

        KnowledgeEventReplicaDto Event(
            string title,
            KnowledgeEventState state,
            EventDateReplicaDto? start = null,
            EventDateReplicaDto? end = null,
            string? venue = null,
            bool online = false,
            string? link = null,
            IReadOnlyList<EventKeyDateReplicaDto>? keyDates = null,
            string? whatToLearn = null,
            Guid? seriesId = null,
            IReadOnlyList<EventNoteLinkReplicaDto>? notes = null,
            string? skipReason = null) =>
            new(
                Id: Guid.CreateVersion7(now),
                Title: title,
                State: state,
                Start: start,
                End: end,
                DateSource: start is null ? null : DateSource.Typed,
                Venue: venue,
                Online: online,
                Link: link,
                KeyDates: keyDates ?? [],
                WhatToLearn: whatToLearn,
                SeriesId: seriesId,
                Notes: notes ?? [],
                SkipReason: skipReason,
                Ticket: null,
                Cost: null,
                Tasks: [],
                Verdict: null,
                UpdatedAt: now);

        KnowledgeEventReplicaDto[] events =
        [
            Event(
                "Techorama 2026",
                KnowledgeEventState.Going,
                new EventDateReplicaDto(today.AddDays(5), new TimeOnly(8, 30), TimeSpan.FromHours(1)),
                new EventDateReplicaDto(today.AddDays(7), null, null),
                venue: "Kinepolis Utrecht",
                link: "https://techorama.nl",
                keyDates:
                [
                    new EventKeyDateReplicaDto(today.AddDays(-30), "Early bird ends"),
                    new EventKeyDateReplicaDto(today.AddDays(2), "Agenda published"),
                ],
                whatToLearn: "Aspire in production\nHow others run Blazor on phones",
                seriesId: series.Id,
                notes: [new EventNoteLinkReplicaDto(NoteLinkKind.Brain, "Talks/aspire-in-production.md", "Aspire in production")]),
            Event(
                "NDC Porto",
                KnowledgeEventState.Considering,
                new EventDateReplicaDto(today.AddDays(30), null, null),
                new EventDateReplicaDto(today.AddDays(32), null, null),
                venue: "Alfândega do Porto",
                online: true,
                link: "https://ndcporto.com",
                keyDates: [new EventKeyDateReplicaDto(today.AddDays(10), "Ticket price goes up")]),
            Event("Build 2027", KnowledgeEventState.Spotted, online: true, link: "https://build.microsoft.com"),
            Event(
                "Update Conference",
                KnowledgeEventState.Attended,
                new EventDateReplicaDto(today.AddDays(-20), null, null),
                new EventDateReplicaDto(today.AddDays(-18), null, null),
                venue: "Clarion Congress Hotel Prague"),
            Event(
                "Cloud Summit",
                KnowledgeEventState.Skipped,
                new EventDateReplicaDto(today.AddDays(12), null, null),
                venue: "Jaarbeurs Utrecht",
                skipReason: "Clashes with the release"),
        ];

        var device = Guid.CreateVersion7(now);
        var rows = new List<KnowledgeEventChangeRecord> { new(KnowledgeEventDocuments.ToChange(series), device, 0) };
        rows.AddRange(events.Select(item => new KnowledgeEventChangeRecord(KnowledgeEventDocuments.ToChange(item), device, 0)));

        await store.SaveAsync(rows, cursor: null, cancellationToken);
    }
}
