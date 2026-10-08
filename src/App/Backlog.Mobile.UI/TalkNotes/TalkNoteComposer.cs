using System.Security.Cryptography;

using Backlog.Mobile.UI.Outbox;
using Backlog.Modules.Sync.Abstractions.DataTransferObjects;

namespace Backlog.Mobile.UI.TalkNotes;

/// <summary>
/// Turns the draft into the one outbox entry a saved talk note is: each
/// picture downscaled unless the note keeps originals, each file moved from the
/// strip's folder to the outbox's, the metadata the service checks — size and
/// SHA-256 of the bytes that will actually go — and a title when the note had
/// none.
/// </summary>
public sealed class TalkNoteComposer(TalkNoteFiles files, DeviceOutbox outbox, TimeProvider clock)
{
    /// <summary>Queues the note and returns its entry id, or null when the draft
    /// cannot be saved as it stands.</summary>
    public async Task<Guid?> SaveAsync(TalkNoteDraft draft, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(draft);
        if (!draft.CanSave) return null;

        var attachments = await PrepareAsync(draft.Accepted, draft.SendOriginals, cancellationToken);

        var today = DateOnly.FromDateTime(clock.GetLocalNow().DateTime);
        var title = TalkNoteTitle.For(draft.Title, draft.Body, [.. attachments.Select(a => (a.Name, a.ContentType))], today);
        var body = draft.Body.Trim();
        var tags = draft.TagList;

        var id = Guid.CreateVersion7();
        var capture = new CaptureRequest(
            title,
            CaptureOutboxKind.Source,
            id,
            body.Length > 0 ? body : null,
            tags.Count > 0 ? tags : null,
            draft.Person,
            attachments.Count > 0 ? attachments : null);

        await outbox.EnqueueAsync(TalkNoteOutboxKind.Token, id, TalkNoteOutboxKind.Write(new TalkNotePayload(capture, [])), cancellationToken);

        draft.LastSaved = id;
        draft.Clear();

        return id;
    }

    /// <summary>
    /// Moves each staged file into the outbox folder as it will be sent — a
    /// picture downscaled unless <paramref name="sendOriginals"/> — and answers
    /// the metadata the service checks. The capture sheet uses it for the photo
    /// it sends with a note, which goes through the note projection rather than
    /// as a talk note.
    /// </summary>
    public async Task<IReadOnlyList<AttachmentMetadata>> PrepareAsync(
        IReadOnlyList<DraftAttachment> staged,
        bool sendOriginals,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(staged);

        Directory.CreateDirectory(files.OutboxFolder);

        var attachments = new List<AttachmentMetadata>();
        foreach (var picked in staged)
        {
            attachments.Add(await PrepareAsync(picked, sendOriginals, cancellationToken));
        }

        return attachments;
    }

    /// <summary>
    /// Readies one file on the strip to go: downscaled unless kept original, moved
    /// into the outbox folder under its id, and described by the size and digest
    /// of the bytes that will actually be uploaded. The Notes editor hands what
    /// this returns to <see cref="Notes.NoteViewProjection"/>, whose outbox entry
    /// uploads it from there.
    /// </summary>
    public async Task<AttachmentMetadata> PrepareAsync(DraftAttachment picked, bool sendOriginals, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(picked);
        Directory.CreateDirectory(files.OutboxFolder);

        var target = files.OutboxPath(picked.Id);
        var name = picked.Name;
        var contentType = picked.ContentType;

        byte[]? downscaled = null;
        if (!sendOriginals && PictureDownscaler.Handles(contentType))
        {
            await using var source = File.OpenRead(picked.StagedPath);
            downscaled = PictureDownscaler.Downscale(source);
        }

        if (downscaled is not null)
        {
            await File.WriteAllBytesAsync(target, downscaled, cancellationToken);
            TalkNoteFiles.Discard(picked);

            name = Path.ChangeExtension(name, ".jpg");
            contentType = PictureDownscaler.ContentType;
        }
        else
        {
            // As taken: a file, a GIF, a picture kept original, or one this
            // device cannot decode.
            File.Move(picked.StagedPath, target, overwrite: true);
        }

        await using var stream = File.OpenRead(target);
        var sha256 = Convert.ToHexStringLower(await SHA256.HashDataAsync(stream, cancellationToken));

        return new AttachmentMetadata(picked.Id, name, contentType, stream.Length, sha256);
    }
}
