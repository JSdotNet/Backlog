using Backlog.Infrastructure.Capture.Feeds;
using Backlog.Modules.Capture.Abstractions;
using Backlog.Modules.Capture.Abstractions.DataTransferObjects;
using Backlog.Modules.Capture.Ports;

namespace Backlog.Infrastructure.Capture.EventPages;

/// <summary>
/// Answers <see cref="ICaptureSourceAdapter"/> for a page where an organiser
/// announces their events (<c>.devbook/domain/capture/features.md#event-page-monitor</c>):
/// each upcoming edition on it becomes an entry carrying its dates and place.
/// <para>
/// Each target is read by fixed rules, stopping at the first step that gives an
/// edition still to come:
/// </para>
/// <list type="number">
/// <item>a schema.org event in the page's structured data
/// (<see cref="SchemaOrgEvents"/>);</item>
/// <item>one hop to a registration link — Eventbrite, or a link whose text says
/// register, registreer or tickets (<see cref="EventPageHtml.RegistrationLinkIn"/>)
/// — and the schema.org event on that page;</item>
/// <item>a date pattern in the page text, English or Dutch month names
/// (<see cref="PageDates"/>);</item>
/// <item>no date at all: the page arrives with its link, so the person can look.</item>
/// </list>
/// <para>
/// A step that reads only passed editions — last edition's event still on the
/// page, or the listing it still links — does not stop the read: the page text
/// may already name the next date. Only when no step finds an edition to come
/// does the target say the edition passed, naming the latest end any step read.
/// </para>
/// <para>
/// No AI model reads a page, and nothing but the feed fetcher is asked: the
/// same page gives the same entries, which is what lets a run know an edition
/// it delivered before. A hop that fails is not the target's failure — the
/// page text may still say the date — and is only mentioned when nothing else
/// was found either.
/// </para>
/// <para>
/// "Today", for which editions have passed, is the reader's local day: an
/// edition that ends today has not passed for the person looking at it,
/// wherever UTC's day has got to.
/// </para>
/// </summary>
internal sealed class EventPageAdapter(FeedFetcher fetcher, TimeProvider clock) : ICaptureSourceAdapter
{
    public CaptureSourceKind Kind => CaptureSourceKind.EventPage;

    public Task<CaptureSourceFindings> RunAsync(MonitoredSource source, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);

        return SourceTargets.ReadAllAsync(source, ReadTargetAsync, cancellationToken);
    }

    private async Task<TargetReading> ReadTargetAsync(string target, CancellationToken cancellationToken)
    {
        var typed = SourceTargets.ParseUrl(target);
        var page = await fetcher.GetAsync(typed, cancellationToken).ConfigureAwait(false);
        var html = page.Text;
        var watched = new WatchedPage(EventPageEntries.PageId(typed), EventPageHtml.TitleOf(html));
        var today = DateOnly.FromDateTime(clock.GetLocalNow().DateTime);

        var onPage = SchemaOrgEvents.In(html);

        if (EventPageEntries.FromSchema(onPage, page.Url, watched.Url, watched, today) is { } fromPage) return fromPage;

        var lastPassed = EventPageEntries.LastDayOf(onPage);
        string? hopFailure = null;

        if (EventPageHtml.RegistrationLinkIn(html, page.Url) is { } registration)
        {
            try
            {
                var listing = await fetcher.GetAsync(registration, cancellationToken).ConfigureAwait(false);
                var listed = SchemaOrgEvents.In(listing.Text);

                if (EventPageEntries.FromSchema(listed, listing.Url, listing.Url.AbsoluteUri, watched, today) is { } fromListing) return fromListing;

                lastPassed = Later(lastPassed, EventPageEntries.LastDayOf(listed));
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                // The page's own text is still to be read; this is only worth
                // a word if that finds nothing either.
                hopFailure = $"its registration page {registration.AbsoluteUri} could not be read: {ex.Message.TrimEnd('.')}";
            }
        }

        var inText = PageDates.In(EventPageHtml.TextOf(html));

        if (EventPageEntries.FromText(inText, watched, today) is { } fromText) return fromText;

        // A date was read, only every one had passed: the reader is owed the
        // reason nothing arrived, not a dateless item for a page that has dates.
        // A failed hop is named beside it only when the text had no date
        // either, since then the next edition may be behind that link.
        if (Later(lastPassed, EventPageEntries.LastDayOf(inText)) is { } ended)
        {
            return EventPageEntries.Passed(ended, inText.Count == 0 ? hopFailure : null);
        }

        return new TargetReading([EventPageEntries.Undated(watched)], hopFailure);
    }

    private static DateOnly? Later(DateOnly? one, DateOnly? other) =>
        one is null ? other : other is null ? one : one > other ? one : other;
}
