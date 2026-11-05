using Backlog.Modules.Notifications.Abstractions;

namespace Backlog.Infrastructure.Sync.Notifications;

/// <summary>
/// PORT — which repository each notification this owner pushes is about (local ADR 0039 §6),
/// worked out by the rule the owner's notification centre keeps for its header scope. The
/// desktop composes it over its shell's resolution, where the Sessions, Tasks and Devbook
/// vocabularies meet; this project sees only the answer, so the exchange never reaches into
/// those contexts.
/// <para>
/// Optional by construction: a head that composes none pushes every notification as "not
/// known", which a viewer keeps under every repository choice.
/// </para>
/// </summary>
public interface INotificationRepositorySource
{
    /// <summary>
    /// The repository of each of <paramref name="notifications"/> that could be worked out, by
    /// id: its alias, or null for the Brain's. A notification left out of the answer is not
    /// known and is pushed with no repository. Called from the background exchange, never from
    /// a window's thread.
    /// </summary>
    Task<IReadOnlyDictionary<Guid, string?>> ResolveAsync(
        IReadOnlyList<NotificationSnapshot> notifications,
        CancellationToken cancellationToken = default);
}
