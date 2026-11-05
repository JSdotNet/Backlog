namespace Backlog.Mobile.UI.Services;

/// <summary>
/// Whether the cloud dictation engine is still worth trying in this app run (local ADR 0045
/// §6). Once the sync service has said it cannot transcribe here — no route, no model
/// configured, no answer — every later turn listens on the device, and the cloud is tried
/// again only after the next start.
/// </summary>
/// <remarks>
/// A singleton, so "the app run" is the process: the phone's, and in the browser harness
/// the harness's across every circuit, as one pretend phone. A fresh process starts
/// available.
/// </remarks>
public sealed class CloudDictationAvailability
{
    private int _unavailable;

    /// <summary>Whether a cloud turn may be tried.</summary>
    public bool IsAvailable => Volatile.Read(ref _unavailable) == 0;

    /// <summary>The service said it cannot transcribe here; stop asking it until the next start.</summary>
    public void MarkUnavailable() => Volatile.Write(ref _unavailable, 1);
}
