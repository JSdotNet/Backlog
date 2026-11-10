using Backlog.Mobile.UI.Notes;
using Backlog.Mobile.UI.TalkNotes;
using Backlog.Modules.Sync.Abstractions.DataTransferObjects;

namespace Backlog.Mobile.UI.Recordings;

/// <summary>A recording file to attach, and the name it goes on the note under.</summary>
internal sealed record RecordingToAttach(Guid AttachmentId, string Name, string ContentType, string Path);

/// <summary>What attaching came to: the files now on the note, and why any are not.</summary>
internal sealed record RecordingAttachOutcome(IReadOnlyList<AttachmentMetadata> Attached, string? Error);

/// <summary>
/// The one way a recording reaches its note, for a stop and for a recovery alike
/// (<c>.devbook/arc42/06-runtime-view.md#talk-note-upload</c>):
/// <list type="number">
/// <item>each part is copied, and the copy moved into the talk notes' outbox folder and hashed
/// by <see cref="TalkNoteComposer"/> — audio is never downscaled;</item>
/// <item>the parts that got there are added to the note in one attach-only write,
/// <see cref="NoteViewProjection.AttachAsync"/>, which keeps whatever title and body the note
/// has by then, and whose outbox entry uploads them;</item>
/// <item>only then is each attached part deleted from <c>notes/recordings/</c>.</item>
/// </list>
/// A part that fails on the way, and every part when the process dies before the note is
/// written, is still in the recordings folder for <see cref="RecordingRecovery"/>; attaching it
/// again under the same attachment id is harmless, because the note never names a file twice.
/// </summary>
internal static class RecordingAttachments
{
    public const string PartKept = "Part of the recording could not be put on the note. It is kept, and added when the app next starts.";
    public const string NoteGone = "The note this recording belongs to is no longer on the phone. The recording is kept.";

    public static async Task<RecordingAttachOutcome> AttachAsync(
        TalkNoteComposer composer,
        RecordingFiles recordings,
        NoteViewProjection notes,
        Guid noteId,
        IReadOnlyList<RecordingToAttach> files,
        CancellationToken cancellationToken)
    {
        if (notes.Find(noteId) is not { } note) return new([], NoteGone);

        string? error = null;
        var prepared = new List<(RecordingToAttach File, AttachmentMetadata Metadata)>();

        foreach (var file in files)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (note.Attachments.Any(attached => attached.Id == file.AttachmentId))
            {
                // Attached before the process died, on the way to deleting it.
                TryDelete(file.Path);
                continue;
            }

            if (note.Attachments.Count + prepared.Count >= AttachmentRules.MaximumPerNote)
            {
                // Left in the folder, as a part that fails is: a later entry may make room.
                error ??= AttachmentRules.TooMany;
                continue;
            }

            var staged = recordings.StagingPath(file.AttachmentId);

            try
            {
                var size = new FileInfo(file.Path).Length;

                // Nothing was recorded into it: a part opened as the app was killed.
                if (size == 0)
                {
                    TryDelete(file.Path);
                    continue;
                }

                Directory.CreateDirectory(recordings.StagingFolder);
                File.Copy(file.Path, staged, overwrite: true);

                var draft = new DraftAttachment(file.AttachmentId, file.Name, file.ContentType, size, staged);
                prepared.Add((file, await composer.PrepareAsync(draft, sendOriginals: true, cancellationToken)));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                TryDelete(staged);
                error ??= PartKept;
            }
        }

        if (prepared.Count == 0) return new([], error);

        var attached = await notes.AttachAsync(noteId, [.. prepared.Select(part => part.Metadata)], cancellationToken);
        if (attached is null) return new([], NoteGone);

        foreach (var (file, _) in prepared) TryDelete(file.Path);

        return new([.. prepared.Select(part => part.Metadata)], error);
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Already on the note; a later recovery finds it named there and deletes it then.
        }
    }
}
