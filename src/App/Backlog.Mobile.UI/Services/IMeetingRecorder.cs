using Backlog.Mobile.UI.Recordings;

namespace Backlog.Mobile.UI.Services;

/// <summary>Where a meeting recorder is in a recording.</summary>
public enum MeetingRecorderState
{
    /// <summary>Not recording.</summary>
    Idle,

    /// <summary>Capturing the microphone.</summary>
    Recording,

    /// <summary>Holding the recording open, capturing nothing.</summary>
    Paused,

    /// <summary>Finishing the last part's file.</summary>
    Stopping,
}

/// <summary>What has taken the microphone away from a recording, as the host sees it.</summary>
public enum MeetingRecorderInterruption
{
    /// <summary>Nothing: the recording hears the room.</summary>
    None,

    /// <summary>A telephony call is on; it always gets the microphone.</summary>
    PhoneCall,

    /// <summary>Another app has the microphone, and the capture receives silence.</summary>
    Silenced,
}

/// <summary>
/// The note a recording is for and the folder its part files go in
/// (<c>notes/recordings/</c>), handed to <see cref="IMeetingRecorder.StartAsync"/>. The recorder
/// asks it for each part's file, so every host names its files the way
/// <see cref="RecordingFiles"/> reads them back after a killed app.
/// </summary>
public sealed class MeetingRecordingTarget
{
    public MeetingRecordingTarget(Guid noteId, string folder)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(folder);

        NoteId = noteId;
        Folder = folder;
    }

    public Guid NoteId { get; }

    public string Folder { get; }

    /// <summary>
    /// The file part <paramref name="number"/> is written to, under an attachment id of its
    /// own, minted here; the folder is created when it is missing.
    /// </summary>
    /// <param name="number">1 for the first part.</param>
    /// <param name="extension"><c>.aac</c> for an ADTS stream, <c>.m4a</c> for MPEG-4.</param>
    public RecordingPartFile NewPart(int number, string extension)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(number, 1);
        ArgumentException.ThrowIfNullOrWhiteSpace(extension);

        Directory.CreateDirectory(Folder);

        var attachmentId = Guid.CreateVersion7();
        return new RecordingPartFile(attachmentId, number, Path.Combine(Folder, RecordingFiles.FileName(NoteId, attachmentId, number, extension)));
    }
}

/// <summary>One part's file, before anything is in it.</summary>
/// <param name="AttachmentId">The id the part is attached to its note under.</param>
public sealed record RecordingPartFile(Guid AttachmentId, int Number, string Path);

/// <summary>One part a recorder finished: its file, what is in it, and how long it plays.</summary>
/// <param name="ContentType"><c>audio/aac</c> for ADTS, <c>audio/mp4</c> for MPEG-4.</param>
public sealed record MeetingRecordingPart(
    Guid AttachmentId,
    int Number,
    string Path,
    string ContentType,
    TimeSpan Duration,
    long SizeBytes)
{
    public static MeetingRecordingPart Of(RecordingPartFile file, string contentType, TimeSpan duration, long sizeBytes)
    {
        ArgumentNullException.ThrowIfNull(file);
        return new(file.AttachmentId, file.Number, file.Path, contentType, duration, sizeBytes);
    }
}

/// <summary>
/// What a start or a stop came to: the parts written, a reason in words, or both — a stop
/// after a failure still hands back what reached the disk.
/// </summary>
/// <remarks>
/// A failure is a result for the reason <see cref="AudioRecording"/> gives: a denied
/// microphone, a busy one, a full disk or a host that cannot record are ordinary answers the
/// screen puts into words.
/// </remarks>
/// <param name="Parts">The finished parts in order; empty on a start.</param>
/// <param name="Error">A message fit to show a person, or <c>null</c>.</param>
public sealed record MeetingRecorderResult(IReadOnlyList<MeetingRecordingPart> Parts, string? Error)
{
    /// <summary>The recording began.</summary>
    public static MeetingRecorderResult Started { get; } = new([], null);

    /// <summary>The recording ended with these parts.</summary>
    public static MeetingRecorderResult Finished(IReadOnlyList<MeetingRecordingPart> parts) => new(parts ?? [], null);

    /// <summary>It did not record, or stopped recording, and why; with what it kept, if anything.</summary>
    public static MeetingRecorderResult Failed(string error, IReadOnlyList<MeetingRecordingPart>? kept = null) => new(kept ?? [], error);

    public bool IsError => Error is not null;
}

/// <summary>
/// The phone's microphone as a meeting recording sees it (local ADR 0048): long, pausable, and
/// written to part files as it goes, so a killed app loses nothing it had. It is not the
/// dictation recorder (<see cref="IAudioRecorder"/>), whose turns are short and kept in memory.
/// </summary>
/// <remarks>
/// <para>
/// One implementation per host. The Android head records AAC in ADTS through
/// <c>MediaRecorder</c> into a pipe whose read end goes through <see cref="AdtsPartWriter"/>,
/// so a part rolls over at 24 MiB with no gap. The browser harness records <c>audio/mp4</c>
/// into one <c>.m4a</c> file, with no rollover. Each asks <see cref="MeetingRecordingTarget.NewPart"/>
/// for its files.
/// </para>
/// <para>
/// One recording at a time. Nothing here throws for an ordinary failure: start and stop answer
/// a <see cref="MeetingRecorderResult"/>, and a failure while recording sets <see cref="Fault"/>,
/// stops capturing and raises <see cref="Changed"/>; <see cref="StopAsync"/> then hands back what
/// was written.
/// </para>
/// </remarks>
public interface IMeetingRecorder : IAsyncDisposable
{
    MeetingRecorderState State { get; }

    /// <summary>The audio recorded so far, pauses left out.</summary>
    TimeSpan Elapsed { get; }

    /// <summary>The input level, 0 (silence) to 1, for the bar's meter.</summary>
    double Level { get; }

    /// <summary>What went wrong while recording, in words, or <c>null</c>. Cleared by the next start.</summary>
    string? Fault { get; }

    /// <summary>What has the microphone away from the recording right now. Not a fault: the
    /// recording goes on, and <see cref="NoteRecordingSession"/> decides what it means.</summary>
    MeetingRecorderInterruption Interruption { get; }

    /// <summary>Whether this recorder can say that its capture was silenced. Where it cannot,
    /// the session reads a level held at 0 as a hint instead.</summary>
    bool ReportsSilencing { get; }

    /// <summary>Whether <see cref="Level"/> is measured at all. A host that cannot measure it
    /// reports 0, which then says nothing about silence.</summary>
    bool MeasuresLevel { get; }

    /// <summary>Raised when the state, the level, the elapsed time, the fault or the
    /// interruption changed. May be raised off the render thread.</summary>
    event Action? Changed;

    /// <summary>Whether this host can record at all; <c>false</c> rather than a throw where it
    /// cannot, and while a component is prerendering.</summary>
    ValueTask<bool> IsSupportedAsync(CancellationToken cancellationToken = default);

    /// <summary>Asks for the microphone the first time, and starts writing part 1 of
    /// <paramref name="target"/>.</summary>
    Task<MeetingRecorderResult> StartAsync(MeetingRecordingTarget target, CancellationToken cancellationToken = default);

    /// <summary>Holds the recording; safe to call when not recording.</summary>
    ValueTask PauseAsync();

    /// <summary>Carries a paused recording on in the same part; safe to call when not paused.</summary>
    ValueTask ResumeAsync();

    /// <summary>Ends the recording and answers its parts in order, every file closed. Answers no
    /// parts when nothing was recording.</summary>
    Task<MeetingRecorderResult> StopAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// The recorder of a host that has none of its own: it cannot record, so the note page's
/// Record stays disabled. Every host that records registers its own before
/// <see cref="MobileShellServiceCollectionExtensions.AddMobileShell"/>.
/// </summary>
public sealed class UnsupportedMeetingRecorder : IMeetingRecorder
{
    public const string NotHere = "Recording is not available here.";

    public MeetingRecorderState State => MeetingRecorderState.Idle;

    public TimeSpan Elapsed => TimeSpan.Zero;

    public double Level => 0;

    public string? Fault => null;

    public MeetingRecorderInterruption Interruption => MeetingRecorderInterruption.None;

    public bool ReportsSilencing => false;

    public bool MeasuresLevel => false;

    public event Action? Changed
    {
        add { }
        remove { }
    }

    public ValueTask<bool> IsSupportedAsync(CancellationToken cancellationToken = default) => ValueTask.FromResult(false);

    public Task<MeetingRecorderResult> StartAsync(MeetingRecordingTarget target, CancellationToken cancellationToken = default) =>
        Task.FromResult(MeetingRecorderResult.Failed(NotHere));

    public ValueTask PauseAsync() => ValueTask.CompletedTask;

    public ValueTask ResumeAsync() => ValueTask.CompletedTask;

    public Task<MeetingRecorderResult> StopAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(MeetingRecorderResult.Finished([]));

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
