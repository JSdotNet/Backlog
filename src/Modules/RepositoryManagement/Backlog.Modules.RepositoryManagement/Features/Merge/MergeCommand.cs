using Backlog.Modules.RepositoryManagement.Abstractions.DataTransferObjects;
using Backlog.Modules.RepositoryManagement.Abstractions.Services;
using Backlog.Modules.RepositoryManagement.Services;
using Backlog.SharedKernel.Handlers;
using Backlog.SharedKernel.Results;

namespace Backlog.Modules.RepositoryManagement.Features.Merge;

/// <summary>Merge the pull request now.</summary>
internal sealed record MergeCommand(PullRequestReference PullRequest);

/// <summary>Merges a pull request through the source, then reads its repository again.</summary>
internal sealed class MergeCommandHandler(PullRequestActs acts, IPullRequestSource source) : ICommandHandler<MergeCommand, Result>
{
    public Task<Result> Handle(MergeCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        return acts.RunAsync(command.PullRequest, source.MergeAsync, cancellationToken);
    }
}
