using Backlog.Mobile.UI.Notes;
using Backlog.Mobile.UI.TalkNotes;
using Backlog.Modules.Sync.Abstractions.DataTransferObjects;

namespace Backlog.Mobile.UI.Recordings;

/// <summary>A recording file to attach, and the name it goes on the note under.</summary>
internal sealed record RecordingToAttach(Guid AttachmentId, string Name, string ContentType, string Path);

/// <summary>What attaching came to: the files now on a note, why any are not, and the note made
/// for them when theirs was gone.</summary>
internal sealed record RecordingAttachOutcome(IReadOnlyList<AttachmentMetadata> Attached, string? Error, NoteViewRow? NewNote = null);

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
/// <para>
/// A note that is gone — deleted or archived elsewhere, and hidden by a pull — gets the parts
/// only when the caller names a title for a new note: a stop does, and the parts go to a new
/// note under it, through <see cref="NoteViewProjection.CreateAsync"/> and the same outbox, in
/// the same order — the note written first, each part deleted after. Recovery names none, and
/// leaves them in the folder.
/// </para>
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
        CancellationToken cancellationToken,
        string? newNoteTitle = null,
        Func<Guid, IReadOnlyList<AttachmentMetadata>, CancellationToken, Task<NoteViewRow?>>? attach = null)
    {
        // The write is a parameter only so a test can hide the note between the look and it.
        attach ??= (id, added, token) => notes.AttachAsync(id, added, token);

        var note = notes.Find(noteId);
        if (note is null && newNoteTitle is null) return new([], NoteGone);

        var prepared = new List<(RecordingToAttach File, AttachmentMetadata Metadata)>();
        var error = await PrepareAsync(composer, recordings, files, note?.Attachments ?? [], prepared, cancellationToken);

        if (prepared.Count == 0) return new([], error);

        NoteViewRow? created = null;

        // Null too for a note hidden since the look above: a pull folded its tombstone meanwhile.
        var attached = note is null ? null : await attach(noteId, [.. prepared.Select(part => part.Metadata)], cancellationToken);
        if (attached is null)
        {
            if (newNoteTitle is null) return new([], NoteGone);

            if (note is not null)
            {
                // The cap was counted against the deleted note's files. The new note holds only
                // these parts, so whatever the cap held back, or failed, is tried again under its
                // own; a part already gone from the folder went with the deleted note.
                var rest = files.Where(file => prepared.All(part => part.File != file) && File.Exists(file.Path)).ToList();
                error = await PrepareAsync(composer, recordings, rest, [.. prepared.Select(part => part.Metadata)], prepared, cancellationToken);
                prepared.Sort((left, right) => IndexOf(files, left.File).CompareTo(IndexOf(files, right.File)));
            }

            created = await notes.CreateAsync(newNoteTitle, null, [.. prepared.Select(part => part.Metadata)], cancellationToken);
        }

        foreach (var (file, _) in prepared) TryDelete(file.Path);

        return new([.. prepared.Select(part => part.Metadata)], error, created);
    }

    /// <summary>
    /// Readies each of <paramref name="files"/> for a note that already names
    /// <paramref name="held"/>, adding what got there to <paramref name="prepared"/>; answers why
    /// any did not. A part past the per-note cap, and one that fails, stays in the folder.
    /// </summary>
    private static async Task<string?> PrepareAsync(
        TalkNoteComposer composer,
        RecordingFiles recordings,
        IReadOnlyList<RecordingToAttach> files,
        IReadOnlyList<AttachmentMetadata> held,
        List<(RecordingToAttach File, AttachmentMetadata Metadata)> prepared,
        CancellationToken cancellationToken)
    {
        string? error = null;
        var added = 0;

        foreach (var file in files)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (held.Any(attached => attached.Id == file.AttachmentId))
            {
                // Attached before the process died, on the way to deleting it.
                TryDelete(file.Path);
                continue;
            }

            if (held.Count + added >= AttachmentRules.MaximumPerNote)
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
                added++;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                TryDelete(staged);
                error ??= PartKept;
            }
        }

        return error;
    }

    private static int IndexOf(IReadOnlyList<RecordingToAttach> files, RecordingToAttach file)
    {
        for (var index = 0; index < files.Count; index++)
        {
            if (files[index] == file) return index;
        }

        return files.Count;
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
