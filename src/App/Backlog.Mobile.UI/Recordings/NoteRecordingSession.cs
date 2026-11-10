using Backlog.Mobile.UI.Notes;
using Backlog.Mobile.UI.Services;
using Backlog.Mobile.UI.TalkNotes;
using Backlog.Modules.Sync.Abstractions.DataTransferObjects;

namespace Backlog.Mobile.UI.Recordings;

/// <summary>
/// The meeting recording the note page and the notification both drive
/// (<c>.devbook/domain/capture/features.md#meeting-recording</c>): which note it is for, where
/// the recorder is, and, on Stop, each finished part attached to that note as
/// "Recording · &lt;duration&gt;" through the note's own outbox path.
/// </summary>
/// <remarks>
/// <para>
/// The recording belongs to this session, not to the page: leaving the note does not stop it,
/// and a Stop from the notification attaches it the same way. That is why the Android head
/// registers it as a singleton — the notification reaches it from outside any scope — and the
/// browser harness per circuit, where its recorder needs the circuit's JavaScript.
/// </para>
/// <para>
/// Nothing here throws for an ordinary failure. A denied microphone, a busy one, a full disk or
/// a note that is gone ends up in <see cref="Error"/>, for the bar to say. A failure while
/// recording stops the recording — on a thread of its own, never inside the recorder's callback
/// — and attaches what was written. A part that cannot be attached stays in
/// <c>notes/recordings/</c>, and the next start attaches it.
/// </para>
/// <para>
/// What else Android does to a recording is handled here too, from the recorder's
/// <see cref="IMeetingRecorder.Changed"/> ticks. A phone call pauses it, and only Resume carries
/// it on: the end of the call does not. A capture another app silenced, or a level held at 0 on
/// a recorder that cannot say so, is a <see cref="Notice"/> and nothing more. Free space at the
/// floor <see cref="RecordingSpace"/> sets stops it the way a failure does. And a note deleted
/// elsewhere while it records is not written to on Stop: the parts go to a new note of their
/// own, which <see cref="LastMoved"/> names.
/// </para>
/// </remarks>
public sealed class NoteRecordingSession : IAsyncDisposable, IDisposable
{
    public const string AlreadyRecording = "Another note is recording. Stop it first.";
    public const string NoteMissing = "This note is not on the phone.";
    public const string PartKept = RecordingAttachments.PartKept;

    private readonly IMeetingRecorder _recorder;
    private readonly RecordingFiles _files;
    private readonly TalkNoteComposer _composer;
    private readonly NoteViewProjection _notes;
    private readonly IFreeSpace _freeSpace;
    private readonly TimeProvider _clock;
    private readonly FlatLevelDetector _flatLevel = new();
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly CancellationTokenSource _lifetime = new();

    /// <summary>The note being recorded into; a reference, so every thread reads it whole.</summary>
    private volatile ActiveNote? _active;
    private volatile bool _stopping;
    private volatile bool _faultStopQueued;
    private volatile string? _error;
    private volatile RecordingFailure? _lastFailure;
    private volatile RecordingMoved? _lastMoved;

    /// <summary>Why a stop the session asked for itself ended the recording, or <c>null</c>.</summary>
    private volatile string? _stopReason;

    /// <summary>Set as a call begins; cleared by Resume, or by the call's end.</summary>
    private volatile bool _pausedByCall;
    private volatile bool _flat;

    /// <summary>The recorder's interruption as last seen, so a call is acted on as it begins.</summary>
    private int _interruption;

    /// <summary>Moved on by every call pause queued and every one made stale, so a queued pause
    /// can tell it is still the latest.</summary>
    private int _callPause;

    /// <summary>When free space is next looked at, in UTC ticks.</summary>
    private long _nextSpaceCheck;
    private int _disposed;

    public NoteRecordingSession(
        IMeetingRecorder recorder,
        RecordingFiles files,
        TalkNoteComposer composer,
        NoteViewProjection notes,
        IFreeSpace freeSpace,
        TimeProvider clock)
    {
        ArgumentNullException.ThrowIfNull(recorder);
        ArgumentNullException.ThrowIfNull(files);
        ArgumentNullException.ThrowIfNull(composer);
        ArgumentNullException.ThrowIfNull(notes);
        ArgumentNullException.ThrowIfNull(freeSpace);
        ArgumentNullException.ThrowIfNull(clock);

        _recorder = recorder;
        _files = files;
        _composer = composer;
        _notes = notes;
        _freeSpace = freeSpace;
        _clock = clock;
        _recorder.Changed += OnRecorderChanged;
    }

    /// <summary>How often free space is looked at while recording: the recorder ticks ten times
    /// a second, and a disk does not fill that fast.</summary>
    public static readonly TimeSpan SpaceCheckInterval = TimeSpan.FromSeconds(10);

    /// <summary>Runs what the session does off the thread that raised it — a call's pause. A pool
    /// thread; a test sets its own to choose the order the pool would not promise.</summary>
    internal Func<Func<Task>, Task> RunInBackground { get; set; } = work => Task.Run(work);

    /// <summary>Raised when anything the bar shows changed — the timer and the level
    /// included. May be raised off the render thread.</summary>
    public event Action? Changed;

    /// <summary>The note being recorded into, or <c>null</c> when nothing is.</summary>
    public Guid? NoteId => _active?.NoteId;

    /// <summary>Where the recording is: idle unless a note is being recorded into.</summary>
    public MeetingRecorderState State => _active is null
        ? MeetingRecorderState.Idle
        : _stopping ? MeetingRecorderState.Stopping : _recorder.State;

    public TimeSpan Elapsed => _recorder.Elapsed;

    /// <summary>The input level, 0 to 1.</summary>
    public double Level => _recorder.Level;

    /// <summary>The last failure, in words, or <c>null</c>. Cleared by the next start.</summary>
    public string? Error => _error;

    /// <summary>What the last Stop put on the note.</summary>
    public IReadOnlyList<AttachmentMetadata> LastAttached { get; private set; } = [];

    /// <summary>
    /// What has the microphone away from the recording, in words, or <c>null</c>: a phone call
    /// that paused it, another app that silenced it, or a level held at 0 that suggests one did.
    /// Only while a recording is live; once a call ends, the bar says plain "Paused".
    /// </summary>
    public string? Notice
    {
        get
        {
            if (_active is null || _stopping) return null;
            if (_pausedByCall) return RecordingNotices.PhoneCall;
            if (_recorder.State != MeetingRecorderState.Recording) return null;

            // A call still on after a Resume is a capture that hears silence too.
            if (_recorder.Interruption is not MeetingRecorderInterruption.None) return RecordingNotices.Silenced;

            return _flat ? RecordingNotices.NoSound : null;
        }
    }

    /// <summary>
    /// The last recording whose note was deleted elsewhere while it ran, and the note Stop made
    /// for it instead, so the deleted note's page can say where it went. Cleared by the next start.
    /// </summary>
    public RecordingMoved? LastMoved => _lastMoved;

    /// <summary>
    /// The last recording that ended on a failure, and the note it was for, so that note can
    /// say why when it is next open — the failure may come while the person is elsewhere in
    /// the app. Cleared by the next recording that starts, and by <see cref="DismissFailure"/>.
    /// A start that was refused is not one: it never recorded, and only <see cref="Error"/>
    /// says why.
    /// </summary>
    public RecordingFailure? LastFailure => _lastFailure;

    /// <summary>Takes <see cref="LastFailure"/> away once the person has read it on
    /// <paramref name="noteId"/>.</summary>
    public void DismissFailure(Guid noteId)
    {
        if (_lastFailure?.NoteId != noteId) return;

        _lastFailure = null;
        Changed?.Invoke();
    }

    /// <summary>Whether <paramref name="noteId"/> is the note being recorded into.</summary>
    public bool IsRecording(Guid noteId) => _active?.NoteId == noteId;

    /// <summary>Whether this host can record; <c>false</c> where its recorder throws.</summary>
    public async ValueTask<bool> IsSupportedAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            return await _recorder.IsSupportedAsync(cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return false;
        }
    }

    /// <summary>Starts recording into <paramref name="noteId"/>, which the phone must hold.
    /// Answers whether it started; when not, <see cref="Error"/> says why.</summary>
    public async Task<bool> StartAsync(Guid noteId, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);

        try
        {
            if (_active is not null) return Refuse(AlreadyRecording);
            if (_notes.Find(noteId) is null) return Refuse(NoteMissing);

            // Before the recorder, so a refusal never starts its service. Unknown is no refusal.
            if (_freeSpace.AvailableBytes(_files.Folder) is { } free && !RecordingSpace.CanStart(free))
            {
                return Refuse(RecordingSpace.Refusal(free));
            }

            _error = null;

            MeetingRecorderResult started;
            try
            {
                started = await _recorder.StartAsync(_files.Target(noteId), cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                started = MeetingRecorderResult.Failed(ex.Message);
            }

            if (started.IsError) return Refuse(started.Error!);

            _stopReason = null;
            _pausedByCall = false;
            _flat = false;
            _flatLevel.Reset();
            Volatile.Write(ref _interruption, (int)_recorder.Interruption);
            Interlocked.Exchange(ref _nextSpaceCheck, (_clock.GetUtcNow() + SpaceCheckInterval).UtcTicks);
            _active = new ActiveNote(noteId);
            _lastFailure = null;
            _lastMoved = null;
        }
        finally
        {
            _gate.Release();
        }

        // A fault the recorder raised while it was starting found no note to stop for.
        if (_recorder.Fault is not null) QueueFaultStop();

        Changed?.Invoke();
        return true;
    }

    public async ValueTask PauseAsync()
    {
        if (_active is null) return;
        await GuardAsync(_recorder.PauseAsync);
    }

    /// <summary>Carries a paused recording on, a call's pause included: only this ends one.</summary>
    public async ValueTask ResumeAsync()
    {
        if (_active is null) return;

        ForgetCallPause();
        await GuardAsync(_recorder.ResumeAsync);
    }

    /// <summary>
    /// Ends the recording and attaches its parts to the note, in order. Safe to call when
    /// nothing is recording. Cancelled while the recorder is still stopping, the recording goes
    /// on; cancelled after, the session is idle and the parts wait in the recordings folder for
    /// the next start.
    /// </summary>
    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);

        try
        {
            await StopCoreAsync(cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    public ValueTask DisposeAsync()
    {
        Dispose();
        return ValueTask.CompletedTask;
    }

    /// <summary>The same as <see cref="DisposeAsync"/>, which awaits nothing: a scope disposed
    /// synchronously — a test renderer's, a host's on shutdown — refuses a service that is
    /// only async-disposable.</summary>
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;

        _recorder.Changed -= OnRecorderChanged;

        // Ends a stop still waiting for the gate. The gate is not disposed: a waiter on a
        // disposed SemaphoreSlim is never released.
        _lifetime.Cancel();
        _lifetime.Dispose();
    }

    private async Task StopCoreAsync(CancellationToken cancellationToken)
    {
        if (_active is not { } active) return;

        _stopping = true;
        Changed?.Invoke();

        var recorderStopped = false;
        string? error = null;
        IReadOnlyList<AttachmentMetadata> attached = [];
        RecordingMoved? moved = null;

        try
        {
            var stopped = await _recorder.StopAsync(cancellationToken);
            recorderStopped = true;
            var forSpace = _stopReason is not null;
            error = forSpace ? RecordingSpace.StoppedForSpace : stopped.Error;

            var parts = stopped.Parts.OrderBy(part => part.Number).ToList();
            if (parts.Count > 0)
            {
                var outcome = await RecordingAttachments.AttachAsync(
                    _composer,
                    _files,
                    _notes,
                    active.NoteId,
                    [.. parts.Select(part => new RecordingToAttach(part.AttachmentId, RecordingNames.Part(part.Number, part.Duration), part.ContentType, part.Path))],
                    cancellationToken,
                    newNoteTitle: RecordingNames.NoteTitle(DateOnly.FromDateTime(_clock.GetLocalNow().DateTime)));

                attached = outcome.Attached;
                if (outcome.NewNote is { } created) moved = new RecordingMoved(active.NoteId, created.Id, created.Title);

                // A space stop says where what was recorded went, and only where that is true;
                // a part that did not get there is said beside any reason, never hidden by it.
                if (forSpace && outcome.Error is null && attached.Count > 0)
                {
                    error = moved is { } went ? RecordingSpace.StoppedOnNewNote(went.Title) : RecordingSpace.Stopped;
                }

                error = Join(error, outcome.Error);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // The recorder's contract is to answer, not throw; one that throws is taken to have
            // stopped. Any part it wrote is still in the recordings folder for the next start.
            recorderStopped = true;
            error = Join(error, ex.Message);
        }
        finally
        {
            // Idle only once the recorder has stopped: a stop cancelled while it was stopping
            // leaves the recording on, as the recorder still has it.
            if (recorderStopped)
            {
                _active = null;
                _error = error;
                LastAttached = attached;
                _lastMoved = moved;
                _stopReason = null;
                _pausedByCall = false;
                _flat = false;
                if (error is not null) _lastFailure = new RecordingFailure(active.NoteId, error, _recorder.Elapsed);
            }

            _stopping = false;
            _faultStopQueued = false;
            Changed?.Invoke();
        }
    }

    private static string? Join(string? reason, string? attachError) =>
        reason is null ? attachError
        : attachError is null ? reason
        : $"{reason} {attachError}";

    private async ValueTask GuardAsync(Func<ValueTask> action)
    {
        try
        {
            await action();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _error = ex.Message;
        }

        Changed?.Invoke();
    }

    private bool Refuse(string error)
    {
        _error = error;
        Changed?.Invoke();
        return false;
    }

    private void OnRecorderChanged()
    {
        // A failure while recording: the recorder has stopped capturing, so what it wrote is
        // attached now rather than when someone presses Stop.
        var live = _active is not null && !_stopping;
        if (_recorder.Fault is not null && live) QueueFaultStop();

        if (live)
        {
            OnInterruption();
            WatchLevel();
            WatchSpace();
        }

        Changed?.Invoke();
    }

    /// <summary>
    /// A call pauses the recording as it begins, once, so a Resume during the call is the
    /// person's to make; its end clears the reason but not the pause.
    /// </summary>
    private void OnInterruption()
    {
        var now = _recorder.Interruption;
        var before = (MeetingRecorderInterruption)Interlocked.Exchange(ref _interruption, (int)now);
        if (now == before) return;

        if (now is MeetingRecorderInterruption.PhoneCall)
        {
            _pausedByCall = true;
            if (_recorder.State == MeetingRecorderState.Recording) QueuePause();
        }
        else if (before is MeetingRecorderInterruption.PhoneCall)
        {
            ForgetCallPause();
        }
    }

    /// <summary>A level held at 0 while recording, on a recorder that cannot say it was silenced
    /// but can measure the level: one that cannot measure it reports 0 and means nothing.</summary>
    private void WatchLevel()
    {
        if (_recorder.State == MeetingRecorderState.Recording && !_recorder.ReportsSilencing && _recorder.MeasuresLevel)
        {
            _flat = _flatLevel.Observe(_recorder.Level, _clock.GetUtcNow());
            return;
        }

        // Paused, or a recorder that says so itself: the count starts again on the next tick.
        _flatLevel.Reset();
        _flat = false;
    }

    /// <summary>At most every <see cref="SpaceCheckInterval"/>; the tick that claims the look
    /// moves the next one on, so two threads never both look.</summary>
    private void WatchSpace()
    {
        var now = _clock.GetUtcNow().UtcTicks;
        var due = Interlocked.Read(ref _nextSpaceCheck);
        if (now < due) return;
        if (Interlocked.CompareExchange(ref _nextSpaceCheck, now + SpaceCheckInterval.Ticks, due) != due) return;

        if (_freeSpace.AvailableBytes(_files.Folder) is { } free && RecordingSpace.MustStop(free))
        {
            _stopReason ??= RecordingSpace.StoppedForSpace;
            QueueFaultStop();
        }
    }

    /// <summary>
    /// Pauses on a thread of its own, for the reason <see cref="QueueFaultStop"/> gives. By the
    /// time it runs the call may have ended or the person pressed Resume; either moves
    /// <see cref="_callPause"/> on, and a pause for a call that is over is skipped.
    /// </summary>
    private void QueuePause()
    {
        var call = Interlocked.Increment(ref _callPause);

        _ = RunInBackground(async () =>
        {
            if (Volatile.Read(ref _callPause) != call || !_pausedByCall) return;

            try
            {
                await PauseAsync();
            }
            catch (ObjectDisposedException)
            {
                // The session went with its scope.
            }
        });
    }

    /// <summary>Ends any call pause still waiting to run: the call is over, or resumed past.</summary>
    private void ForgetCallPause()
    {
        _pausedByCall = false;
        Interlocked.Increment(ref _callPause);
    }

    /// <summary>Stops on a thread of its own: the recorder raises its fault from its own
    /// thread — the pipe reader on Android — and a stop run there would wait on itself.</summary>
    private void QueueFaultStop()
    {
        if (_faultStopQueued) return;
        _faultStopQueued = true;

        CancellationToken lifetime;
        try
        {
            lifetime = _lifetime.Token;
        }
        catch (ObjectDisposedException)
        {
            return;
        }

        _ = Task.Run(async () =>
        {
            try
            {
                await StopAsync(lifetime);
            }
            catch (OperationCanceledException)
            {
                // The session went with its scope; the next start recovers the files.
            }
            catch (ObjectDisposedException)
            {
            }
        });
    }

    private sealed record ActiveNote(Guid NoteId);
}

/// <summary>A recording that ended on a failure: the note it was for, why in words, and how
/// long it had run.</summary>
public sealed record RecordingFailure(Guid NoteId, string Error, TimeSpan Elapsed);

/// <summary>A recording whose note was deleted elsewhere while it ran: that note, and the note
/// Stop made for its parts instead, under its title.</summary>
public sealed record RecordingMoved(Guid FromNoteId, Guid ToNoteId, string Title);
