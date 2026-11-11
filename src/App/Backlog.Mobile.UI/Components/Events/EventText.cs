using System.Globalization;

using Backlog.Mobile.UI.Events;
using Backlog.Modules.Events.Abstractions;
using Backlog.Modules.Events.Abstractions.DataTransferObjects;

namespace Backlog.Mobile.UI.Components.Events;

/// <summary>
/// How the phone's Events screens word a knowledge event: its state, when it runs and where, in
/// the words the desktop's Events pane uses, so an event reads the same on both. Days are written
/// in the invariant culture the phone's other screens use.
/// </summary>
public static class EventText
{
    private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;

    /// <summary>The Events tab.</summary>
    public const string ListHref = "events";

    /// <summary>One event's detail.</summary>
    public static string Href(Guid id) => $"events/{id:D}";

    /// <summary>A state as the desktop pane names it.</summary>
    public static string StateLabel(KnowledgeEventState state) => state switch
    {
        KnowledgeEventState.Spotted => "Spotted",
        KnowledgeEventState.Considering => "Considering",
        KnowledgeEventState.Going => "Going",
        KnowledgeEventState.Attended => "Attended",
        KnowledgeEventState.Harvested => "Harvested",
        _ => "Skipped"
    };

    /// <summary>The status badge tone a state wears, the desktop pane's.</summary>
    public static string StateTone(KnowledgeEventState state) => state switch
    {
        KnowledgeEventState.Spotted => "draft",
        KnowledgeEventState.Considering => "ready",
        KnowledgeEventState.Going => "inprogress",
        KnowledgeEventState.Attended or KnowledgeEventState.Harvested => "done",
        _ => "archived"
    };

    /// <summary>A day in full, with its time when the desktop gave one: <c>Mon 26 Oct 2026, 08:30</c>.
    /// The time is the one typed on the desktop, as the event's programme prints it.</summary>
    public static string Day(EventDateReplicaDto day)
    {
        ArgumentNullException.ThrowIfNull(day);

        var date = day.Date.ToString("ddd d MMM yyyy", Invariant);
        return day.Time is { } time ? $"{date}, {time.ToString("HH:mm", Invariant)}" : date;
    }

    /// <summary>When the event runs, as a list row says it: a day, a range of days, or that the
    /// dates are not known yet.</summary>
    public static string When(PhoneEvent item)
    {
        ArgumentNullException.ThrowIfNull(item);

        if (item.FirstDay is not { } first) return "Date not known";
        if (item.LastDay is not { } last || last == first) return first.ToString("ddd d MMM yyyy", Invariant);

        return first.Year == last.Year
            ? $"{first.ToString("d MMM", Invariant)} – {last.ToString("d MMM yyyy", Invariant)}"
            : $"{first.ToString("d MMM yyyy", Invariant)} – {last.ToString("d MMM yyyy", Invariant)}";
    }

    /// <summary>Where the event is: its venue, online, or both; null when neither is known.</summary>
    public static string? Where(KnowledgeEventReplicaDto knowledgeEvent)
    {
        ArgumentNullException.ThrowIfNull(knowledgeEvent);

        var venue = string.IsNullOrWhiteSpace(knowledgeEvent.Venue) ? null : knowledgeEvent.Venue.Trim();

        return (venue, knowledgeEvent.Online) switch
        {
            (null, true) => "Online",
            (not null, true) => $"{venue} · also online",
            _ => venue
        };
    }

    /// <summary>A list row's second line: when, the series and where, as far as known.</summary>
    public static string Line(PhoneEvent item)
    {
        ArgumentNullException.ThrowIfNull(item);

        return string.Join(" · ", new[] { When(item), item.SeriesName, Where(item.Event) }
            .Where(part => !string.IsNullOrWhiteSpace(part)));
    }

    /// <summary>
    /// The event's link as an address the phone may hand to the browser: an absolute http or https
    /// address, else null. The link is text a person typed on a desktop, so anything else — a
    /// relative path, a <c>javascript:</c> address — is shown as words and never followed.
    /// </summary>
    public static Uri? ExternalLink(string? link) =>
        Uri.TryCreate(link?.Trim(), UriKind.Absolute, out var uri)
        && (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp)
            ? uri
            : null;

    /// <summary>What the attach picker calls an event: its title and, when known, its first day.</summary>
    public static string Option(PhoneEvent item)
    {
        ArgumentNullException.ThrowIfNull(item);

        return item.FirstDay is { } first ? $"{item.Title} · {first.ToString("d MMM yyyy", Invariant)}" : item.Title;
    }

    /// <summary>
    /// The events a note may be attached to, in the order the picker offers them: the upcoming
    /// ones first, soonest first, then the past ones, latest first. Skipped events are left off —
    /// nobody takes notes at an event they skipped — and so are <paramref name="except"/>.
    /// </summary>
    public static IReadOnlyList<PhoneEvent> AttachChoices(
        IReadOnlyList<PhoneEvent> upcoming, IReadOnlyList<PhoneEvent> held, IReadOnlySet<Guid> except)
    {
        ArgumentNullException.ThrowIfNull(upcoming);
        ArgumentNullException.ThrowIfNull(held);
        ArgumentNullException.ThrowIfNull(except);

        var first = upcoming.Select(item => item.Id).ToHashSet();
        var past = held
            .Where(item => item.State != KnowledgeEventState.Skipped && !first.Contains(item.Id))
            .OrderByDescending(item => item.LastDay)
            .ThenBy(item => item.Title, StringComparer.CurrentCultureIgnoreCase);

        return [.. upcoming.Concat(past).Where(item => !except.Contains(item.Id))];
    }
}
