using System.ComponentModel;

using Backlog.Modules.Sessions.Abstractions;
using Backlog.Modules.Tasks.Abstractions.Services;

using ModelContextProtocol.Server;

namespace Backlog.Infrastructure.Mcp;

/// <summary>
/// The agent sessions this machine can describe.
/// <para>
/// Its own group (<see cref="BacklogMcpTools.Sessions"/>) because it is its own
/// switchable area.
/// </para>
/// <para>
/// <b>The repository argument is optional, and it filters on the resolved
/// repository.</b> A session carries two of them and they are not the same fact:
/// <see cref="AgentSession.Repository"/> is what the agent wrote down — Claude
/// writes nothing at all — and <see cref="AgentSession.ResolvedRepository"/> is
/// what this machine worked out by finding the working folder inside a registered
/// clone. Filtering on the recorded one would hide every Claude session on the
/// machine, running ones included, which is the outcome
/// <c>ISessionRepositoryResolver</c> exists to avoid. Both still travel in the
/// payload, apart, because they fail differently.
/// </para>
/// <para>
/// <b>The unkeyed <see cref="IAgentSessionSource"/>, and only that.</b> The
/// composition registers a composite under the plain service type and the local
/// and replicated readers under keys. Injecting
/// <c>IEnumerable&lt;IAgentSessionSource&gt;</c> would miss the keyed
/// registrations entirely and injecting a keyed one would answer with half the
/// machine, so the composite is what a session asks and what it gets.
/// </para>
/// </summary>
[McpServerToolType]
public sealed class SessionTools(IAgentSessionSource sessions, IRepositoryDirectory repositories)
{
    internal const string ListSessions = "list_sessions";

    [McpServerTool(Name = ListSessions, Title = "List agent sessions", ReadOnly = true, Idempotent = true, OpenWorld = false)]
    [Description(
        "The Claude and Copilot sessions this machine knows about, newest first, with what was dropped by the cap "
        + "and what could not be read. Optionally only those whose working folder this machine placed inside one "
        + "repository's registered clone. Read-only.")]
    public async Task<SessionsPayload> ListSessionsAsync(
        [Description(
            "Optional. The repository in owner/name form, e.g. JSdotNet/Backlog. Omit it for every session this "
            + "machine knows about.")]
        string? repository = null,
        CancellationToken cancellationToken = default)
    {
        // Resolved before the catalog is read, so an unknown repository is the
        // same refusal here as everywhere else rather than an empty list that
        // looks like "no sessions". Only when one was asked for: absent is not a
        // repository nobody registered, it is no question about repositories.
        var scope = string.IsNullOrWhiteSpace(repository)
            ? null
            : RepositoryScope.Resolve(repositories, repository).ValueOrThrow();

        var catalog = await sessions.GetSessionsAsync(cancellationToken).ConfigureAwait(false);

        var listed = scope is null
            ? catalog.Sessions
            : [.. catalog.Sessions.Where(session => string.Equals(
                session.ResolvedRepository,
                scope.Id,
                StringComparison.OrdinalIgnoreCase))];

        // Discovered and Capped are the catalog's own, never this filter's. The
        // domain rule is that a truncated list has to say so, and the truncation
        // that happened is the source's per-agent cap — which ran before anything
        // here saw a session, so a repository's sessions can be missing from a
        // capped catalog and no filtering of ours would know. Recomputing either
        // number over the filtered list would quietly turn "the cap took
        // something" into "the filter did", and lose the warning.
        return new SessionsPayload(
            scope?.Id,
            listed.Count,
            catalog.Discovered,
            catalog.Capped,
            catalog.Unreadable,
            [.. listed.Select(Projections.Session)]);
    }
}
