using System.Net;
using System.Text.Json;

using Backlog.Mobile.UI.Services;
using Backlog.Modules.Sync.Abstractions;
using Backlog.Modules.Sync.Abstractions.DataTransferObjects;

namespace Backlog.Mobile.UI.Outbox;

/// <summary>What a <see cref="CaptureEditOutboxKind"/> entry carries: the
/// capture it rewords, and the new title and body, a null body meaning none.</summary>
public sealed record CaptureEdit(Guid CaptureId, string Title, string? BodyMd);

/// <summary>
/// The phone rewording a capture that has already left it, sent as
/// <c>PUT /api/sync/inbox/{id}</c>. A capture still waiting in the outbox is
/// rewritten there instead (<c>CaptureEdits</c>), so an entry of this kind
/// only exists for one the service already holds — or is being sent right now.
/// <para>
/// The entry's id is its own; the capture's travels in the payload, as a note
/// edit's does, so two edits of one capture are two entries in the order they
/// were made. The service stamps each on its own clock, and the later one wins.
/// </para>
/// <para>
/// <see cref="Token"/> is a stored value: entries written under it are read by
/// every later build, so it is never renamed. An older build that does not know
/// it sets such an entry aside as refused and moves on.
/// </para>
/// </summary>
public sealed class CaptureEditOutboxKind(CloudSyncClient sync, CaptureDealtWithNotices notices) : IOutboxKind
{
    public const string Token = "capture-edit";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public string Kind => Token;

    public static string Write(CaptureEdit edit) => JsonSerializer.Serialize(edit, Json);

    public static CaptureEdit Read(OutboxEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);

        return JsonSerializer.Deserialize<CaptureEdit>(entry.PayloadJson, Json)
            ?? throw new InvalidOperationException($"Outbox entry {entry.Id} holds no capture edit.");
    }

    public Task<OutboxDelivery> SendAsync(OutboxEntry entry, CancellationToken cancellationToken)
    {
        var edit = Read(entry);

        return AttemptAsync(
            edit.CaptureId,
            notices,
            () => sync.PutCaptureAsync(edit.CaptureId, new CaptureEditRequest(edit.Title, edit.BodyMd), cancellationToken),
            NoEditRoute,
            cancellationToken);
    }

    /// <summary>
    /// A bare 404 or 405 on the edit route: a service older than this phone, which
    /// has no such route. Set aside rather than held — a held entry keeps every
    /// later capture on the phone until the service is updated, and the deployed
    /// service can lag the app — and never counted as delivered. The person
    /// retries it from the row once the service is updated.
    /// </summary>
    internal static OutboxDelivery NoEditRoute { get; } = OutboxDelivery.Refused("The cloud service cannot change a capture yet.");

    /// <summary>
    /// One attempt at a change to a capture, counted the way both capture kinds
    /// count it:
    /// <list type="bullet">
    /// <item>a 2xx is delivered;</item>
    /// <item>a 404 carrying <c>inbox.item_not_found</c> is delivered too — the
    /// desktop decided the capture first, so there is nothing left to change — and
    /// recorded in <paramref name="notices"/> so the person hears of it;</item>
    /// <item>a 404 or 405 with no problem code is not the capture's answer, and is
    /// counted as <paramref name="bareRefusal"/> — never as delivered;</item>
    /// <item>anything else as every other kind counts it
    /// (<see cref="OutboxDelivery.FromAnswer"/>).</item>
    /// </list>
    /// </summary>
    internal static async Task<OutboxDelivery> AttemptAsync(
        Guid captureId,
        CaptureDealtWithNotices notices,
        Func<Task<(HttpStatusCode Status, string? Detail, string? Code)>> send,
        OutboxDelivery bareRefusal,
        CancellationToken cancellationToken)
    {
        (HttpStatusCode Status, string? Detail, string? Code) answer;

        try
        {
            answer = await send();
        }
        catch (HttpRequestException ex)
        {
            return OutboxDelivery.Transient(ex.Message);
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return OutboxDelivery.Transient("Cloud sync did not answer in time.");
        }

        switch (answer)
        {
            case { Status: HttpStatusCode.NotFound, Code: SyncErrorCodes.InboxItemNotFound }:
                notices.Record(captureId);
                return OutboxDelivery.Delivered;

            case { Status: HttpStatusCode.NotFound or HttpStatusCode.MethodNotAllowed, Code: null }:
                return bareRefusal;

            default:
                return OutboxDelivery.FromAnswer(answer.Status, answer.Detail);
        }
    }
}
