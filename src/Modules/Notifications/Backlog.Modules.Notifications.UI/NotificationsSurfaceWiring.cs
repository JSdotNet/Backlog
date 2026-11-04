using global::Backlog.Modules.Notifications.Abstractions;

namespace Backlog.Modules.Notifications.UI;

/// <summary>
/// What the notification centre reads from the window it is shown in that only the host can
/// answer (local ADR 0043 §1, §3): the window's repository scope, which repository each
/// notification is about, and the hue a repository wears.
/// <para>
/// One per window, with the window's view state, filled by the host from the window's own
/// state, so a centre popped out into a window of its own follows that window's scope. This
/// project sees neither Sessions, Tasks nor Devbook; the host joins them behind
/// <see cref="RepositoriesOf"/>. A window whose host fills nothing lists every notification,
/// as the Sessions surface does.
/// </para>
/// </summary>
public sealed class NotificationsSurfaceWiring
{
    /// <summary>The repositories in the window's scope, by alias, the Brain as
    /// <c>BrainScope.Value</c>; empty for all.</summary>
    public Func<IReadOnlyList<string>>? RepositoryScope { get; set; }

    /// <summary>
    /// The repository each notification is about, by id: its alias, or null for one filed
    /// against no repository — the Brain's. A notification left out of the answer could not be
    /// worked out, and the centre shows it under every scope.
    /// </summary>
    public Func<IReadOnlyList<NotificationView>, CancellationToken, Task<IReadOnlyDictionary<Guid, string?>>>? RepositoriesOf { get; set; }

    /// <summary>Which identity hue a repository wears, by alias, or null.</summary>
    public Func<string?, int?>? RepositoryColour { get; set; }
}
