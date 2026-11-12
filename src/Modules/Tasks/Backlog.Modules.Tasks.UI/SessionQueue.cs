using Backlog.Modules.Tasks.Abstractions.Services;

namespace Backlog.Desktop.UI.Tasks;

/// <summary>
/// One open session request as the filter bar's session queue lists it
/// (<c>.devbook/domain/sessions/features.md#start-a-session-from-an-entry</c>): what is
/// waiting, where, and how it is reached.
/// </summary>
/// <param name="Request">The request itself: its state, repository and when it was made.</param>
/// <param name="EntryId">The entry it was made for, or null for a plan's.</param>
/// <param name="PlanTag">The plan's tag without its sigil, or null for an entry's.</param>
/// <param name="Name">The entry's title or the plan's <c>+tag</c>; for an entry the
/// loaded rows do not hold, <see cref="SessionQueue.NotInView"/>.</param>
/// <param name="InView">Whether the row can be jumped to: the entry is among the loaded
/// rows, or the plan has a chip on the bar.</param>
public sealed record SessionQueueRow(
    EntrySessionRequest Request,
    Guid? EntryId,
    string? PlanTag,
    string Name,
    bool InView)
{
    /// <summary>Whether the row offers Cancel: only while no dispatcher has claimed it.</summary>
    public bool CanCancel => Request.State is EntrySessionRequestState.Requested;

    /// <summary>The row's stable key, for <c>@key</c> and test ids.</summary>
    public string Key => EntryId is { } id ? $"entry-{id:n}" : $"plan-{PlanTag}";
}

/// <summary>
/// The session queue's words and order: every open request, entry and plan, oldest
/// first, named from what the pane already holds — the Sessions record carries no entry
/// text (local ADR 0038 §1), so a title is joined here and never travels back.
/// </summary>
public static class SessionQueue
{
    /// <summary>The name of an entry request whose entry the loaded rows do not hold —
    /// another repository scope, or a deleted entry.</summary>
    public const string NotInView = "Entry not in this view";

    /// <summary>The name of a loaded entry with no title yet.</summary>
    public const string Untitled = "Untitled entry";

    /// <summary>The remedy the dispatcher warning names.</summary>
    public const string StartDispatcher = "/loop /backlog-tools:dispatch-sessions";

    /// <summary>The queue's rows, oldest first; ties keep entries before plans and then
    /// the name, so a redraw never reorders two requests made in the same instant.</summary>
    /// <param name="entries">Open entry requests, by entry id.</param>
    /// <param name="plans">Open plan requests, by bare tag.</param>
    /// <param name="titleOf">The loaded entry's title, or null when no loaded row has the id.</param>
    /// <param name="planOnBar">Whether a plan, by bare tag, has a chip on the bar.</param>
    public static IReadOnlyList<SessionQueueRow> Rows(
        IReadOnlyDictionary<Guid, EntrySessionRequest> entries,
        IReadOnlyDictionary<string, EntrySessionRequest> plans,
        Func<Guid, string?> titleOf,
        Func<string, bool> planOnBar)
    {
        ArgumentNullException.ThrowIfNull(entries);
        ArgumentNullException.ThrowIfNull(plans);
        ArgumentNullException.ThrowIfNull(titleOf);
        ArgumentNullException.ThrowIfNull(planOnBar);

        var rows = new List<SessionQueueRow>(entries.Count + plans.Count);

        foreach (var (entryId, request) in entries)
        {
            var title = titleOf(entryId);
            rows.Add(new SessionQueueRow(
                request,
                entryId,
                PlanTag: null,
                title is null ? NotInView : string.IsNullOrWhiteSpace(title) ? Untitled : title,
                InView: title is not null));
        }

        foreach (var (tag, request) in plans)
        {
            rows.Add(new SessionQueueRow(request, EntryId: null, tag, $"+{tag}", planOnBar(tag)));
        }

        return rows
            .OrderBy(row => row.Request.RequestedAt)
            .ThenBy(row => row.EntryId is null)
            .ThenBy(row => row.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>The trigger's name, the count in words: "Session queue, 3 waiting", or
    /// "Session queue, empty".</summary>
    public static string TriggerLabel(int count) =>
        count <= 0 ? "Session queue, empty" : $"Session queue, {count} waiting";

    /// <summary>The state in the waiting chip's own words.</summary>
    public static string StateLabel(EntrySessionRequestState state) => state switch
    {
        EntrySessionRequestState.Claimed => "Claimed",
        EntrySessionRequestState.Started => "Started",
        _ => "Requested"
    };

    /// <summary>How long ago <paramref name="at"/> was, coarsely: "just now", "4 min ago",
    /// "2 h ago", "3 d ago". A moment ahead of the clock reads "just now".</summary>
    public static string Age(DateTimeOffset at, DateTimeOffset now)
    {
        var elapsed = now - at;

        if (elapsed < TimeSpan.FromMinutes(1)) return "just now";
        if (elapsed < TimeSpan.FromHours(1)) return $"{(int)elapsed.TotalMinutes} min ago";
        if (elapsed < TimeSpan.FromDays(1)) return $"{(int)elapsed.TotalHours} h ago";

        return $"{(int)elapsed.TotalDays} d ago";
    }

    /// <summary>The dispatcher warning's first sentence: never heard since the app
    /// started, or when it last was.</summary>
    public static string SilenceSentence(DispatcherContactReading contact, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(contact);

        return contact.LastHeardAt is { } heard
            ? $"A dispatcher last reached Backlog {Age(heard, now)}, so nothing is starting these requests."
            : "No dispatcher has reached Backlog since it started, so nothing will start these requests.";
    }
}
