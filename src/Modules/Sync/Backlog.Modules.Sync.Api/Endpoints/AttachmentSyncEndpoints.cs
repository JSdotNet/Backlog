using Backlog.Modules.Sync.Abstractions;
using Backlog.Modules.Sync.Abstractions.DataTransferObjects;
using Backlog.Modules.Sync.Api.Options;
using Backlog.Modules.Sync.Api.Security;
using Backlog.Modules.Sync.Features.ReadAttachment;
using Backlog.Modules.Sync.Features.StoreAttachment;
using Backlog.Modules.Sync.Ports;
using Backlog.SharedKernel.Handlers;
using Backlog.SharedKernel.Results;
using Microsoft.Extensions.Options;
using Microsoft.Net.Http.Headers;

namespace Backlog.Modules.Sync.Api.Endpoints;

/// <summary>
/// The only door to the attachment store (local ADR 0014): a device uploads a
/// file here before the capture that names it, and the desktop downloads it
/// from here when it takes the capture in. No client ever reaches storage
/// directly, so the owner scope, the size cap and the type allowlist are
/// enforced in this one place, beside <c>OwnerScopeFilter</c>.
/// <para>
/// Neither route takes an owner. The id is the client's own, and the service
/// looks it up under the owner in the caller's token — so another owner's id is
/// a key this caller never reaches, and reads as not found.
/// </para>
/// </summary>
internal static class AttachmentSyncEndpoints
{
    internal static IEndpointRouteBuilder MapAttachmentSyncEndpoints(this IEndpointRouteBuilder sync)
    {
        var limits = sync.ServiceProvider.GetRequiredService<IOptions<SyncAttachmentOptions>>().Value;

        var attachments = sync.MapGroup(string.Empty)
            .WithTags("Attachments")
            .RequireAuthorization(SyncPolicies.PairedDevice)
            .AddEndpointFilter<ReplicaFaultFilter>()
            .RequireOwnerScope();

        // The cap as the host's own request-body limit too, so Kestrel refuses a
        // declared length over it before a byte is read and cuts off a body that
        // runs past it. The handler's counting stream is the same cap for the
        // hosts and bodies that limit does not reach.
        attachments.MapPut(SyncRoutes.Attachment, StoreAttachment)
            .WithMetadata(new RequestBodyLimit(limits.MaxBytes))
            .WithSummary("Uploads one attachment's bytes under this owner.");

        attachments.MapGet(SyncRoutes.Attachment, ReadAttachment)
            .WithSummary("Streams back one of this owner's attachments.");

        return sync;
    }

    /// <remarks>201 with the stored metadata for a new upload; 200 with it for a
    /// repeat of the same bytes under the same id, so a retry after a lost answer
    /// costs nothing.</remarks>
    private static async Task<IResult> StoreAttachment(
        HttpContext context,
        Guid id,
        IOptions<SyncAttachmentOptions> options,
        ICommandHandler<StoreAttachmentCommand, Result<StoreAttachmentOutcome>> handler,
        CancellationToken cancellationToken)
    {
        var limits = options.Value;
        var request = context.Request;

        if (Refusal(request, id, limits) is { } refusal)
        {
            return SyncResults.From(context, Result.Failure<StoredAttachment>(refusal), Results.Ok);
        }

        Result<StoreAttachmentOutcome> result;
        try
        {
            result = await handler.Handle(
                new StoreAttachmentCommand(
                    context.GetOwnerScope(),
                    id,
                    request.ContentType!.Trim(),
                    request.Headers[SyncRoutes.AttachmentSha256Header].ToString().Trim().ToLowerInvariant(),
                    request.Body,
                    limits.MaxBytes),
                cancellationToken);
        }
        catch (BadHttpRequestException limit) when (limit.StatusCode == StatusCodes.Status413PayloadTooLarge)
        {
            // Kestrel enforcing the request-body limit above, mid-stream. The
            // same refusal the counting stream gives, so a client sees one answer
            // for one mistake whichever layer noticed.
            result = Result.Failure<StoreAttachmentOutcome>(StoreAttachmentCommandHandler.TooLarge(limits.MaxBytes));
        }

        return SyncResults.From(
            context,
            result,
            outcome => outcome.Created
                ? Results.Created(SyncRoutes.AttachmentFor(id), outcome.Attachment)
                : Results.Ok(outcome.Attachment));
    }

    /// <summary>
    /// What is wrong with an upload before its body is read, or null when
    /// nothing is. Each refusal names the header or the field, so the client
    /// knows what to change.
    /// </summary>
    private static Error? Refusal(HttpRequest request, Guid id, SyncAttachmentOptions limits)
    {
        if (id == Guid.Empty)
        {
            return Error.Validation(SyncErrorCodes.AttachmentInvalid, "An attachment's id may not be empty.");
        }

        if (!MediaTypeHeaderValue.TryParse(request.ContentType, out var declared) || declared.MediaType.Length == 0)
        {
            return new Error(
                SyncErrorCodes.AttachmentTypeNotAllowed,
                "An attachment upload has to declare its Content-Type.",
                ErrorType.Validation);
        }

        var mediaType = declared.MediaType.ToString();

        if (!limits.Allows(mediaType))
        {
            return new Error(
                SyncErrorCodes.AttachmentTypeNotAllowed,
                $"The Content-Type '{mediaType}' is not one the sync service stores as an attachment.",
                ErrorType.Validation);
        }

        if (request.ContentLength > limits.MaxBytes)
        {
            return StoreAttachmentCommandHandler.TooLarge(limits.MaxBytes);
        }

        var digest = request.Headers[SyncRoutes.AttachmentSha256Header].ToString().Trim();

        if (digest.Length != 64 || !digest.All(char.IsAsciiHexDigit))
        {
            return Error.Validation(
                SyncErrorCodes.AttachmentInvalid,
                $"An attachment upload has to declare the SHA-256 of its bytes in {SyncRoutes.AttachmentSha256Header}, as 64 hex digits.");
        }

        return null;
    }

    /// <remarks>Served as a download and never as a page: <c>Content-Disposition:
    /// attachment</c> and <c>nosniff</c>, so nothing this service returns is
    /// rendered as active content in a browser, whatever type it was stored
    /// with.</remarks>
    private static async Task<IResult> ReadAttachment(
        HttpContext context,
        Guid id,
        IQueryHandler<ReadAttachmentQuery, Result<AttachmentContent>> handler,
        CancellationToken cancellationToken)
    {
        var result = await handler.Handle(new ReadAttachmentQuery(context.GetOwnerScope(), id), cancellationToken);

        return SyncResults.From(context, result, content =>
        {
            var headers = context.Response.Headers;
            headers.ContentDisposition = "attachment";
            headers.XContentTypeOptions = "nosniff";
            headers[SyncRoutes.AttachmentSha256Header] = content.Attachment.Sha256;

            return Results.Stream(content.Content, content.Attachment.ContentType);
        });
    }
}
