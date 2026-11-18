using System.Globalization;

using Backlog.Mobile.UI.Services;
using Backlog.Mobile.UI.TalkNotes;

namespace Backlog.Mobile.UI.Recordings;

/// <summary>A part file found in the recordings folder, read back from its name.</summary>
public sealed record RecordingFile(Guid NoteId, Guid AttachmentId, int Part, string Path, string ContentType);

/// <summary>
/// Where a meeting recording is written while it runs: <c>notes/recordings/</c> beside the
/// database, one file a part, named so a file a killed app left behind still says which note
/// it belongs to (<c>.devbook/arc42/06-runtime-view.md#meeting-recording</c>). A file leaves the
/// folder when it is attached, into the talk notes' outbox folder.
/// </summary>
/// <remarks>
/// A part is named <c>&lt;noteId:N&gt;_&lt;attachmentId:N&gt;.aac</c>, and a later one
/// <c>…_p2.aac</c>; <c>.m4a</c> where the host records MPEG-4. A part is copied into the outbox
/// and deleted from here only once its note names it, so a failure or a kill on the way leaves
/// it here for <see cref="RecordingRecovery"/>.
/// </remarks>
public sealed class RecordingFiles
{
    private const string PartMarker = "_p";

    public RecordingFiles(string folder)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(folder);
        Folder = folder;
    }

    public string Folder { get; }

    /// <summary>Where a part's copy waits on its way into the outbox folder, so the part itself
    /// stays in <see cref="Folder"/> until its note names it. Never read as a recording.</summary>
    public string StagingFolder => Path.Combine(Folder, "staging");

    /// <summary>The copy of a part on its way into the outbox folder.</summary>
    public string StagingPath(Guid attachmentId) => Path.Combine(StagingFolder, attachmentId.ToString("N"));

    /// <summary>Drops the copies a killed attach left half made; their parts are still in
    /// <see cref="Folder"/>. Only before anything records.</summary>
    public void ClearStaging()
    {
        try
        {
            if (Directory.Exists(StagingFolder)) Directory.Delete(StagingFolder, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // A stray copy costs space, not a recording; the next start tries again.
        }
    }

    /// <summary>What a recorder is handed to record <paramref name="noteId"/>'s meeting into.</summary>
    public MeetingRecordingTarget Target(Guid noteId) => new(noteId, Folder);

    /// <summary>The name of a part's file.</summary>
    public static string FileName(Guid noteId, Guid attachmentId, int part, string extension)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(part, 1);
        ArgumentException.ThrowIfNullOrWhiteSpace(extension);

        var suffix = part == 1 ? string.Empty : string.Create(CultureInfo.InvariantCulture, $"{PartMarker}{part}");
        var dot = extension.StartsWith('.') ? extension : "." + extension;

        return $"{noteId:N}_{attachmentId:N}{suffix}{dot.ToLowerInvariant()}";
    }

    /// <summary>Every recording file in the folder, by note and then part. A file not named the
    /// way <see cref="FileName"/> names one, or not audio the phone takes, is not one.</summary>
    public IReadOnlyList<RecordingFile> Files()
    {
        if (!Directory.Exists(Folder)) return [];

        return [.. Directory.EnumerateFiles(Folder)
            .Select(path => TryRead(path, out var file) ? file : null)
            .OfType<RecordingFile>()
            .OrderBy(file => file.NoteId)
            .ThenBy(file => file.Part)
            .ThenBy(file => file.AttachmentId)];
    }

    /// <summary>Reads a part file's note, attachment and part back from its name.</summary>
    public static bool TryRead(string path, out RecordingFile file)
    {
        file = null!;

        var contentType = AttachmentRules.ContentTypeOf(path, null);
        if (!AttachmentRules.IsAudio(contentType)) return false;

        var stem = Path.GetFileNameWithoutExtension(path);
        var part = 1;

        var marker = stem.IndexOf(PartMarker, StringComparison.Ordinal);
        if (marker >= 0)
        {
            if (!int.TryParse(stem.AsSpan(marker + PartMarker.Length), NumberStyles.None, CultureInfo.InvariantCulture, out part) || part < 2)
            {
                return false;
            }

            stem = stem[..marker];
        }

        var ids = stem.Split('_');
        if (ids.Length != 2
            || !Guid.TryParseExact(ids[0], "N", out var noteId)
            || !Guid.TryParseExact(ids[1], "N", out var attachmentId))
        {
            return false;
        }

        file = new RecordingFile(noteId, attachmentId, part, path, contentType);
        return true;
    }
}
