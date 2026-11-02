using Backlog.Modules.RepositoryManagement.Abstractions.DataTransferObjects;
using Backlog.Modules.RepositoryManagement.Abstractions.Services;
using Backlog.Modules.RepositoryManagement.Services;
using Backlog.SharedKernel.Handlers;
using Backlog.SharedKernel.Results;

namespace Backlog.Modules.RepositoryManagement.Features.RerunFailed;

/// <summary>Run the failed GitHub Actions runs behind the head commit's failed checks
/// again.</summary>
internal sealed record RerunFailedCommand(PullRequestReference PullRequest);

/// <summary>Asks the source to re-run every failed Actions run — each once, all of them,
/// a partial refusal said with how many went through — then reads the repository
/// again.</summary>
internal sealed class RerunFailedCommandHandler(PullRequestActs acts, IPullRequestSource source) : ICommandHandler<RerunFailedCommand, Result>
{
    public Task<Result> Handle(RerunFailedCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        return acts.RunAsync(command.PullRequest, source.RerunFailedAsync, cancellationToken);
    }
}
