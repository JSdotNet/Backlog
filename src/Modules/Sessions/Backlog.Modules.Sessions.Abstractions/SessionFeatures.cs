namespace Backlog.Modules.Sessions.Abstractions;

/// <summary>
/// The feature keys the Sessions context owns.
/// <para>
/// The key itself is unchanged — <c>"sessions"</c> — and that is deliberate. It
/// briefly lived on <c>DevPcFeatures</c>, because the session list was first built
/// as a second surface on Dev PC Management. It is its own bounded context now, so
/// the key moved to the context that owns it; keeping the string means nobody's
/// settings file forgets that they had switched the area off. The same move
/// <c>DashboardFeatures</c> records, for the same reason.
/// </para>
/// <para>
/// Only the Shell gates on this today, because the area is toggled from the app
/// chrome rather than from inside the pane. That makes it tempting to file the key
/// with the Shell's own — but the Shell is asking a question about this context, and
/// the day the pane wants to gate on it too the key would have to move out of a
/// place nothing below the Shell may read. It sits with the context it is a feature
/// of instead.
/// </para>
/// </summary>
public static class SessionFeatures
{
    /// <summary>List the Claude and Copilot sessions an environment has a record of,
    /// grouped by environment or by agent.
    /// <para>
    /// One key for the whole area rather than one per column or per grouping. The
    /// repository's guidance asks for coarse-grained flags over scattered low-level
    /// toggles, and the two things this gates — whether the header offers the
    /// surface, and whether the surface may be shown — are the same question asked
    /// twice.
    /// </para></summary>
    public const string Sessions = "sessions";
}
