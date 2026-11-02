namespace Backlog.Modules.RepositoryManagement.Abstractions;

/// <summary>
/// The feature keys the Repository Management context owns.
/// <para>
/// The key itself is unchanged — <c>"pull-requests"</c> — and that is deliberate. It
/// lived on <c>SessionFeatures</c> while the pull requests list was the Sessions
/// context's second surface. Repository Management owns pull-request state now (local
/// ADR 0042), so the key moved to the context that owns it; keeping the string means
/// nobody's settings file forgets that they had switched the list off. The same move
/// <c>DashboardFeatures</c> and <c>RoadmapFeatures</c> record, for the same reason.
/// </para>
/// </summary>
public static class RepositoryManagementFeatures
{
    /// <summary>List the open pull requests of every registered repository, and update,
    /// ready or merge them from that list.
    /// <para>
    /// Its own key rather than one shared with the session list, because the two are
    /// different screens with different costs — this one asks GitHub on every opening —
    /// and somebody can want the list of sessions without it.
    /// </para></summary>
    public const string PullRequests = "pull-requests";
}
