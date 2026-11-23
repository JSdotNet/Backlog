using System.Globalization;

namespace Backlog.Mobile.UI.Recordings;

/// <summary>
/// How much room a meeting recording needs on the phone
/// (<c>.devbook/domain/capture/features.md#meeting-recording</c>). A recording stops cleanly at
/// <see cref="StopAtBytes"/> free, so what it wrote can still be attached: Stop copies each part
/// once more on its way to the outbox, at most one part's size. A start needs that floor and two
/// parts on top — the first part, and its copy — so it does not stop as soon as it began.
/// </summary>
public static class RecordingSpace
{
    private const long Megabyte = 1024 * 1024;

    /// <summary>200 MB: at or below it, a running recording stops.</summary>
    public const long StopAtBytes = 200 * Megabyte;

    /// <summary>248 MB: the floor and two 24 MiB parts, which a start needs free.</summary>
    public const long StartNeedsBytes = StopAtBytes + (2 * AdtsPartWriter.DefaultPartCap);

    /// <summary>Why a running recording stopped for space, alone: the start of every sentence
    /// below, and the whole of it when nothing reached a note.</summary>
    public const string StoppedForSpace = "The recording stopped because the phone has less than 200 MB free.";

    /// <summary>Why a running recording stopped for space, for the bar's failure, when every
    /// part reached its note.</summary>
    public const string Stopped = StoppedForSpace + " What was recorded is on the note.";

    /// <summary>The same, when the note was deleted elsewhere and the parts went to a new one.</summary>
    public static string StoppedOnNewNote(string title) => $"{StoppedForSpace} What was recorded is on a new note, {title}.";

    public static bool CanStart(long availableBytes) => availableBytes >= StartNeedsBytes;

    public static bool MustStop(long availableBytes) => availableBytes <= StopAtBytes;

    /// <summary>A start refused for space, in the words the app update's refusal uses.</summary>
    public static string Refusal(long availableBytes) => string.Create(
        CultureInfo.InvariantCulture,
        $"There is not enough storage on this phone to record: it needs {Megabytes(StartNeedsBytes)} MB free and has {Megabytes(availableBytes)} MB.");

    /// <summary>Whole megabytes, rounded down so a refusal never claims more room than there is.</summary>
    private static long Megabytes(long bytes) => Math.Max(bytes, 0) / Megabyte;
}
