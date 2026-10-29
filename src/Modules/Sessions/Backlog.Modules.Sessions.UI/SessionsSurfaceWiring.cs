using Backlog.Modules.Sessions.Abstractions;

namespace Backlog.Modules.Sessions.UI;

/// <summary>
/// What the Sessions surface reads from the window it is shown in that only the host
/// can answer (local ADR 0041 §1, §3): the window's repository scope, what a recorded
/// repository is called and the hue it wears, the Backlog entry that linked a session,
/// how a pull request stands, and how an entry is opened.
/// <para>
/// One per window, with the window's view state, filled by the host from the window's
/// own state. Opening an entry is a jump to the Tasks surface through the window's
/// navigator, so it lands in the window it was pressed in. A window whose host fills
/// nothing lists every session, with no entry links and no pull request chips.
/// </para>
/// </summary>
public sealed class SessionsSurfaceWiring
{
    /// <summary>The repositories in the window's scope, by alias; empty for all.</summary>
    public Func<IReadOnlyList<string>>? RepositoryScope { get; set; }

    /// <summary>What a recorded repository is called here, or null for one this
    /// workspace does not know.</summary>
    public Func<string?, string?>? RepositoryAlias { get; set; }

    /// <summary>Which identity hue a repository wears, or null.</summary>
    public Func<string?, int?>? RepositoryColour { get; set; }

    /// <summary>Opens a Backlog entry; null renders task references as inert text.</summary>
    public Func<DeliveryRunReference, Task>? OpenTask { get; set; }

    /// <summary>The Backlog entry that linked a session, by the session's id.</summary>
    public Func<string, DeliveryRunReference?>? SessionTask { get; set; }

    /// <summary>How a pull request a session leads to stands. Read only while the
    /// catalog offers the pull requests surface.</summary>
    public Func<DeliveryRunReference, Task<SessionPullRequestStatus?>>? PullRequestStatus { get; set; }
}
