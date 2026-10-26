namespace Backlog.Modules.Sessions.Abstractions;

/// <summary>
/// Whether a pull request is still open, as whatever reads GitHub for this host answers
/// it: the port the Sessions subject probe asks about a <c>sessions / pull-request</c>
/// subject at startup. The desktop shell answers it, because it is what holds the GitHub
/// client; a host with none composes nothing, and every pull request then reads as open.
/// </summary>
public interface ISessionPullRequestStates
{
    /// <summary>True while open or draft, false once merged or closed, and null when it
    /// cannot be told — GitHub not reachable, the repository archived or not found.</summary>
    Task<bool?> IsOpenAsync(string repository, int number, CancellationToken cancellationToken = default);
}
