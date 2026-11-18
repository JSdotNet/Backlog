using Backlog.Mobile.UI.Notes;
using Backlog.Mobile.UI.TalkNotes;

namespace Backlog.Mobile.UI.Recordings;

/// <summary>
/// The sweep at the start of a process (<c>.devbook/arc42/06-runtime-view.md#meeting-recording</c>):
/// every part file a killed app left in <c>notes/recordings/</c> is attached to its note as
/// "Recording (interrupted)" and queued. A file whose note the phone does not hold stays where
/// it is, and so does one that cannot be attached this time.
/// </summary>
/// <remarks>
/// Run once per process, before anything can record: a sweep while a recording runs would
/// attach its live file. That is why the hosts run it after building the app, and the layout
/// never does — an Android activity is recreated while the recording service keeps going.
/// </remarks>
public sealed class RecordingRecovery(RecordingFiles files, TalkNoteComposer composer, NoteViewProjection notes)
{
    /// <summary>Attaches what was left and answers how many files it attached. One note's
    /// files failing does not hold up the next note's.</summary>
    public async Task<int> RecoverAsync(CancellationToken cancellationToken = default)
    {
        files.ClearStaging();

        var attached = 0;

        foreach (var note in files.Files().GroupBy(file => file.NoteId))
        {
            try
            {
                var outcome = await RecordingAttachments.AttachAsync(
                    composer,
                    files,
                    notes,
                    note.Key,
                    [.. note.Select(file => new RecordingToAttach(file.AttachmentId, RecordingNames.Interrupted, file.ContentType, file.Path))],
                    cancellationToken);

                attached += outcome.Attached.Count;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Its files stay in the folder for the next start.
            }
        }

        return attached;
    }
}
