using Backlog.Mobile.UI.Services;

namespace Backlog.Mobile.UI.TalkNotes;

/// <summary>
/// Where a talk note's files wait on the device. The outbox row holds JSON, not
/// bytes, so the bytes live beside the database in two folders: <c>drafts</c>
/// for what is on the strip, and <c>outbox</c> for what has been saved and not
/// yet delivered. A file leaves <c>outbox</c> when the capture naming it has
/// reached the service — never before, since until then a retry may need it.
/// </summary>
public sealed class TalkNoteFiles
{
    public TalkNoteFiles(string root)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(root);

        DraftsFolder = Path.Combine(root, "drafts");
        OutboxFolder = Path.Combine(root, "outbox");
    }

    public string DraftsFolder { get; }

    public string OutboxFolder { get; }

    /// <summary>
    /// A picked file on the strip. A file the service would refuse is described
    /// and not copied — there is no reason to read 400 MB of video to say no to
    /// it — and so is one past the per-note limit.
    /// </summary>
    public async Task<DraftAttachment> StageAsync(PickedFile file, int alreadyOnStrip, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(file);

        var id = Guid.CreateVersion7();
        var contentType = AttachmentRules.ContentTypeOf(file.Name, file.ContentType);
        var refusal = alreadyOnStrip >= AttachmentRules.MaximumPerNote
            ? AttachmentRules.TooMany
            : AttachmentRules.RefusalOf(file.Name, contentType, file.SizeBytes);

        if (refusal is not null)
        {
            return new DraftAttachment(id, file.Name, contentType, file.SizeBytes, string.Empty, refusal);
        }

        Directory.CreateDirectory(DraftsFolder);
        var path = Path.Combine(DraftsFolder, id.ToString("N"));

        long written;
        await using (var source = await file.OpenReadAsync(cancellationToken))
        await using (var target = File.Create(path))
        {
            await source.CopyToAsync(target, cancellationToken);
            written = target.Length;
        }

        // The size the platform reported is a claim; the bytes on disk are what
        // will be uploaded, so the cap is checked again against them.
        return new DraftAttachment(id, file.Name, contentType, written, path, AttachmentRules.RefusalOf(file.Name, contentType, written));
    }

    /// <summary>Where a saved note's attachment waits for its upload.</summary>
    public string OutboxPath(Guid attachmentId) => Path.Combine(OutboxFolder, attachmentId.ToString("N"));

    /// <summary>Takes a file off the strip.</summary>
    public static void Discard(DraftAttachment attachment)
    {
        ArgumentNullException.ThrowIfNull(attachment);
        TryDelete(attachment.StagedPath);
    }

    /// <summary>The capture that named it has arrived; the file is the
    /// service's now.</summary>
    public void Release(Guid attachmentId) => TryDelete(OutboxPath(attachmentId));

    private static void TryDelete(string path)
    {
        if (string.IsNullOrEmpty(path)) return;

        try
        {
            File.Delete(path);
        }
        catch (IOException)
        {
            // Left for the next start; a stray file costs space, not correctness.
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
