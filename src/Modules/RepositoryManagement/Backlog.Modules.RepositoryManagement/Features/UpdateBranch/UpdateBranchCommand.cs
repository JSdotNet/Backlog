using Backlog.Modules.RepositoryManagement.Abstractions.DataTransferObjects;
using Backlog.Modules.RepositoryManagement.Abstractions.Services;
using Backlog.Modules.RepositoryManagement.Services;
using Backlog.SharedKernel.Handlers;
using Backlog.SharedKernel.Results;

namespace Backlog.Modules.RepositoryManagement.Features.UpdateBranch;

/// <summary>Bring the pull request's branch up to date with its base.</summary>
internal sealed record UpdateBranchCommand(PullRequestReference PullRequest);

/// <summary>Updates the branch through the source, pinned to the head the kept status
/// read, then reads the repository again.</summary>
internal sealed class UpdateBranchCommandHandler(PullRequestActs acts, IPullRequestSource source) : ICommandHandler<UpdateBranchCommand, Result>
{
    public Task<Result> Handle(UpdateBranchCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        return acts.RunAsync(command.PullRequest, source.UpdateBranchAsync, cancellationToken);
    }
}
