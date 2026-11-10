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
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly CancellationTokenSource _lifetime = new();

    /// <summary>The note being recorded into; a reference, so every thread reads it whole.</summary>
    private volatile ActiveNote? _active;
    private volatile bool _stopping;
    private volatile bool _faultStopQueued;
    private volatile string? _error;
    private volatile RecordingFailure? _lastFailure;
    private int _disposed;

    public NoteRecordingSession(IMeetingRecorder recorder, RecordingFiles files, TalkNoteComposer composer, NoteViewProjection notes)
    {
        ArgumentNullException.ThrowIfNull(recorder);
        ArgumentNullException.ThrowIfNull(files);
        ArgumentNullException.ThrowIfNull(composer);
        ArgumentNullException.ThrowIfNull(notes);

        _recorder = recorder;
        _files = files;
        _composer = composer;
        _notes = notes;
        _recorder.Changed += OnRecorderChanged;
    }

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

            _active = new ActiveNote(noteId);
            _lastFailure = null;
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

    public async ValueTask ResumeAsync()
    {
        if (_active is null) return;
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

        try
        {
            var stopped = await _recorder.StopAsync(cancellationToken);
            recorderStopped = true;
            error = stopped.Error;

            var parts = stopped.Parts.OrderBy(part => part.Number).ToList();
            if (parts.Count > 0)
            {
                var outcome = await RecordingAttachments.AttachAsync(
                    _composer,
                    _files,
                    _notes,
                    active.NoteId,
                    [.. parts.Select(part => new RecordingToAttach(part.AttachmentId, RecordingNames.Part(part.Number, part.Duration), part.ContentType, part.Path))],
                    cancellationToken);

                attached = outcome.Attached;
                error ??= outcome.Error;
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // The recorder's contract is to answer, not throw; one that throws is taken to have
            // stopped. Any part it wrote is still in the recordings folder for the next start.
            recorderStopped = true;
            error ??= ex.Message;
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
                if (error is not null) _lastFailure = new RecordingFailure(active.NoteId, error, _recorder.Elapsed);
            }

            _stopping = false;
            _faultStopQueued = false;
            Changed?.Invoke();
        }
    }

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
        if (_recorder.Fault is not null && _active is not null && !_stopping) QueueFaultStop();

        Changed?.Invoke();
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
