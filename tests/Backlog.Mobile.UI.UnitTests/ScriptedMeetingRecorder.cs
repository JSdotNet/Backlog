using Backlog.Mobile.UI.Recordings;

namespace Backlog.Mobile.UI.UnitTests;

/// <summary>
/// A meeting recorder the test drives by hand. A start answers <see cref="StartAnswer"/>; a
/// stop writes each of <see cref="Parts"/> into the file the target names for it — the bytes a
/// real recorder would have streamed there — and answers them. <see cref="Fail"/> is a failure
/// while recording, as a full disk is: the recorder stops capturing, says why and raises
/// <see cref="Changed"/>, and a stop still hands back what was written.
/// </summary>
internal sealed class ScriptedMeetingRecorder : IMeetingRecorder
{
    private MeetingRecordingTarget? _target;
    /// <summary>The managed thread raising a fault right now, or 0.</summary>
    private volatile int _raisingThread;

    public bool Supported { get; set; } = true;

    /// <summary>What a start answers: <see cref="MeetingRecorderResult.Started"/> unless the test
    /// scripts a failure — a denied microphone, a busy one.</summary>
    public MeetingRecorderResult StartAnswer { get; set; } = MeetingRecorderResult.Started;

    /// <summary>The parts a stop writes, in order, with how long each plays.</summary>
    public List<(byte[] Bytes, TimeSpan Duration)> Parts { get; } = [AdtsPart(TimeSpan.FromSeconds(754))];

    /// <summary>Thrown by the next stop, for a recorder that breaks its contract.</summary>
    public Exception? StopThrows { get; set; }

    /// <summary>A failure the recorder raises while it starts, before the start has answered.</summary>
    public string? FailOnStart { get; set; }

    /// <summary>Runs once each part's file is written, before the stop answers.</summary>
    public Action<RecordingPartFile>? AfterPartWritten { get; set; }

    /// <summary>Runs as the stop is about to answer: the recorder has stopped.</summary>
    public Action? AfterStop { get; set; }

    /// <summary>Whether a stop ran on the thread that was raising a fault — inside the
    /// recorder's own callback, which on Android is its pipe reader.</summary>
    public bool StoppedOnRaisingThread { get; private set; }

    /// <summary>The note each start recorded into.</summary>
    public List<Guid> Started { get; } = [];

    public MeetingRecorderState State { get; private set; }

    public TimeSpan Elapsed { get; set; }

    public double Level { get; set; }

    public string? Fault { get; private set; }

    /// <summary>What has the microphone away from the recording; set by <see cref="Interrupt"/>.</summary>
    public MeetingRecorderInterruption Interruption { get; private set; }

    /// <summary>Whether this recorder says when its capture is silenced; when not, the session
    /// watches the level instead.</summary>
    public bool ReportsSilencing { get; set; }

    public event Action? Changed;

    /// <summary>A phone call taking the microphone, another app silencing the capture, or the
    /// microphone back: raised from the test's thread, as a platform callback would be.</summary>
    public void Interrupt(MeetingRecorderInterruption interruption)
    {
        Interruption = interruption;
        _raisingThread = Environment.CurrentManagedThreadId;

        try
        {
            Changed?.Invoke();
        }
        finally
        {
            _raisingThread = 0;
        }
    }

    /// <summary>Whether this recorder can measure the level at all; a browser with no
    /// AudioContext cannot, and its level of 0 means nothing.</summary>
    public bool MeasuresLevel { get; set; } = true;

    /// <summary>How many times the recording was asked to pause.</summary>
    public int PauseCount => Volatile.Read(ref _pauseCount);

    /// <summary>Whether a pause ran on the thread raising an interruption or a fault — inside the
    /// platform's own callback.</summary>
    public bool PausedOnRaisingThread { get; private set; }

    private int _pauseCount;

    /// <summary>One level tick, as the Android head's 100 ms poll raises it.</summary>
    public void Tick(double level)
    {
        Level = level;
        Changed?.Invoke();
    }

    public static (byte[] Bytes, TimeSpan Duration) AdtsPart(TimeSpan duration, int frames = 3) =>
        (AdtsStream.Of(Enumerable.Repeat(120, frames)), duration);

    /// <summary>How many times a host asked whether it can record.</summary>
    public int SupportAsked { get; private set; }

    public ValueTask<bool> IsSupportedAsync(CancellationToken cancellationToken = default)
    {
        SupportAsked++;
        return ValueTask.FromResult(Supported);
    }

    public Task<MeetingRecorderResult> StartAsync(MeetingRecordingTarget target, CancellationToken cancellationToken = default)
    {
        if (StartAnswer.IsError) return Task.FromResult(StartAnswer);

        _target = target;
        Fault = null;
        Started.Add(target.NoteId);
        Move(MeetingRecorderState.Recording);
        if (FailOnStart is { } fault) Fail(fault);
        return Task.FromResult(StartAnswer);
    }

    public ValueTask PauseAsync()
    {
        Interlocked.Increment(ref _pauseCount);
        if (_raisingThread == Environment.CurrentManagedThreadId) PausedOnRaisingThread = true;
        if (State == MeetingRecorderState.Recording) Move(MeetingRecorderState.Paused);
        return ValueTask.CompletedTask;
    }

    public ValueTask ResumeAsync()
    {
        if (State == MeetingRecorderState.Paused) Move(MeetingRecorderState.Recording);
        return ValueTask.CompletedTask;
    }

    /// <summary>A failure while recording, put into words.</summary>
    public void Fail(string fault)
    {
        Fault = fault;
        _raisingThread = Environment.CurrentManagedThreadId;

        try
        {
            Changed?.Invoke();
        }
        finally
        {
            _raisingThread = 0;
        }
    }

    public async Task<MeetingRecorderResult> StopAsync(CancellationToken cancellationToken = default)
    {
        if (_raisingThread == Environment.CurrentManagedThreadId) StoppedOnRaisingThread = true;
        if (StopThrows is { } thrown) throw thrown;
        if (_target is not { } target) return MeetingRecorderResult.Finished([]);

        Move(MeetingRecorderState.Stopping);

        var finished = new List<MeetingRecordingPart>();
        foreach (var (bytes, duration) in Parts)
        {
            var file = target.NewPart(finished.Count + 1, ".aac");
            await File.WriteAllBytesAsync(file.Path, bytes, cancellationToken);
            finished.Add(MeetingRecordingPart.Of(file, "audio/aac", duration, bytes.Length));
            AfterPartWritten?.Invoke(file);
        }

        _target = null;
        Move(MeetingRecorderState.Idle);
        AfterStop?.Invoke();

        return Fault is { } fault ? MeetingRecorderResult.Failed(fault, finished) : MeetingRecorderResult.Finished(finished);
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    private void Move(MeetingRecorderState state)
    {
        State = state;
        Changed?.Invoke();
    }
}
