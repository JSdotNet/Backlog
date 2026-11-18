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
    /// <summary>Whether the row offers Cancel: while Requested, and while Started so a
    /// request whose session never called <c>link_session</c> can be cleared. Never while
    /// Claimed: a dispatcher is starting the session right then.</summary>
    public bool CanCancel => Request.State is EntrySessionRequestState.Requested or EntrySessionRequestState.Started;

    /// <summary>Whether the row can move in the queue: only while Requested, a lapsed
    /// claim included — a Claimed or Started request already has its dispatcher.</summary>
    public bool CanMove => Request.State is EntrySessionRequestState.Requested;

    /// <summary>The row's stable key, for <c>@key</c> and test ids.</summary>
    public string Key => EntryId is { } id ? $"entry-{id:n}" : $"plan-{PlanTag}";
}

/// <summary>The three ways a Requested row moves in the session queue.</summary>
public enum SessionQueueStep
{
    /// <summary>One place nearer the head.</summary>
    Up,

    /// <summary>One place further back.</summary>
    Down,

    /// <summary>To the head: the next one a dispatcher is handed.</summary>
    Top
}

/// <summary>
/// The session queue's words and order: every open request, entry and plan — the
/// Claimed and Started ones first, then the Requested ones in the order the person set
/// (local ADR 0038 §4, amended) — named from what the pane already holds. The Sessions
/// record carries no entry text (local ADR 0038 §1), so a title is joined here and never
/// travels back.
/// </summary>
public static class SessionQueue
{
    /// <summary>The name of an entry request whose entry the loaded rows do not hold —
    /// another repository scope, or a deleted entry.</summary>
    public const string NotInView = "Entry not in this view";

    /// <summary>The name of a loaded entry with no title yet.</summary>
    public const string Untitled = "Untitled entry";

    /// <summary>The queue's rows: the Claimed and Started ones first, oldest first, since
    /// a dispatcher already has them; then the Requested ones in queue order — by
    /// <paramref name="pendingOrder"/> while a move is on its way, else by each one's
    /// <see cref="EntrySessionRequest.QueuePosition"/>, and oldest first where none is
    /// known. Ties keep entries before plans and then the name, so a redraw never
    /// reorders two requests made in the same instant.</summary>
    /// <param name="entries">Open entry requests, by entry id.</param>
    /// <param name="plans">Open plan requests, by bare tag.</param>
    /// <param name="titleOf">The loaded entry's title, or null when no loaded row has the id.</param>
    /// <param name="planOnBar">Whether a plan, by bare tag, has a chip on the bar.</param>
    /// <param name="pendingOrder">The Requested requests' ids in the order a move on its
    /// way will leave them, or null when none is.</param>
    public static IReadOnlyList<SessionQueueRow> Rows(
        IReadOnlyDictionary<Guid, EntrySessionRequest> entries,
        IReadOnlyDictionary<string, EntrySessionRequest> plans,
        Func<Guid, string?> titleOf,
        Func<string, bool> planOnBar,
        IReadOnlyList<Guid>? pendingOrder = null)
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

        int? Pending(SessionQueueRow row)
        {
            if (pendingOrder is null) return null;

            for (var index = 0; index < pendingOrder.Count; index++)
            {
                if (pendingOrder[index] == row.Request.Id) return index;
            }

            return null;
        }

        int? Place(SessionQueueRow row) => Pending(row) ?? row.Request.QueuePosition;

        return rows
            .OrderBy(row => row.CanMove)
            .ThenBy(row => row.CanMove && Place(row) is null)
            .ThenBy(row => row.CanMove ? Place(row) : null)
            .ThenBy(row => row.Request.RequestedAt)
            .ThenBy(row => row.EntryId is null)
            .ThenBy(row => row.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>The Requested rows' request ids, in the order <paramref name="rows"/>
    /// lists them: what a move counts positions in.</summary>
    public static IReadOnlyList<Guid> Waiting(IReadOnlyList<SessionQueueRow> rows)
    {
        ArgumentNullException.ThrowIfNull(rows);

        return [.. rows.Where(row => row.CanMove).Select(row => row.Request.Id)];
    }

    /// <summary>What the live region says once a move landed: "Moved to position 2 of 3."
    /// Both numbers count the Requested rows only, from 1.</summary>
    public static string MovedAnnouncement(int position, int count) => $"Moved to position {position} of {count}.";

    /// <summary>The words a Move control is named by, the row's name in them:
    /// "Move +gtd-para up", "Move +gtd-para down", "Move +gtd-para to the top".</summary>
    public static string MoveLabel(SessionQueueStep step, string name) => step switch
    {
        SessionQueueStep.Up => $"Move {name} up",
        SessionQueueStep.Down => $"Move {name} down",
        _ => $"Move {name} to the top"
    };

    /// <summary>Where <paramref name="step"/> takes the row at <paramref name="from"/>
    /// among <paramref name="count"/> Requested rows, or null at the edge it cannot pass.</summary>
    public static int? MoveTarget(SessionQueueStep step, int from, int count) => step switch
    {
        SessionQueueStep.Up when from > 0 => from - 1,
        SessionQueueStep.Down when from >= 0 && from < count - 1 => from + 1,
        SessionQueueStep.Top when from > 0 => 0,
        _ => null
    };

    /// <summary>The trigger's name, the count in words and the pause when there is one:
    /// "Session queue, 3 waiting", "Session queue, paused, 3 waiting", or
    /// "Session queue, empty".</summary>
    public static string TriggerLabel(int count, bool paused = false) =>
        $"Session queue, {(paused ? "paused, " : string.Empty)}{(count <= 0 ? "empty" : $"{count} waiting")}";

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

/// <summary>One long-running agent a person starts under <c>/loop</c> in a pinned session.</summary>
/// <param name="Command">What the person types, verbatim — what the copy puts on the clipboard.</param>
/// <param name="Name">What the popover calls it.</param>
/// <param name="Purpose">One line on what it does and where it runs.</param>
public sealed record LongRunningAgent(string Command, string Name, string Purpose);

/// <summary>
/// The commands that start the long-running agents of the <c>backlog-tools</c> plugin, each
/// the <c>/loop</c> line its own skill names. One set, so the session queue's agents group and
/// the dispatcher warning cannot drift apart, and a skill renamed changes one place.
/// </summary>
public static class LongRunningAgents
{
    /// <summary>The Manager session that starts what this queue holds (local ADR 0038).</summary>
    public const string DispatchSessions = "/loop /backlog-tools:dispatch-sessions";

    /// <summary>The agent that tends the Brain folder (local ADR 0026).</summary>
    public const string TendBrain = "/loop /backlog-tools:tend-brain";

    /// <summary>The agent that keeps the goals current (local ADR 0035).</summary>
    public const string TrackGoals = "/loop /backlog-tools:track-goals";

    /// <summary>Every agent, the dispatcher first: it is the one this queue needs.</summary>
    public static IReadOnlyList<LongRunningAgent> All { get; } =
    [
        new(DispatchSessions, "Session dispatcher", "Starts the sessions this queue holds."),
        new(TendBrain, "Brain", "Files what lands in the Brain's inbox. Run it from the Brain folder, or pass its path."),
        new(TrackGoals, "Goals", "Keeps the goals current and reports each one's next step.")
    ];
}
