using Backlog.Modules.Sync.Abstractions;
using Backlog.Modules.Sync.Abstractions.DataTransferObjects;
using Backlog.Modules.Sync.Api.Security;

namespace Backlog.Modules.Sync.Api.Endpoints;

/// <summary>
/// The captures waiting to reach the desktop. The request and response shapes
/// are unchanged from before device pairing existed — what changed is that the
/// group now requires a device token and every call is scoped to the owner that
/// token names.
/// </summary>
internal static class InboxEndpoints
{
    internal static IEndpointRouteBuilder MapInboxEndpoints(this IEndpointRouteBuilder sync)
    {
        var inbox = sync.MapGroup(string.Empty)
            .WithTags("Inbox")
            .RequireAuthorization(SyncPolicies.PairedDevice)
            .RequireOwnerScope();

        inbox.MapGet(SyncRoutes.Inbox, (HttpContext context, SyncStore store) =>
                Results.Ok(store.All(context.GetOwnerScope().OwnerId)))
            .WithSummary("The captures waiting for this owner.");

        inbox.MapPost(SyncRoutes.Inbox, (HttpContext context, SyncStore store, CaptureRequest request) =>
            {
                var item = store.Capture(context.GetOwnerScope().OwnerId, request.Title, request.Source);
                // The collection, not a per-item route: there is no GET for one
                // capture, and a Location naming an unmapped URL would send a
                // client that followed it to a 404.
                return Results.Created(SyncRoutes.Absolute(SyncRoutes.Inbox), item);
            })
            .WithSummary("Pushes a capture into this owner's inbox.");

        inbox.MapPost(SyncRoutes.AcknowledgeInboxItem, (HttpContext context, SyncStore store, Guid id) =>
                store.Acknowledge(context.GetOwnerScope().OwnerId, id)
                    ? Results.NoContent()
                    : SyncResults.Problem(
                        context,
                        StatusCodes.Status404NotFound,
                        SyncErrorCodes.InboxItemNotFound,
                        "No capture with that id is waiting for this owner."))
            .WithSummary("Acknowledges one capture, removing it.");

        return sync;
    }
}
