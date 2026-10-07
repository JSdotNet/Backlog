using System.Text.Json;

using Backlog.Mobile.UI.Services;
using Backlog.Mobile.UI.TalkNotes;
using Backlog.Modules.Sync.Abstractions.DataTransferObjects;

namespace Backlog.Mobile.UI.Outbox;

/// <summary>
/// A note the phone created or edited, as one outbox entry: the files it newly
/// names uploaded first, each to <c>PUT /api/sync/attachments/{id}</c>, then the
/// whole note document pushed to <c>POST /api/sync/tasks</c>
/// (<c>.devbook/arc42/06-runtime-view.md#mobile-note-sync</c>).
/// <para>
/// The same queue, order, backoff and waiting marker as every other kind. The
/// files go first for the reason a talk note's do (local ADR 0014), and each
/// upload that lands is checkpointed, so a retry starts at the first file still
/// to go. The note document needs no idempotency key: the replica upserts whole
/// documents under last-write-wins (local ADR 0005), so a retry that sends the
/// same document under the same note id lands the same document.
/// </para>
/// <para>
/// The entry's own id is not the note's. A note edited twice while the phone is
/// offline is two entries, oldest first, and the outbox keys an entry by its id.
/// </para>
/// </summary>
public sealed class NoteOutboxKind(CloudSyncClient sync, TalkNoteFiles files) : IStagedOutboxKind
{
    /// <summary>A stored value — the <c>kind</c> column of every queued note.</summary>
    public const string Token = "note";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public string Kind => Token;

    public static string Write(NoteOutboxPayload payload) => JsonSerializer.Serialize(payload, Json);

    public static NoteOutboxPayload Read(OutboxEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);

        return JsonSerializer.Deserialize<NoteOutboxPayload>(entry.PayloadJson, Json)
            ?? throw new InvalidOperationException($"Outbox entry {entry.Id} holds no note.");
    }

    /// <summary>
    /// Whether an edit of the note is still on the phone, parked or not: the
    /// editor says "Waiting to sync" until the entry carrying the last of them is
    /// delivered. Keyed on the note inside the payload, since an entry's own id is
    /// not the note's.
    /// </summary>
    public static bool Holds(DeviceOutbox outbox, Guid noteId)
    {
        ArgumentNullException.ThrowIfNull(outbox);

        return outbox.Entries.Any(entry => entry.Kind == Token && Carries(entry, noteId));
    }

    private static bool Carries(OutboxEntry entry, Guid noteId)
    {
        try
        {
            return Read(entry).Change.Id == noteId;
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException)
        {
            // An entry this build cannot read is not this note's; the outbox
            // still sends or parks it on its own.
            return false;
        }
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
        var named = payload.Change.Task.Attachments ?? [];

        foreach (var id in payload.ToUpload)
        {
            if (payload.Uploaded.Contains(id)) continue;

            if (named.FirstOrDefault(attachment => attachment.Id == id) is not { } attachment)
            {
                // Named for upload and then not on the note: nothing to send.
                continue;
            }

            var path = files.OutboxPath(id);
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

            payload = payload with { Uploaded = [.. payload.Uploaded, id] };
            await checkpoint(Write(payload), cancellationToken);
        }

        var pushed = await OutboxDelivery.AttemptAsync(() => sync.PushTaskAsync(payload.Change, cancellationToken), cancellationToken);

        if (pushed.Kind == OutboxDeliveryKind.Delivered)
        {
            foreach (var id in payload.ToUpload) files.Release(id);
        }

        return pushed;
    }
}

/// <summary>
/// A queued note: the whole document as it will be pushed, the ids of the files
/// this edit added, which are still on the phone to upload, and the ids of those
/// already uploaded. Files the note named before this edit are in the attachment
/// store already and are not sent again.
/// </summary>
public sealed record NoteOutboxPayload(TaskChange Change, IReadOnlyList<Guid> ToUpload, IReadOnlyList<Guid> Uploaded);
