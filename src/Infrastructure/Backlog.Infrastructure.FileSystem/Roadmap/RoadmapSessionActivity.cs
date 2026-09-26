namespace Backlog.Infrastructure.FileSystem.Roadmap;

/// <summary>
/// When one AI session linked to a backlog entry was at work, as the Sessions context
/// recorded it — the two instants a rollup falls back on when the entry itself carries
/// no <c>started:</c> or <c>completed:</c> date.
/// <para>
/// Its own two-field shape rather than the Sessions context's <c>AgentSession</c>, so
/// <see cref="RoadmapItemRollupBuilder"/> stays a function of exactly what it reads and
/// a test can state a session in one line.
/// </para>
/// </summary>
/// <param name="StartedAt">When the session began, or null where its agent left
/// nothing to date the start from.</param>
/// <param name="LastActivityAt">The last thing the session did — always known.</param>
public sealed record RoadmapSessionActivity(DateTimeOffset? StartedAt, DateTimeOffset LastActivityAt)
{
    /// <summary>The earliest instant this session is known to have been working.</summary>
    public DateTimeOffset EarliestAt => StartedAt is { } started && started < LastActivityAt ? started : LastActivityAt;
}
