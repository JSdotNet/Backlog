namespace Backlog.Mobile.UI.Services;

/// <summary>
/// The words a refused microphone is said in, shared by every recorder of one host — dictation
/// and the meeting recording alike — so the two cannot drift apart. The Android head keeps its
/// own sentence beside its recorders; this is the browser harness's.
/// </summary>
public static class MicrophoneAccess
{
    /// <summary>A browser that refused the microphone, as <c>getUserMedia</c>'s
    /// <c>NotAllowedError</c> says.</summary>
    public const string DeniedInBrowser = "Microphone access was denied. Allow it for this site and try again.";
}
