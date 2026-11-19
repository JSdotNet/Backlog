using System.ComponentModel;
using System.Diagnostics;
using System.Security.Claims;
using System.Text.Json.Serialization;
using Backlog.Modules.Sync.Abstractions.DataTransferObjects;
using Backlog.Modules.Sync.Api.Endpoints;
using Backlog.Modules.Sync.DomainModels;
using Backlog.Modules.Sync.Features.CaptureInboxItem;
using Backlog.Modules.Sync.Observability;
using Backlog.SharedKernel.Handlers;
using Backlog.SharedKernel.Results;
using Microsoft.IdentityModel.JsonWebTokens;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Backlog.Modules.Sync.Api.Mcp;

/// <summary>
/// The <c>inbox:capture</c> tool group of the remote <c>/mcp</c> endpoint (local ADR 0058 §8):
/// the one thing the claude.ai connector may do, capture into the owner's Inbox.
/// <para>
/// <b>It writes the replica, never the desktop.</b> The capture goes through the same
/// <see cref="CaptureInboxItemCommand"/> the phone's <c>POST /api/sync/inbox</c> does, so it lands
/// as a <c>capture</c>-kind document (local ADR 0009) the desktop's inbox intake takes on its
/// next pull — and the same bounds refuse what that route refuses.
/// </para>
/// <para>
/// <b>Whose Inbox is the token's.</b> The owner is the access token's subject, which the
/// authorization server set to the owner it signed in; no argument names one. The writer is
/// <see cref="ConnectorDevice"/>, a fixed id no paired device has, so the desktop never takes a
/// connector capture for its own echo.
/// </para>
/// </summary>
[McpServerToolType]
public sealed class RemoteInboxTools(
    ICommandHandler<CaptureInboxItemCommand, Result<CaptureOutcome>> capture,
    TimeProvider clock)
{
    internal const string CaptureToInbox = "capture_to_inbox";

    /// <summary>The capture source the desktop files these under as their channel, which its
    /// Inbox labels "Claude".</summary>
    internal const string Source = "claude";

    /// <summary>The writer of every connector capture on the replica: no device, so a fixed id
    /// that no registration ever mints (they are version 7).</summary>
    internal static readonly DeviceId ConnectorDevice = new(new Guid("c1a0de00-0000-8000-8000-00000000c0de"));

    [McpServerTool(Name = CaptureToInbox, Title = "Capture into the Backlog Inbox", ReadOnly = false, Idempotent = false, Destructive = false, OpenWorld = false, UseStructuredContent = true)]
    [Description(
        "Adds one item to the person's Backlog Inbox, where they triage it on the desktop later. "
        + "A title is required; notes are Markdown beneath it; tags are optional words without #. "
        + "Pass an id you generated to make a retry safe: the same id returns the stored capture "
        + "instead of a second one. Nothing else in Backlog can be read or changed through this server.")]
    public async Task<RemoteCapturePayload> CaptureAsync(
        RequestContext<CallToolRequestParams> request,
        [Description("The capture's title, one line.")]
        string title,
        [Description("Optional. Notes beneath the title, as Markdown.")]
        string? notes = null,
        [Description("Optional. Tags, each one word, without # or @.")]
        string[]? tags = null,
        [Description("Optional. A GUID you generated, so a retry returns the same capture.")]
        Guid? id = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var owner = OwnerOf(request.User);

        var wanted = new CaptureRequest(title, Source, id, notes, tags);
        if (InboxEndpoints.OutOfBounds(wanted, clock.GetUtcNow()) is { } refusal)
        {
            throw Failure(refusal);
        }

        var result = await capture.Handle(
            new CaptureInboxItemCommand(
                new OwnerScope(owner, ConnectorDevice),
                title,
                Source,
                id,
                notes,
                tags),
            cancellationToken);

        if (!result.IsSuccess)
        {
            throw Failure(result.Error);
        }

        SyncTelemetry.RemoteMcpCaptures.Add(1, new TagList { { "created", result.Value.Created } });

        var item = result.Value.Item;
        return new RemoteCapturePayload(item.Id, item.Title, item.CapturedAt, result.Value.Created);
    }

    /// <summary>The owner the access token names. The endpoint's policy admitted the call, so a
    /// principal without one is a policy that stopped matching this tool: refused, never
    /// guessed.</summary>
    private static OwnerId OwnerOf(ClaimsPrincipal? user) =>
        Guid.TryParse(user?.FindFirstValue(JwtRegisteredClaimNames.Sub), out var owner)
            ? new OwnerId(owner)
            : throw new McpException("This call carries no owner.");

    /// <summary>A refusal as the tool's error result: its code and message, and nothing of the
    /// request.</summary>
    private static McpException Failure(Error error) => new(error.ToString());
}

/// <summary>What <c>capture_to_inbox</c> answers: the capture as stored, and whether this call
/// created it or found it from an earlier call with the same id.</summary>
public sealed record RemoteCapturePayload(
    [property: JsonPropertyName("id")] Guid Id,
    [property: JsonPropertyName("title")] string Title,
    [property: JsonPropertyName("captured_at")] DateTimeOffset CapturedAt,
    [property: JsonPropertyName("created")] bool Created);
