namespace Backlog.Mobile.UI.Recordings;

/// <summary>
/// What the recording bar and the notification say while something has the microphone away from
/// a recording (<c>.devbook/domain/capture/features.md#meeting-recording</c>), and what the note
/// page says when its recording went to a note of its own.
/// </summary>
public static class RecordingNotices
{
    /// <summary>A telephony call paused the recording; it waits for Resume.</summary>
    public const string PhoneCall = "Paused: a phone call has the microphone.";

    /// <summary>The platform said the capture is silenced; the recording runs on.</summary>
    public const string Silenced = "Another app has the microphone. Nothing is being recorded.";

    /// <summary>The level has been 0 for ten seconds, on a recorder that cannot say why.</summary>
    public const string NoSound = "No sound is reaching the recording. Another app may have the microphone.";

    /// <summary>The note was deleted elsewhere while it recorded, so Stop made a new one.</summary>
    public static string Moved(string title) =>
        $"This note was deleted elsewhere. The recording was saved to a new note, {title}.";
}
