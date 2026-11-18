using Backlog.Modules.Events.Abstractions;
using Backlog.Modules.Events.DomainModels;
using Backlog.SharedKernel.Results;

namespace Backlog.Modules.Events.Services;

/// <summary>
/// Answers <see cref="IKnowledgeEvents"/> over the two repositories: the To Event route's
/// Spotted event, in the series of its watched page or a new one
/// (<c>.devbook/domain/inbox/features.md#route-to-event</c>).
/// <para>
/// The series is saved before the event, so an event never names a series that was not
/// written; a series written for an event that then failed to save is an empty series the
/// person can still see, which costs less than an event pointing nowhere.
/// </para>
/// </summary>
internal sealed class KnowledgeEvents(IKnowledgeEventRepository events, IEventSeriesRepository series) : IKnowledgeEvents
{
    public async Task<Result<SpottedKnowledgeEventDto>> SpotAsync(SpotKnowledgeEventDto request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var created = KnowledgeEvent.Create(Guid.CreateVersion7(), request.Title);
        if (created.IsFailure) return created.Error;

        var knowledgeEvent = created.Value;
        knowledgeEvent.SetLink(request.Link);
        SetFoundDates(knowledgeEvent, request.Start, request.End);
        knowledgeEvent.SetPlace(PlaceOf(request.Place));

        var (seriesId, started) = await SeriesForAsync(request, cancellationToken).ConfigureAwait(false);
        knowledgeEvent.SetSeries(seriesId);

        await events.SaveAsync(knowledgeEvent, cancellationToken).ConfigureAwait(false);

        return new SpottedKnowledgeEventDto(knowledgeEvent.Id, seriesId, started);
    }

    /// <summary>The found dates, read from the event page. What a rule refuses is let go rather
    /// than the route: a start whose time has no offset keeps its day, and an end the start
    /// does not precede is dropped.</summary>
    private static void SetFoundDates(KnowledgeEvent knowledgeEvent, EventDateDto? start, EventDateDto? end)
    {
        if (DateOf(start) is not { } from) return;

        var to = DateOf(end);
        if (knowledgeEvent.SetDates(from, to, DateSource.EventPage).IsFailure)
            knowledgeEvent.SetDates(from, null, DateSource.EventPage);
    }

    private static EventDate? DateOf(EventDateDto? date)
    {
        if (date is null) return null;

        var exact = EventDate.Create(date.Date, date.Time, date.UtcOffset);
        return exact.IsSuccess ? exact.Value : EventDate.Create(date.Date, null, null).Value;
    }

    private static Place? PlaceOf(string? written)
    {
        if (string.IsNullOrWhiteSpace(written)) return null;

        return string.Equals(written.Trim(), "Online", StringComparison.OrdinalIgnoreCase)
            ? Place.Online
            : Place.AtVenue(written).Value;
    }

    /// <summary>The series whose page is the watched page, or a new one named after it; none
    /// without a page.</summary>
    private async Task<(Guid? Id, bool Started)> SeriesForAsync(SpotKnowledgeEventDto request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.SeriesPage)) return (null, false);

        var page = request.SeriesPage.Trim();
        var known = await series.ListAsync(cancellationToken).ConfigureAwait(false);
        if (known.FirstOrDefault(each => SamePage(each.Link, page)) is { } match) return (match.Id, false);

        var name = string.IsNullOrWhiteSpace(request.SeriesPageTitle) ? HostOf(page) : request.SeriesPageTitle;
        var started = EventSeries.Create(Guid.CreateVersion7(), name, organiser: null, link: page).Value;
        await series.SaveAsync(started, cancellationToken).ConfigureAwait(false);

        return (started.Id, true);
    }

    /// <summary>Two links name the same page when they differ only in case or a trailing
    /// slash: <c>https://TECHORAMA.nl</c> is <c>https://techorama.nl/</c>.</summary>
    internal static bool SamePage(string? link, string page) =>
        link is not null && string.Equals(Normalized(link), Normalized(page), StringComparison.OrdinalIgnoreCase);

    private static string Normalized(string link) => link.Trim().TrimEnd('/');

    /// <summary>A name for a page that gave no title: its host, without <c>www.</c>, or the
    /// link itself when it is no address.</summary>
    private static string HostOf(string page)
    {
        if (!Uri.TryCreate(page, UriKind.Absolute, out var uri) || uri.Host.Length == 0) return page;

        return uri.Host.StartsWith("www.", StringComparison.OrdinalIgnoreCase) ? uri.Host[4..] : uri.Host;
    }
}
