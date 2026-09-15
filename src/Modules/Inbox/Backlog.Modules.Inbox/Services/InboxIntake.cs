using Backlog.Modules.Inbox.Abstractions.DataTransferObjects;
using Backlog.Modules.Inbox.Abstractions.Services;
using Backlog.Modules.Inbox.Features.ReceiveCapture;
using Backlog.SharedKernel.Handlers;
using Backlog.SharedKernel.Results;

namespace Backlog.Modules.Inbox.Services;

/// <summary>
/// The published <see cref="IInboxIntake"/> port over the one slice behind it.
/// The handler never fails — every capture has an outcome — so the
/// <see cref="Result"/> is unwrapped here and the caller sees the outcome alone.
/// </summary>
internal sealed class InboxIntake(ICommandHandler<ReceiveCaptureCommand, Result<InboxIntakeOutcome>> receive)
    : IInboxIntake
{
    public async Task<InboxIntakeOutcome> ReceiveAsync(InboxCaptureDto capture, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(capture);

        var result = await receive.Handle(new ReceiveCaptureCommand(capture), cancellationToken).ConfigureAwait(false);

        return result.Value;
    }
}
