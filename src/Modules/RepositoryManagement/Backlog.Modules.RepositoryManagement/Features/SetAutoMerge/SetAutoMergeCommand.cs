using Backlog.Modules.RepositoryManagement.Abstractions.DataTransferObjects;
using Backlog.Modules.RepositoryManagement.Abstractions.Services;
using Backlog.Modules.RepositoryManagement.Services;
using Backlog.SharedKernel.Handlers;
using Backlog.SharedKernel.Results;

namespace Backlog.Modules.RepositoryManagement.Features.SetAutoMerge;

/// <summary>Hand the pull request to GitHub's auto-merge, or withdraw that request.</summary>
internal sealed record SetAutoMergeCommand(PullRequestReference PullRequest, bool Enable);

/// <summary>Switches auto-merge through the source, then reads the repository again.</summary>
internal sealed class SetAutoMergeCommandHandler(PullRequestActs acts, IPullRequestSource source) : ICommandHandler<SetAutoMergeCommand, Result>
{
    public Task<Result> Handle(SetAutoMergeCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        return acts.RunAsync(
            command.PullRequest,
            (pull, token) => source.SetAutoMergeAsync(pull, command.Enable, token),
            cancellationToken);
    }
}
