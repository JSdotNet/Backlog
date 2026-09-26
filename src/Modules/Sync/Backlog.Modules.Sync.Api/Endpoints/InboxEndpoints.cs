using Backlog.Modules.Sync.Abstractions;
using Backlog.Modules.Sync.Abstractions.DataTransferObjects;
using Backlog.Modules.Sync.Api.Security;
using Backlog.Modules.Sync.Features.AcknowledgeInboxItem;
using Backlog.Modules.Sync.Features.CaptureInboxItem;
using Backlog.Modules.Sync.Features.ListInbox;
using Backlog.SharedKernel.Handlers;
using Backlog.SharedKernel.Results;

namespace Backlog.Modules.Sync.Api.Endpoints;

/// <summary>
/// The captures waiting to reach the desktop. The routes and the request and
/// response shapes are unchanged from when this was a dictionary in the host —
/// the mobile client, the desktop inbox pane, and the editor extension all call
/// them, and this slice moved the storage rather than the surface.
/// <para>
/// What changed underneath is that a capture is now a task document in the
/// replica rather than a record of its own, so the desktop pulls it through the
/// ordinary task feed and there is no second sync path for captures.
/// </para>
/// </summary>
internal static class InboxEndpoints
{
    internal static IEndpointRouteBuilder MapInboxEndpoints(this IEndpointRouteBuilder sync)
    {
        var inbox = sync.MapGroup(string.Empty)
            .WithTags("Inbox")
            .RequireAuthorization(SyncPolicies.PairedDevice)
            .AddEndpointFilter<ReplicaFaultFilter>()
            .RequireOwnerScope();

        inbox.MapGet(SyncRoutes.Inbox, ListInbox)
            .WithSummary("The captures waiting for this owner.");

        inbox.MapPost(SyncRoutes.Inbox, CaptureInboxItem)
            .WithSummary("Pushes a capture into this owner's inbox.");

        inbox.MapPost(SyncRoutes.AcknowledgeInboxItem, AcknowledgeInboxItem)
            .WithSummary("Acknowledges one capture, taking it out of the inbox.");

        return sync;
    }

    private static async Task<IResult> ListInbox(
        HttpContext context,
        IQueryHandler<ListInboxQuery, Result<IReadOnlyList<InboxItem>>> handler,
        CancellationToken cancellationToken)
    {
        var result = await handler.Handle(new ListInboxQuery(context.GetOwnerScope()), cancellationToken);

        return SyncResults.From(context, result, Results.Ok);
    }

    /// <param name="request">Bounded here, at the edge. A capture with nothing in
    /// it is a task nobody can act on, and one the size of a document is a store
    /// failure the person can do nothing about — both are 400s naming the field
    /// rather than something Cosmos answers for.</param>
    /// <remarks>201 for a capture this call wrote; 200 with the stored capture
    /// for a retry whose client id is already here, so a phone that lost its
    /// first answer gets the same item back rather than a second one.</remarks>
    private static async Task<IResult> CaptureInboxItem(
        HttpContext context,
        CaptureRequest request,
        ICommandHandler<CaptureInboxItemCommand, Result<CaptureOutcome>> handler,
        CancellationToken cancellationToken)
    {
        if (OutOfBounds(request) is { } refusal)
        {
            return SyncResults.From(context, Result.Failure<InboxItem>(refusal), Results.Ok);
        }

        var result = await handler.Handle(
            new CaptureInboxItemCommand(
                context.GetOwnerScope(),
                request.Title,
                request.Source,
                request.Id,
                request.BodyMd,
                request.Tags,
                request.Person,
                request.Attachments),
            cancellationToken);

        return SyncResults.From(
            context,
            result,
            // The collection, not a per-item route: there is no GET for one
            // capture, and a Location naming an unmapped URL would send a client
            // that followed it to a 404.
            outcome => outcome.Created
                ? Results.Created(SyncRoutes.Absolute(SyncRoutes.Inbox), outcome.Item)
                : Results.Ok(outcome.Item));
    }

    /// <summary>What is wrong with a capture, or null when nothing is.
    /// <para>
    /// Title and source are required; the rest are optional and bounded when
    /// present. Every failure is one code with its own message: a client cannot
    /// do anything different about them, and the message says which field it
    /// was.
    /// </para>
    /// </summary>
    private static Error? OutOfBounds(CaptureRequest request) =>
        FieldsOutOfBounds(request)
        ?? (request.Tags is { } tags ? TagsOutOfBounds(tags) : null)
        ?? (request.Attachments is { } attachments ? AttachmentsOutOfBounds(attachments) : null);

    private static Error? FieldsOutOfBounds(CaptureRequest request) => request switch
    {
        { Title: var title } when string.IsNullOrWhiteSpace(title) =>
            Invalid("A capture needs a title."),

        { Title.Length: var length } when length > SyncRequestLimits.MaximumCaptureTitle =>
            Invalid($"A capture's title may be at most {SyncRequestLimits.MaximumCaptureTitle} characters."),

        { Source: var source } when string.IsNullOrWhiteSpace(source) =>
            Invalid("A capture needs to say where it came from."),

        { Source.Length: var length } when length > SyncRequestLimits.MaximumCaptureSource =>
            Invalid($"A capture's source may be at most {SyncRequestLimits.MaximumCaptureSource} characters."),

        { Id: var id } when id == Guid.Empty =>
            Invalid("A capture's id, when given, may not be empty."),

        { BodyMd.Length: var length } when length > SyncRequestLimits.MaximumCaptureBody =>
            Invalid($"A capture's body may be at most {SyncRequestLimits.MaximumCaptureBody} characters."),

        { Person.Length: var length } when length > SyncRequestLimits.MaximumCapturePerson =>
            Invalid($"A capture's person may be at most {SyncRequestLimits.MaximumCapturePerson} characters."),

        // One handle, because the desktop reads it back as one @name token.
        { Person: { } person } when person.Trim().TrimStart('@').Any(char.IsWhiteSpace) =>
            Invalid("A capture's person is one name, with no spaces."),

        _ => null,
    };

    /// <summary>The metadata of the files a capture names, bounded like every
    /// other field. Whether each one was actually uploaded is the handler's to
    /// check against the store; this is only what a well-formed list looks
    /// like.</summary>
    private static Error? AttachmentsOutOfBounds(IReadOnlyList<AttachmentMetadata> attachments)
    {
        if (attachments.Count > SyncRequestLimits.MaximumCaptureAttachments)
            return Invalid($"A capture may name at most {SyncRequestLimits.MaximumCaptureAttachments} attachments.");

        if (attachments.Select(attachment => attachment?.Id).Distinct().Count() != attachments.Count)
            return Invalid("A capture may name each attachment once.");

        foreach (var attachment in attachments)
        {
            if (attachment is null)
                return Invalid("A capture's attachments may not be empty.");

            if (attachment.Id == Guid.Empty)
                return Invalid("An attachment's id may not be empty.");

            if (string.IsNullOrWhiteSpace(attachment.Name) || attachment.Name.Length > SyncRequestLimits.MaximumAttachmentName)
                return Invalid($"An attachment's name is required and may be at most {SyncRequestLimits.MaximumAttachmentName} characters.");

            if (string.IsNullOrWhiteSpace(attachment.ContentType) || attachment.ContentType.Length > SyncRequestLimits.MaximumAttachmentContentType)
                return Invalid($"An attachment's content type is required and may be at most {SyncRequestLimits.MaximumAttachmentContentType} characters.");

            if (attachment.SizeBytes < 0)
                return Invalid("An attachment's size may not be negative.");

            if (attachment.Sha256 is not { Length: 64 } digest || !digest.All(char.IsAsciiHexDigit))
                return Invalid("An attachment's sha256 is 64 hex digits.");
        }

        return null;
    }

    private static Error? TagsOutOfBounds(IReadOnlyList<string> tags)
    {
        if (tags.Count > SyncRequestLimits.MaximumCaptureTags)
            return Invalid($"A capture may carry at most {SyncRequestLimits.MaximumCaptureTags} tags.");

        foreach (var tag in tags)
        {
            if (string.IsNullOrWhiteSpace(tag))
                return Invalid("A capture's tags may not be empty.");

            if (tag.Length > SyncRequestLimits.MaximumCaptureTag)
                return Invalid($"A capture's tags may be at most {SyncRequestLimits.MaximumCaptureTag} characters each.");

            // The person travels among the document's tags as @name, and the
            // desktop reads the sigil as "a person" — so a person sent as a tag
            // would arrive as one. It has a field of its own.
            if (tag.Trim().TrimStart('#').TrimStart().StartsWith('@'))
                return Invalid($"'{tag.Trim()}' names a person, and a person is not a tag; send it as the person.");
        }

        return null;
    }

    private static Error Invalid(string message) => Error.Validation(SyncErrorCodes.CaptureInvalid, message);

    private static async Task<IResult> AcknowledgeInboxItem(
        HttpContext context,
        Guid id,
        ICommandHandler<AcknowledgeInboxItemCommand, Result> handler,
        CancellationToken cancellationToken)
    {
        var result = await handler.Handle(
            new AcknowledgeInboxItemCommand(context.GetOwnerScope(), id), cancellationToken);

        return SyncResults.From(context, result, Results.NoContent);
    }
}
