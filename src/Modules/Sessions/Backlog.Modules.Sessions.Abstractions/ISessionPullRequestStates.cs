namespace Backlog.Modules.Sessions.Abstractions;

/// <summary>
/// What state a pull request is in, as whatever reads GitHub for this host answers it: the
/// port the Sessions subject probe asks about a <c>sessions / pull-request</c> subject, and
/// about the pull request a <c>sessions / run</c> recorded, and the run's Personal
/// Validation question asks before it is published. The desktop shell answers it, because it
/// is what holds the GitHub client; a host with none composes nothing, and every pull request
/// then reads as open.
/// </summary>
public interface ISessionPullRequestStates
{
    /// <summary>The pull request's state, or null when it cannot be told — GitHub not
    /// reachable, the repository archived or not found.</summary>
    Task<SessionPullRequestState?> StateAsync(string repository, int number, CancellationToken cancellationToken = default);
}

/// <summary>A pull request's state as <see cref="ISessionPullRequestStates"/> reads it.</summary>
public enum SessionPullRequestState
{
    /// <summary>Open and ready for review.</summary>
    Open,

    /// <summary>Open as a draft.</summary>
    Draft,

    /// <summary>Merged: it ended.</summary>
    Merged,

    /// <summary>Closed without merging: it ended.</summary>
    Closed
}
