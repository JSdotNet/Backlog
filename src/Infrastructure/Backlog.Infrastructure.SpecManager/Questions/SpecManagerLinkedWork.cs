using Backlog.Modules.Sessions.Abstractions;
using Backlog.Modules.Tasks.Abstractions.Connectors;
using Backlog.Modules.Tasks.Abstractions.DataTransferObjects;
using Backlog.Modules.Tasks.Abstractions.Services;

using Microsoft.Extensions.DependencyInjection;

namespace Backlog.Infrastructure.SpecManager.Questions;

/// <summary>
/// The linked task a spec-manager item became, and the one agent session it names — what
/// a question's subject and its "Via" are read from.
/// </summary>
/// <param name="TaskId">The linked task's id, the subject's id.</param>
/// <param name="SessionTitle">The title of the agent session the task links, when its
/// <c>session</c> projection refs name exactly one session and the session record holds
/// it; null otherwise.</param>
internal sealed record SpecManagerLinkedWork(Guid TaskId, string? SessionTitle);

/// <summary>Finds the <see cref="SpecManagerLinkedWork"/> of an item.</summary>
internal interface ISpecManagerLinkedWorkLookup
{
    /// <summary>The linked task item <paramref name="itemId"/> became, or null when no live
    /// task follows it — a product not connected here, an item the sync skipped.</summary>
    Task<SpecManagerLinkedWork?> FindAsync(string itemId, CancellationToken cancellationToken);
}

/// <summary>
/// <see cref="ISpecManagerLinkedWorkLookup"/> over the Tasks context's published
/// <see cref="ITaskItems"/> and, where a host composes one, the Sessions context's
/// <see cref="IAgentSessionSource"/>. An adapter may see both; the id it asks for is the
/// one the sync gave the task (<see cref="LinkedTaskIds"/>), so nothing is searched. The
/// session record is read once a minute at most, since a pull may publish several
/// questions of one session's item in a row.
/// </summary>
internal sealed class SpecManagerLinkedWorkLookup(IServiceScopeFactory scopes, TimeProvider time) : ISpecManagerLinkedWorkLookup
{
    /// <summary>How long one reading of the session record answers for.</summary>
    internal static readonly TimeSpan SessionsFreshFor = TimeSpan.FromMinutes(1);

    private (DateTimeOffset At, IReadOnlyList<AgentSession> Sessions)? _sessions;

    public async Task<SpecManagerLinkedWork?> FindAsync(string itemId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(itemId)) return null;

        var taskId = LinkedTaskIds.For(SpecManagerConnector.ConnectorId, itemId);

        var scope = scopes.CreateAsyncScope();
        await using var _ = scope.ConfigureAwait(false);
        if (scope.ServiceProvider.GetService<ITaskItems>() is not { } tasks) return null;

        var task = await tasks.GetAsync(taskId, cancellationToken).ConfigureAwait(false);
        if (task is null) return null;

        var sessions = task.Projections
            .Where(projection => string.Equals(projection.TargetType, EntryProjectionDto.SessionTargetType, StringComparison.Ordinal))
            .Select(projection => projection.ExternalId)
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        return new SpecManagerLinkedWork(
            task.Id,
            sessions.Count == 1 ? await SessionTitleAsync(scope.ServiceProvider, sessions[0], cancellationToken).ConfigureAwait(false) : null);
    }

    /// <summary>What the session is called where it is set — the desktop app's title, else
    /// the record's — or null where no record holds it or the record cannot be read: the
    /// "Via" then says "Agent session" alone.</summary>
    private async Task<string?> SessionTitleAsync(IServiceProvider services, string sessionId, CancellationToken cancellationToken)
    {
        if (services.GetService<IAgentSessionSource>() is not { } source) return null;

        try
        {
            var now = time.GetUtcNow();
            if (_sessions is not { } read || now - read.At >= SessionsFreshFor)
            {
                var catalog = await source.GetSessionsAsync(AgentSessionQuery.Newest.TimestampsOnly(), cancellationToken).ConfigureAwait(false);
                read = (now, catalog.Sessions);
                _sessions = read;
            }

            var session = read.Sessions.FirstOrDefault(candidate => string.Equals(candidate.Id, sessionId, StringComparison.OrdinalIgnoreCase));

            return session is null ? null
                : !string.IsNullOrWhiteSpace(session.DesktopTitle) ? session.DesktopTitle.Trim()
                : !string.IsNullOrWhiteSpace(session.Title) ? session.Title.Trim()
                : null;
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            return null;
        }
    }
}
