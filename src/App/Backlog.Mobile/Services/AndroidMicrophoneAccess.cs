namespace Backlog.Mobile.Services;

/// <summary>
/// The words a refused microphone is said in on the phone, for dictation's recorder, its
/// recogniser and the meeting recorder alike, so the three cannot drift apart. The browser
/// harness's own is <see cref="Backlog.Mobile.UI.Services.MicrophoneAccess.DeniedInBrowser"/>.
/// </summary>
internal static class AndroidMicrophoneAccess
{
    public const string Denied = "Microphone access was denied. Allow it in Android settings and try again.";
}
