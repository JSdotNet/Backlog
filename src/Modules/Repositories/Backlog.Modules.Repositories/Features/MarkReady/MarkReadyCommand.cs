using Backlog.Modules.Repositories.Abstractions.DataTransferObjects;
using Backlog.Modules.Repositories.Abstractions.Services;
using Backlog.Modules.Repositories.Services;
using Backlog.SharedKernel.Handlers;
using Backlog.SharedKernel.Results;

namespace Backlog.Modules.Repositories.Features.MarkReady;

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
