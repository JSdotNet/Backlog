using Backlog.Modules.RepositoryManagement.Abstractions.DataTransferObjects;
using Backlog.Modules.RepositoryManagement.Abstractions.Services;
using Backlog.Modules.RepositoryManagement.Services;
using Backlog.SharedKernel.Handlers;
using Backlog.SharedKernel.Results;

namespace Backlog.Modules.RepositoryManagement.Features.MarkReady;

/// <summary>Take a draft out of draft.</summary>
internal sealed record MarkReadyCommand(PullRequestReference PullRequest);

/// <summary>Marks the draft ready for review through the source, then reads the
/// repository again.</summary>
internal sealed class MarkReadyCommandHandler(PullRequestActs acts, IPullRequestSource source) : ICommandHandler<MarkReadyCommand, Result>
{
    public Task<Result> Handle(MarkReadyCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        return acts.RunAsync(command.PullRequest, source.MarkReadyForReviewAsync, cancellationToken);
    }
}
