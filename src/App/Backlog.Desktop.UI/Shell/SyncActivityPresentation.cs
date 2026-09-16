using System.Globalization;

using Backlog.Infrastructure.Sync;
using Backlog.Infrastructure.Sync.Sessions;

namespace Backlog.Desktop.UI.Shell;

/// <summary>
/// What the footer reads off the two replication loops, folded into one
/// reading: task sync and session sync are two workers with two summaries, and
/// a band with room for one short phrase wants one answer.
/// <para>
/// <paramref name="Sent"/> and <paramref name="Received"/> are the counts a
/// person can act on. Received is what was <em>applied</em>, not what was
/// pulled: a device's own echo comes back every cycle and changes nothing, and
/// a footer that counted it would say the backlog moved when it did not.
/// <paramref name="At"/> is the later of the two last successes, or null when
/// neither loop has completed a cycle yet.
/// </para>
/// </summary>
public sealed record SyncFooterState(bool Syncing, int Sent, int Received, DateTimeOffset? At, string? Error)
{
    /// <summary>The two workers' current readings, folded. Either may be null on
    /// a head that composed only one of the loops.</summary>
    public static SyncFooterState From(TaskSyncWorker? tasks, SessionSyncWorker? sessions) => From(
        syncing: tasks?.IsSyncing == true || sessions?.IsSyncing == true,
        tasks?.LastSummary,
        tasks?.LastError,
        sessions?.LastSummary,
        sessions?.LastError);

    /// <summary>The fold itself, over the readings rather than the workers that
    /// hold them — the workers set their summaries privately, so this is the
    /// seam a test reaches the arithmetic through.</summary>
    public static SyncFooterState From(
        bool syncing,
        TaskSyncSummary? tasks,
        string? taskError,
        SessionSyncSummary? sessions,
        string? sessionError)
    {
        var sent = (tasks?.Pushed ?? 0) + (sessions?.Pushed ?? 0);
        var received = (tasks?.Applied ?? 0) + (sessions?.Applied ?? 0);

        DateTimeOffset? at = (tasks?.At, sessions?.At) switch
        {
            ({ } a, { } b) => a > b ? a : b,
            ({ } a, null) => a,
            (null, { } b) => b,
            _ => null,
        };

        // Either loop failing is the band's business: the workers clear their
        // error on the next success, so this only outlives the failure itself.
        return new SyncFooterState(syncing, sent, received, at, taskError ?? sessionError);
    }

    /// <summary>True when there is anything at all to draw. Before the first
    /// cycle there is not, and a band that said "Sync" with nothing behind it
    /// would be a control that opens an empty window.</summary>
    public bool HasSomethingToShow => Syncing || At is not null || Error is not null;
}

/// <summary>The words the footer and its window use for replication. Kept out
/// of the component so a test can read them without rendering, and so the two
/// surfaces cannot drift apart on a phrase.</summary>
public static class SyncActivityPresentation
{
    /// <summary>The footer's phrase, or null when there is nothing to say.
    /// Arrows rather than words on the band because the band is one line and
    /// the direction is the whole point; the accessible name below says the
    /// same thing in words.</summary>
    public static string? Label(SyncFooterState state)
    {
        ArgumentNullException.ThrowIfNull(state);

        if (state.Syncing) return "Syncing…";
        if (state.Error is not null) return "Sync failed";
        if (state.At is { } at) return $"Synced ↑{state.Sent} ↓{state.Received} · {Clock(at)}";

        return null;
    }

    /// <summary>What a screen reader is told about the control, including that
    /// it opens the window. Every state says the same thing the label does, in
    /// words the arrows stand for.</summary>
    public static string ControlLabel(SyncFooterState state)
    {
        ArgumentNullException.ThrowIfNull(state);

        const string opens = "Open sync activity";

        if (state.Syncing) return $"Syncing with the cloud. {opens}";
        if (state.Error is not null) return $"Sync failed: {state.Error} {opens}";
        if (state.At is { } at) return $"Synced at {Clock(at)}: {state.Sent} sent, {state.Received} received. {opens}";

        return opens;
    }

    /// <summary>One summary line per loop, for the window. Null for a loop the
    /// host did not compose or that has not run yet.</summary>
    public static string? TaskLine(TaskSyncSummary? summary) => summary is null
        ? null
        : $"Tasks: sent {summary.Pushed}, received {summary.Applied} of {summary.Pulled} pulled"
            + (summary.Skipped > 0 ? $", {summary.Skipped} unreadable by this build" : string.Empty)
            + $" · {Clock(summary.At)}";

    /// <inheritdoc cref="TaskLine"/>
    public static string? SessionLine(SessionSyncSummary? summary) => summary is null
        ? null
        : $"Sessions: sent {summary.Pushed}, received {summary.Applied} of {summary.Pulled} pulled · {Clock(summary.At)}";

    /// <summary>The direction as a person reads it in a list: arrow first so
    /// the column scans, word second so it is not colour or glyph alone.</summary>
    public static string Direction(SyncDirection direction) => direction switch
    {
        SyncDirection.Sent => "↑ Sent",
        SyncDirection.Received => "↓ Received",
        _ => direction.ToString(),
    };

    public static string Kind(SyncItemKind kind) => kind switch
    {
        SyncItemKind.Task => "Task",
        SyncItemKind.Capture => "Capture",
        SyncItemKind.Session => "Session",
        _ => kind.ToString(),
    };

    /// <summary>Hours and minutes in the reader's own clock and culture. The
    /// footer has room for no more, and a date would say nothing new about a
    /// cycle that runs every five minutes.</summary>
    public static string Clock(DateTimeOffset at) =>
        at.ToLocalTime().ToString("t", CultureInfo.CurrentCulture);

    /// <summary>With seconds, for the list, where two entries a cycle apart
    /// would otherwise read as the same moment.</summary>
    public static string Stamp(DateTimeOffset at) =>
        at.ToLocalTime().ToString("T", CultureInfo.CurrentCulture);
}
