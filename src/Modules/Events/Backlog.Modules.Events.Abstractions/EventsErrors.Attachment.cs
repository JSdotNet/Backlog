using Backlog.SharedKernel.Results;

namespace Backlog.Modules.Events.Abstractions;

/// <summary>The refusal an attachment request adds
/// (<c>.devbook/domain/events/domain.invariants.md#attachment-request</c>).</summary>
public static partial class EventsErrors
{
    public static Error AttachmentRequestIncomplete() =>
        Error.Validation("attachment_request.incomplete", "An attachment request names one knowledge event and one Inbox note.");
}
