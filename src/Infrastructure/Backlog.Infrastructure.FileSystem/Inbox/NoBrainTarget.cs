using Backlog.Modules.Inbox.Abstractions;
using Backlog.Modules.Inbox.Abstractions.DataTransferObjects;
using Backlog.Modules.Inbox.Abstractions.Services;
using Backlog.SharedKernel.Results;

namespace Backlog.Infrastructure.FileSystem.Inbox;

/// <summary>The target of a host composed without a Brain: every route is
/// refused in words, and nothing is written.</summary>
internal sealed class NoBrainTarget : IInboxBrainTarget
{
    public Task<Result<string>> WriteNoteAsync(InboxBrainNoteRequestDto request, CancellationToken cancellationToken = default) =>
        Task.FromResult(Result.Failure<string>(InboxErrors.BrainUnavailable(InboxErrors.NoBrainHere)));

    public string? UnavailableReason => InboxErrors.NoBrainHere;
}
