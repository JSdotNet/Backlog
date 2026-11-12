using System.Diagnostics;

using Microsoft.JSInterop;

namespace Backlog.Mobile.UI.Services;

/// <summary>
/// <see cref="IMeetingRecorder"/> over the browser's <c>MediaRecorder</c>, for the mobile web
/// harness (local ADR 0048, scope Q3): the browser cannot write ADTS, so it records
/// <c>audio/mp4</c> (AAC), which a note accepts, into one <c>.m4a</c> part with no rollover.
/// Where the browser cannot record <c>audio/mp4</c> at all, Record is disabled.
/// </summary>
/// <remarks>
/// <para>
/// The JS lives in <c>meeting-recorder.js</c>, beside dictation's <c>recorder.js</c>, which
/// this leaves alone. <c>MediaRecorder</c> runs with a timeslice, and every slice crosses to
/// the circuit as an <see cref="IJSStreamReference"/> — the harness is Blazor Server — and is
/// appended to the part file as it comes, so a closed tab leaves what was recorded in
/// <c>notes/recordings/</c> for the next start to attach.
/// </para>
/// <para>
/// Scoped per circuit, because it needs the circuit's <see cref="IJSRuntime"/>. The timer is
/// this side's clock, stopped while paused. The module ticks a few times a second while
/// recording, with the input level when it can measure one and 0 when it cannot, and each tick
/// raises <see cref="Changed"/>, which is what moves the bar's timer.
/// </para>
/// <para>
/// A failure while recording makes the session stop on a thread of its own, so a stop can run
/// off the circuit's dispatcher while slices and ticks arrive on it: the clock and the part
/// file are only touched under <see cref="_sync"/>, and the file's bytes under
/// <see cref="_writeGate"/>.
/// </para>
/// </remarks>
public sealed class WebMeetingRecorder(IJSRuntime js) : IMeetingRecorder
{
    private const string ModulePath = "./_content/Backlog.Mobile.UI/meeting-recorder.js";

    /// <summary>One slice a second: small enough that a closed tab loses little.</summary>
    private const int TimesliceMilliseconds = 1000;

    /// <summary>A slice is a second of 32–128 kbps audio; this is far past any real one.</summary>
    private const long MaxChunkBytes = 16L * 1024 * 1024;

    public const string ContentType = "audio/mp4";

    public const string NoSpace = "The recording could not be saved. The disk may be full.";

    public const string Busy = "A recording is already running.";

    private readonly SemaphoreSlim _moduleGate = new(1, 1);
    private readonly SemaphoreSlim _writeGate = new(1, 1);
    private readonly Lock _sync = new();
    private readonly Stopwatch _clock = new();

    private IJSObjectReference? _module;
    private DotNetObjectReference<WebMeetingRecorder>? _self;
    private RecordingPartFile? _file;
    private FileStream? _stream;
    private volatile MeetingRecorderState _state;
    private volatile string? _fault;

    /// <summary>Set once a write failed: later slices are dropped. A capture failure does not
    /// set it, so the slice the browser was still handing over is kept.</summary>
    private volatile bool _writeFaulted;
    private double _level;

    public MeetingRecorderState State => _state;

    public TimeSpan Elapsed
    {
        get
        {
            lock (_sync) return _clock.Elapsed;
        }
    }

    public double Level => Volatile.Read(ref _level);

    public string? Fault => _fault;

    /// <summary>Always none: a browser says nothing of who else has the microphone.</summary>
    public MeetingRecorderInterruption Interruption => MeetingRecorderInterruption.None;

    /// <summary>A browser cannot say its capture was silenced, so the session watches the level.</summary>
    public bool ReportsSilencing => false;

    /// <summary>Whether the last tick carried a measured level: <c>meeting-recorder.js</c> sends
    /// -1 from a browser with no <c>AudioContext</c>, whose level is no reading at all.</summary>
    public bool MeasuresLevel => _measuresLevel;

    private volatile bool _measuresLevel;

    public event Action? Changed;

    /// <inheritdoc />
    public async ValueTask<bool> IsSupportedAsync(CancellationToken cancellationToken = default)
    {
        var module = await LoadModuleAsync(cancellationToken);
        if (module is null) return false;

        try
        {
            return await module.InvokeAsync<bool>("isSupported", cancellationToken);
        }
        catch (JSException)
        {
            return false;
        }
        catch (Exception ex) when (IsBrowserGone(ex, cancellationToken))
        {
            return false;
        }
    }

    /// <inheritdoc />
    public async Task<MeetingRecorderResult> StartAsync(MeetingRecordingTarget target, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(target);

        if (_state != MeetingRecorderState.Idle || _stream is not null) return MeetingRecorderResult.Failed(Busy);

        var module = await LoadModuleAsync(cancellationToken);
        if (module is null) return MeetingRecorderResult.Failed(UnsupportedMeetingRecorder.NotHere);

        // Open before the module starts: its first slice may arrive before the start answers.
        try
        {
            var file = target.NewPart(1, ".m4a");
            var stream = new FileStream(file.Path, FileMode.CreateNew, FileAccess.Write, FileShare.Read);
            lock (_sync)
            {
                _file = file;
                _stream = stream;
                _clock.Reset();
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return MeetingRecorderResult.Failed(NoSpace);
        }

        _fault = null;
        _writeFaulted = false;
        Volatile.Write(ref _level, 0);
        _self ??= DotNetObjectReference.Create(this);

        string? error;
        try
        {
            error = await module.InvokeAsync<string?>("start", cancellationToken, _self, TimesliceMilliseconds);
        }
        catch (JSException ex)
        {
            error = ex.Message;
        }
        catch (Exception ex) when (IsBrowserGone(ex, cancellationToken))
        {
            error = "not-supported";
        }
        catch (OperationCanceledException)
        {
            await CallAsync("abort");
            await DiscardAsync();
            throw;
        }

        if (error is not null)
        {
            await DiscardAsync();
            return MeetingRecorderResult.Failed(Describe(error));
        }

        lock (_sync) _clock.Start();
        Move(MeetingRecorderState.Recording);
        return MeetingRecorderResult.Started;
    }

    /// <inheritdoc />
    public async ValueTask PauseAsync()
    {
        if (_state != MeetingRecorderState.Recording || _module is null) return;

        await CallAsync("pause");
        lock (_sync) _clock.Stop();
        Volatile.Write(ref _level, 0);
        Move(MeetingRecorderState.Paused);
    }

    /// <inheritdoc />
    public async ValueTask ResumeAsync()
    {
        if (_state != MeetingRecorderState.Paused || _module is null || _fault is not null) return;

        await CallAsync("resume");
        lock (_sync) _clock.Start();
        Move(MeetingRecorderState.Recording);
    }

    /// <inheritdoc />
    /// <remarks>Cancellation is honoured only before anything has stopped: once the browser is
    /// told to stop, the file is closed and handed back whatever the token says, so the state
    /// cannot be left at <see cref="MeetingRecorderState.Stopping"/>.</remarks>
    public async Task<MeetingRecorderResult> StopAsync(CancellationToken cancellationToken = default)
    {
        RecordingPartFile file;
        lock (_sync)
        {
            if (_stream is null || _file is null) return MeetingRecorderResult.Finished([]);
            file = _file;
        }

        cancellationToken.ThrowIfCancellationRequested();

        lock (_sync) _clock.Stop();
        Move(MeetingRecorderState.Stopping);

        // Resolves once the last slice has been handed over, through OnChunkAsync.
        await CallAsync("stop");

        long size;
        await _writeGate.WaitAsync(CancellationToken.None);
        try
        {
            size = await CloseAsync();
        }
        finally
        {
            _writeGate.Release();
        }

        TimeSpan elapsed;
        lock (_sync)
        {
            _file = null;
            elapsed = _clock.Elapsed;
        }

        Volatile.Write(ref _level, 0);
        Move(MeetingRecorderState.Idle);

        if (size <= 0)
        {
            TryDelete(file.Path);
            return MeetingRecorderResult.Failed(_fault ?? "Nothing was recorded.");
        }

        var part = MeetingRecordingPart.Of(file, ContentType, elapsed, size);
        return _fault is { } fault ? MeetingRecorderResult.Failed(fault, [part]) : MeetingRecorderResult.Finished([part]);
    }

    /// <summary>Called from <c>meeting-recorder.js</c> with each slice, in order.</summary>
    [JSInvokable]
    public async Task OnChunkAsync(IJSStreamReference chunk)
    {
        ArgumentNullException.ThrowIfNull(chunk);

        await using (chunk)
        {
            await _writeGate.WaitAsync();
            try
            {
                FileStream? stream;
                lock (_sync) stream = _stream;
                if (stream is null || _writeFaulted) return;

                await using var source = await chunk.OpenReadStreamAsync(MaxChunkBytes);
                await source.CopyToAsync(stream);
                await stream.FlushAsync();
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                _writeFaulted = true;
                Halt(NoSpace);
            }
            catch (Exception ex) when (ex is JSException || IsBrowserGone(ex, CancellationToken.None))
            {
                // The browser went mid-slice; the file keeps what reached it.
            }
            finally
            {
                _writeGate.Release();
            }
        }
    }

    /// <summary>Called from <c>meeting-recorder.js</c> a few times a second while recording,
    /// with the input level from 0 to 1, or -1 where the browser cannot measure one, which shows
    /// as 0 and clears <see cref="MeasuresLevel"/>. Each tick moves the timer.</summary>
    [JSInvokable]
    public void OnLevel(double level)
    {
        if (_state != MeetingRecorderState.Recording) return;

        _measuresLevel = level >= 0;
        Volatile.Write(ref _level, double.IsFinite(level) ? Math.Clamp(level, 0, 1) : 0);
        Changed?.Invoke();
    }

    /// <summary>Called from <c>meeting-recorder.js</c> when the microphone stopped
    /// on its own: a device unplugged, a recorder error.</summary>
    [JSInvokable]
    public Task OnFaultAsync(string error)
    {
        if (_state is MeetingRecorderState.Recording or MeetingRecorderState.Paused)
        {
            Halt(Describe(error));
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (_module is not null)
        {
            try
            {
                // Abandoned, not stopped: nothing more is handed to a reference disposed below.
                await _module.InvokeVoidAsync("abort");
                await _module.DisposeAsync();
            }
            catch (JSException)
            {
            }
            catch (Exception ex) when (IsBrowserGone(ex, CancellationToken.None))
            {
                // The circuit is already gone.
            }

            _module = null;
        }

        // The file stays in notes/recordings/ for the next start to attach.
        await _writeGate.WaitAsync();
        try
        {
            await CloseAsync();
        }
        finally
        {
            _writeGate.Release();
        }

        lock (_sync) _clock.Stop();
        _state = MeetingRecorderState.Idle;
        _self?.Dispose();
        _self = null;
    }

    /// <summary>A failure while recording: capture stops, the reason is kept for the stop, and
    /// <see cref="Changed"/> tells the session, which stops and attaches what was written.</summary>
    private void Halt(string fault)
    {
        if (_fault is not null) return;

        _fault = fault;
        lock (_sync) _clock.Stop();
        Volatile.Write(ref _level, 0);
        _ = CallAsync("pause").AsTask();
        Changed?.Invoke();
    }

    private void Move(MeetingRecorderState state)
    {
        _state = state;
        Changed?.Invoke();
    }

    /// <summary>Closes the part file and answers its size; 0 when none was open.</summary>
    private async Task<long> CloseAsync()
    {
        FileStream? stream;
        lock (_sync)
        {
            stream = _stream;
            _stream = null;
        }

        if (stream is null) return 0;

        long size;
        try
        {
            await stream.FlushAsync();
            size = stream.Length;
        }
        catch (IOException)
        {
            _fault ??= NoSpace;
            size = stream.Length;
        }
        finally
        {
            try
            {
                await stream.DisposeAsync();
            }
            catch (IOException)
            {
                _fault ??= NoSpace;
            }
        }

        return size;
    }

    private async Task DiscardAsync()
    {
        await CloseAsync();

        RecordingPartFile? file;
        lock (_sync)
        {
            file = _file;
            _file = null;
        }

        if (file is not null) TryDelete(file.Path);
    }

    private async ValueTask CallAsync(string method)
    {
        if (_module is not { } module) return;

        try
        {
            await module.InvokeVoidAsync(method);
        }
        catch (JSException)
        {
            // Nothing to act on: the recorder had already stopped.
        }
        catch (Exception ex) when (IsBrowserGone(ex, CancellationToken.None))
        {
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Left for recovery, which drops an empty file.
        }
    }

    /// <summary>The module, or null where there is no browser to load it from. A cancelled
    /// caller is told so, not that the host cannot record.</summary>
    private async ValueTask<IJSObjectReference?> LoadModuleAsync(CancellationToken cancellationToken)
    {
        if (_module is not null) return _module;

        await _moduleGate.WaitAsync(cancellationToken);
        try
        {
            return _module ??= await js.InvokeAsync<IJSObjectReference>("import", cancellationToken, ModulePath);
        }
        catch (JSException)
        {
            return null;
        }
        catch (Exception ex) when (IsBrowserGone(ex, cancellationToken))
        {
            return null;
        }
        finally
        {
            _moduleGate.Release();
        }
    }

    /// <summary>No browser to talk to right now; see <see cref="WebSpeechTranscriber"/>. A
    /// cancellation the caller asked for is not one: it is let through.</summary>
    private static bool IsBrowserGone(Exception ex, CancellationToken cancellationToken) =>
        ex is JSDisconnectedException or InvalidOperationException or ObjectDisposedException
        || (ex is OperationCanceledException && !cancellationToken.IsCancellationRequested);

    /// <summary>The <c>getUserMedia</c> and recorder error names, in words a person can act on.</summary>
    private static string Describe(string? error) => error switch
    {
        "NotAllowedError" or "SecurityError" => MicrophoneAccess.DeniedInBrowser,
        "NotFoundError" or "OverconstrainedError" => "No microphone was found.",
        "NotReadableError" or "AbortError" or "audio-capture" => "The microphone could not be read. Another app may be using it.",
        "track-ended" => "The microphone stopped recording.",
        "not-supported" or "aborted" => UnsupportedMeetingRecorder.NotHere,
        null or "" => "Recording failed.",
        _ => $"Recording failed ({error})."
    };
}
