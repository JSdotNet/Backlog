namespace Backlog.Desktop.UI.Shell;

/// <summary>
/// The main view the workspace shows, picked by the header's view switch.
/// <para>
/// Exactly one is always on screen while the workspace is: a view is never closed,
/// only replaced by another. That is the difference from a
/// <see cref="WorkspaceSurface"/> takeover, which hides the workspace altogether and
/// which a second press — or, but for the Inbox, Escape — closes. The Tasks pane
/// used to be one of the side panes and the roadmap one of the takeovers; both are
/// views now. The Inbox and the Devbook were side panes beside the view and are
/// takeovers now, opened over whichever view is showing, which waits underneath.
/// </para>
/// <para>
/// The shell stores the member's name as <c>lastView</c> in
/// <c>shell-navigation.json</c>, so a name here is a stored value. Board, Calendar
/// and Goals — once In progress — join as members of their own, each one option in the switch.
/// </para>
/// </summary>
internal enum ShellView
{
    /// <summary>The task list and its details — the view the shell opens on when
    /// nothing else was remembered.</summary>
    Tasks,

    /// <summary>The Roadmap context's plans on a graduated axis, under its own
    /// Planning heading row. Offered while <c>RoadmapFeatures.Roadmap</c> is on.</summary>
    Roadmap,

    /// <summary>The filtered tasks on a month grid by their due date, under the
    /// Tasks filter bar and beside the same detail panel. Its option sits after
    /// Tasks and Board in the switch; the members' order is not the options'.</summary>
    Calendar,

    /// <summary>The filtered tasks in columns of cards — by status, plan,
    /// repository or priority — under the Tasks filter bar and beside the same
    /// detail panel. Its option sits right after Tasks in the switch; the
    /// members' order is not the options'.</summary>
    Board,

    /// <summary>The work in progress group's own view, first in that group rather
    /// than in the task views' switch: it answers "what is moving", which is that
    /// group's question. It is the Goals view (local ADR 0035 §10), always offered.
    /// <para>
    /// Stored as <c>Goals</c>. It was <c>InProgress</c> until the Goals rename, and a
    /// file that still holds that name reads as this member through
    /// <see cref="ShellViews.Read"/>. Appended, because the name is stored.
    /// </para></summary>
    Goals
}

/// <summary>Reads a stored <see cref="ShellView"/> name back.</summary>
internal static class ShellViews
{
    /// <summary>The name <see cref="ShellView.Goals"/> was stored under before the
    /// Goals rename.</summary>
    internal const string InProgressName = "InProgress";

    /// <summary>
    /// The view <paramref name="stored"/> names, or <paramref name="fallback"/> for a
    /// missing or unknown name. Only member names count, never numbers, and the
    /// retired <c>InProgress</c> reads as <see cref="ShellView.Goals"/>.
    /// </summary>
    internal static ShellView Read(string? stored, ShellView fallback)
    {
        if (string.Equals(stored, InProgressName, StringComparison.Ordinal)) return ShellView.Goals;

        return !string.IsNullOrEmpty(stored)
            && char.IsLetter(stored[0])
            && Enum.TryParse<ShellView>(stored, out var value)
            && Enum.IsDefined(value)
                ? value
                : fallback;
    }
}
