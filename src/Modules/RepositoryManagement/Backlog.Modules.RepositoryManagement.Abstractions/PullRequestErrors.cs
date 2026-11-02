using Backlog.Modules.RepositoryManagement.Abstractions.DataTransferObjects;
using Backlog.SharedKernel.Results;

namespace Backlog.Modules.RepositoryManagement.Abstractions;

/// <summary>
/// The errors a pull-request command fails with (local ADR 0042 §3). Each carries a
/// sentence a person can read as it stands; the code is for a caller that has to tell
/// them apart.
/// </summary>
public static class PullRequestErrors
{
    public const string RefusedCode = "pull-requests.refused";
    public const string ArchivedCode = "pull-requests.archived";
    public const string NotFoundCode = "pull-requests.not-found";
    public const string NotConfiguredCode = "pull-requests.not-configured";

    /// <summary>GitHub refused the act, in the words the source already chose.</summary>
    public static Error Refused(string message) => new(RefusedCode, message, ErrorType.Failure);

    /// <summary>The pull request's repository is archived, so nothing is sent for it
    /// (local ADR 0029).</summary>
    public static Error Archived(PullRequestReference pullRequest)
    {
        ArgumentNullException.ThrowIfNull(pullRequest);
        return Error.Conflict(
            ArchivedCode,
            $"{pullRequest.Key} is in an archived repository, so Backlog does not act on it. Unarchive the repository in Settings first.");
    }

    /// <summary>GitHub has no such pull request.</summary>
    public static Error NotFound(PullRequestReference pullRequest)
    {
        ArgumentNullException.ThrowIfNull(pullRequest);
        return Error.NotFound(NotFoundCode, $"GitHub has no pull request {pullRequest.Key}.");
    }

    /// <summary>No repository is registered, so there is nothing to act through.</summary>
    public static Error NotConfigured { get; } = new(
        NotConfiguredCode,
        "No repository is registered. Add one in Settings to act on its pull requests.",
        ErrorType.Failure);
}
