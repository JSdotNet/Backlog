using System.Globalization;

using Backlog.UI.Components.Notes;

namespace Backlog.Mobile.UI.Recordings;

/// <summary>
/// What a meeting recording is called on its note
/// (<c>.devbook/domain/capture/features.md#meeting-recording</c>). The name carries no
/// extension: the attachment's content type says the format, which is how a player picks it.
/// </summary>
public static class RecordingNames
{
    /// <summary>A file a killed app left behind, attached on the next start.</summary>
    public const string Interrupted = "Recording (interrupted)";

    /// <summary>"Recording · 12:34" for the first part, "Recording · part 2 · 1:02:03" after.</summary>
    public static string Part(int number, TimeSpan duration)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(number, 1);

        return number == 1
            ? $"Recording · {Duration(duration)}"
            : string.Create(CultureInfo.InvariantCulture, $"Recording · part {number} · {Duration(duration)}");
    }

    /// <summary>"m:ss" under an hour, "h:mm:ss" from an hour; seconds are not rounded up. The
    /// bar's own format, so the timer and the name it leaves cannot disagree.</summary>
    public static string Duration(TimeSpan duration) => RecordingBar.FormatElapsed(duration);
}
