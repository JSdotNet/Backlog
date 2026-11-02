using Backlog.Modules.RepositoryManagement.Abstractions;
using Backlog.Modules.RepositoryManagement.Abstractions.DataTransferObjects;
using Backlog.Modules.RepositoryManagement.Abstractions.Services;
using Backlog.SharedKernel.Results;

namespace Backlog.Modules.RepositoryManagement.Services;

/// <summary>
/// What every pull-request command does around its one call to GitHub (local ADR 0042
/// §3): refuse an archived repository's pull request with no call at all, act on the
/// status kept for it — reading it first when none is kept — then read its repository
/// again, whether the act went through or was refused, so every reader sees the new
/// state, and say so through <paramref name="changed"/>.
/// <para>
/// The re-read after a refusal is what corrects a reader that drew a pull request
/// GitHub had already moved on from; after a success it is what shows the result.
/// Once GitHub has been asked, the change is said even when the caller leaves while the
/// re-read is out: the act was made, and every other reader has to hear of it.
/// </para>
/// </summary>
internal sealed class PullRequestActs(PullRequestCache cache, IRegisteredRepositories registry, Action<PullRequestsChanged> changed)
{
    public async Task<Result> RunAsync(
        PullRequestReference pullRequest,
        Func<PullRequestStatus, CancellationToken, Task<Result>> act,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(pullRequest);
        ArgumentNullException.ThrowIfNull(act);

        if (!registry.IsConfigured) return Result.Failure(PullRequestErrors.NotConfigured);

        var acted = pullRequest with { RepositoryFullName = pullRequest.RepositoryFullName.Trim() };
        if (registry.IsArchived(acted.RepositoryFullName)) return Result.Failure(PullRequestErrors.Archived(acted));

        var status = cache.KeptStatus(acted);
        if (status is null)
        {
            var read = await cache.ReadAsync([acted], PullRequestReadOptions.FreshFor, cancellationToken).ConfigureAwait(false);
            status = read.PullRequests.FirstOrDefault();

            if (status is null)
            {
                return Result.Failure(read.Failures.FirstOrDefault() is { } failure
                    ? PullRequestErrors.Refused(failure.Message)
                    : PullRequestErrors.NotFound(acted));
            }
        }

        var result = await act(status, cancellationToken).ConfigureAwait(false);

        try
        {
            await cache.RereadAfterActAsync(acted, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // The transport gave up on the re-read, not the caller: GitHub has already
            // acted, so the caller hears what it did.
        }
        finally
        {
            changed(new PullRequestsChanged([acted.RepositoryFullName], [acted]));
        }

        return result;
    }
}
