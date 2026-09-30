using System.Text.Json;

using Backlog.Mobile.UI.Services;
using Backlog.Mobile.UI.TalkNotes;
using Backlog.Modules.Sync.Abstractions.DataTransferObjects;

namespace Backlog.Mobile.UI.Outbox;

/// <summary>
/// A talk note, as one outbox entry: its attachments uploaded first, each to
/// <c>PUT /api/sync/attachments/{id}</c>, then the capture naming them posted to
/// <c>POST /api/sync/inbox</c> — the order local ADR 0014 requires, since the
/// service refuses a capture naming a file it does not have.
/// <para>
/// One entry rather than one per file, so a saved note is queued whole or not at
/// all. Each upload that lands is checkpointed into the payload, so a retry
/// starts at the first file still to go and never sends one twice; and each gets
/// the full attempt budget, since the checkpoint hands the attempts back. A file
/// the service refuses (409, 413, 415) refuses the entry, naming the file: the
/// capture would be refused for the missing attachment anyway.
/// </para>
/// </summary>
public sealed class TalkNoteOutboxKind(CloudSyncClient sync, TalkNoteFiles files) : IStagedOutboxKind
{
    /// <summary>A stored value — the <c>kind</c> column of every queued talk note.</summary>
    public const string Token = "talk-note";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public string Kind => Token;

    public static string Write(TalkNotePayload payload) => JsonSerializer.Serialize(payload, Json);

    public static TalkNotePayload Read(OutboxEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);

        return JsonSerializer.Deserialize<TalkNotePayload>(entry.PayloadJson, Json)
            ?? throw new InvalidOperationException($"Outbox entry {entry.Id} holds no talk note.");
    }

    public Task<OutboxDelivery> SendAsync(OutboxEntry entry, CancellationToken cancellationToken) =>
        SendAsync(entry, (_, _) => Task.CompletedTask, cancellationToken);

    public async Task<OutboxDelivery> SendAsync(
        OutboxEntry entry,
        Func<string, CancellationToken, Task> checkpoint,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(checkpoint);

        var payload = Read(entry);
        var attachments = payload.Capture.Attachments ?? [];

        foreach (var attachment in attachments)
        {
            if (payload.Uploaded.Contains(attachment.Id)) continue;

            var path = files.OutboxPath(attachment.Id);
            if (!File.Exists(path))
            {
                return OutboxDelivery.Refused($"{attachment.Name} is no longer on this phone.");
            }

            var delivery = await OutboxDelivery.AttemptAsync(
                async () =>
                {
                    await using var content = File.OpenRead(path);
                    return await sync.PutAttachmentAsync(attachment, content, cancellationToken);
                },
                cancellationToken);

            if (delivery.Kind != OutboxDeliveryKind.Delivered)
            {
                return delivery with { Error = $"{attachment.Name}: {delivery.Error}" };
            }

            payload = payload with { Uploaded = [.. payload.Uploaded, attachment.Id] };
            await checkpoint(Write(payload), cancellationToken);
        }

        // Posted as a plain capture is: under the entry's id, the one every
        // retry repeats and the service de-duplicates on, and made when the
        // note was saved.
        var capture = CaptureOutboxKind.Stamped(payload.Capture, entry);
        var posted = await OutboxDelivery.AttemptAsync(() => sync.PostCaptureAsync(capture, cancellationToken), cancellationToken);

        if (posted.Kind == OutboxDeliveryKind.Delivered)
        {
            foreach (var attachment in attachments) files.Release(attachment.Id);
        }

        return posted;
    }

    /// <summary>
    /// What the page says about a saved note: "synced" once the outbox has
    /// let it go, "uploading 2 of 3" while an attempt is sending its files,
    /// otherwise "waiting" — or "waiting — tap to retry" once it is parked.
    /// </summary>
    public static string StatusOf(DeviceOutbox outbox, Guid id)
    {
        ArgumentNullException.ThrowIfNull(outbox);

        if (outbox.Entries.FirstOrDefault(entry => entry.Id == id) is not { } entry) return "synced";
        if (entry.IsParked) return "waiting — tap to retry";

        var payload = Read(entry);
        var total = payload.Capture.Attachments?.Count ?? 0;
        var uploaded = payload.Uploaded.Count;

        return outbox.Sending == id && uploaded < total
            ? $"uploading {uploaded + 1} of {total}"
            : "waiting";
    }
}

/// <summary>
/// A queued talk note: the capture as it will be posted, attachment metadata
/// included, and the ids of the attachments already uploaded.
/// </summary>
public sealed record TalkNotePayload(CaptureRequest Capture, IReadOnlyList<Guid> Uploaded);
