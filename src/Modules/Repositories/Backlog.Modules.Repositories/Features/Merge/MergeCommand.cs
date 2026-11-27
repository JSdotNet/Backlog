using Backlog.Modules.Repositories.Abstractions.DataTransferObjects;
using Backlog.Modules.Repositories.Abstractions.Services;
using Backlog.Modules.Repositories.Services;
using Backlog.SharedKernel.Handlers;
using Backlog.SharedKernel.Results;

namespace Backlog.Modules.Repositories.Features.Merge;

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
