namespace Backlog.UI.Components.Notes;

/// <summary>Where a meeting recording shown by <see cref="RecordingBar"/> is. There is no idle
/// state: a host with nothing recording shows no bar.</summary>
public enum RecordingBarState
{
    /// <summary>Capturing: the dot pulses and the timer runs.</summary>
    Recording,

    /// <summary>Held open, capturing nothing; Pause has become Resume.</summary>
    Paused,

    /// <summary>Stopped and being attached to the note; Stop is busy.</summary>
    Stopping,

    /// <summary>Ended on a failure, which the bar says in words.</summary>
    Failed,
}
