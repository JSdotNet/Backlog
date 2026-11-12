using System.Diagnostics;

using Android.Media;
using Android.OS;

using Backlog.Mobile.UI.Recordings;
using Backlog.Mobile.UI.Services;

using Microsoft.Extensions.Logging;

using OperationCanceledException = System.OperationCanceledException;

namespace Backlog.Mobile.Services;

/// <summary>
/// <see cref="IMeetingRecorder"/> over Android's own <see cref="MediaRecorder"/> (local ADR 0048):
/// AAC in an ADTS stream, 16 kHz mono at 32 kbps, split into part files of at most 24 MiB by
/// <see cref="AdtsPartWriter"/>, while <see cref="RecordingService"/> keeps the microphone open
/// with the app in the background.
/// </summary>
/// <remarks>
/// <para>
/// The recorder writes into a pipe, not a file. <c>setNextOutputFile</c> cannot roll an ADTS
/// recording — only the MPEG-4 writer supports it — and a stop and a restart would leave a gap.
/// A reader task takes the pipe's other end and hands it to <see cref="AdtsPartWriter"/>, which
/// cuts between frames, so a rollover drops and repeats nothing.
/// </para>
/// <para>
/// The foreground service starts before the microphone, from the person's press on the visible
/// note: Android 11 and later let a microphone service start only while the app is on screen, and
/// refuse it with an exception otherwise, which is answered here as words.
/// </para>
/// <para>
/// A failure while recording — a full disk, the microphone taken by another app — sets
/// <see cref="Fault"/> and stops capturing; the reader keeps draining the pipe so the recorder's
/// own stop is never left waiting on a full one. <see cref="StopAsync"/> then hands back every
/// part that reached the disk.
/// </para>
/// </remarks>
public sealed class AndroidMeetingRecorder(ILogger<AndroidMeetingRecorder> logger) : IMeetingRecorder
{
    private const string ContentType = "audio/aac";
    private const string Extension = ".aac";

    // Speech, kept long: 32 kbps mono puts a 24 MiB part at about an hour and forty minutes.
    private const int SamplingRate = 16_000;
    private const int BitRate = 32_000;

    private const int MaxAmplitude = 32_767;
    private static readonly TimeSpan LevelInterval = TimeSpan.FromMilliseconds(100);

    // How long a stopped recorder gets to close its end of the pipe before the read end is closed
    // under the reader.
    private static readonly TimeSpan DrainTimeout = TimeSpan.FromSeconds(5);

    // Android allows a service started in the foreground five seconds to say so; past that the
    // start has failed either way.
    private static readonly TimeSpan PromoteTimeout = TimeSpan.FromSeconds(5);

    private const string PermissionDenied = AndroidMicrophoneAccess.Denied;

    private const string Busy = "The microphone could not be started. Another app may be using it.";
    private const string NotOnScreen = "Recording can only start while Backlog is on screen.";
    private const string AlreadyRunning = "A recording is already running.";
    private const string NoSpace = "The recording could not be written. The phone may be out of space.";
    private const string MicrophoneLost = "The microphone stopped recording.";
    private const string CouldNotWrite = "The recording could not be written.";

    private readonly Lock _gate = new();
    private readonly Stopwatch _clock = new();

    // AudioManager's MODE_CALL_SCREENING (API 30) and MODE_CALL_REDIRECT (API 33), by value: an
    // older phone never reports them, so no version check is needed to compare against them.
    private const int ModeCallScreening = 4;
    private const int ModeCallRedirect = 5;

    private Capture? _capture;
    private Timer? _levelTimer;
    private MeetingRecorderState _state;
    private double _level;
    private string? _fault;
    private bool _inCall;
    private bool _silenced;
    private AudioManager? _audio;
    private Java.Lang.Object? _modeListener;

    public MeetingRecorderState State
    {
        get
        {
            lock (_gate) return _state;
        }
    }

    public TimeSpan Elapsed
    {
        get
        {
            lock (_gate) return _clock.Elapsed;
        }
    }

    public double Level
    {
        get
        {
            lock (_gate) return _level;
        }
    }

    public string? Fault
    {
        get
        {
            lock (_gate) return _fault;
        }
    }

    /// <summary>
    /// A telephony call, read from the audio mode, which needs no phone-state permission:
    /// <c>MODE_IN_CALL</c>, call screening or call redirect. A VoIP call
    /// (<c>MODE_IN_COMMUNICATION</c>) is not one; it silences the capture, which Android 10 and
    /// later report through the recording callback instead.
    /// </summary>
    public MeetingRecorderInterruption Interruption
    {
        get
        {
            lock (_gate)
            {
                return _inCall ? MeetingRecorderInterruption.PhoneCall
                    : _silenced ? MeetingRecorderInterruption.Silenced
                    : MeetingRecorderInterruption.None;
            }
        }
    }

    /// <summary><c>MediaRecorder</c>'s recording callback and <c>isClientSilenced</c> are API 29.</summary>
    public bool ReportsSilencing => OperatingSystem.IsAndroidVersionAtLeast(29);

    /// <summary><c>MediaRecorder.getMaxAmplitude</c> always measures.</summary>
    public bool MeasuresLevel => true;

    public event Action? Changed;

    public ValueTask<bool> IsSupportedAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var context = Microsoft.Maui.ApplicationModel.Platform.AppContext;
            return ValueTask.FromResult(
                context.PackageManager?.HasSystemFeature(Android.Content.PM.PackageManager.FeatureMicrophone) ?? false);
        }
        catch (Java.Lang.Exception)
        {
            return ValueTask.FromResult(false);
        }
    }

    public async Task<MeetingRecorderResult> StartAsync(MeetingRecordingTarget target, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(target);

        lock (_gate)
        {
            if (_state != MeetingRecorderState.Idle) return MeetingRecorderResult.Failed(AlreadyRunning);

            // Held while the permission prompt is up, so a second press cannot start a second recorder.
            _state = MeetingRecorderState.Stopping;
            _fault = null;
        }

        var started = MeetingRecorderResult.Failed(Busy);
        try
        {
            started = await StartCoreAsync(target, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning("A meeting recording did not start: {Error}", ex.GetType().Name);
        }
        finally
        {
            // Whatever ended the start, a failed one leaves the recorder ready for the next.
            if (started.IsError)
            {
                lock (_gate) _state = MeetingRecorderState.Idle;
            }
        }

        Changed?.Invoke();
        return started;
    }

    public ValueTask PauseAsync()
    {
        lock (_gate)
        {
            if (_state != MeetingRecorderState.Recording || _capture is null) return ValueTask.CompletedTask;

            if (!_capture.Pause()) return ValueTask.CompletedTask;

            _clock.Stop();
            _level = 0;
            _state = MeetingRecorderState.Paused;
        }

        Changed?.Invoke();
        return ValueTask.CompletedTask;
    }

    public ValueTask ResumeAsync()
    {
        lock (_gate)
        {
            if (_state != MeetingRecorderState.Paused || _capture is null) return ValueTask.CompletedTask;

            if (!_capture.Resume()) return ValueTask.CompletedTask;

            _clock.Start();
            _state = MeetingRecorderState.Recording;
        }

        Changed?.Invoke();
        return ValueTask.CompletedTask;
    }

    public async Task<MeetingRecorderResult> StopAsync(CancellationToken cancellationToken = default)
    {
        Capture? capture;
        lock (_gate)
        {
            capture = _capture;
            if (capture is null) return MeetingRecorderResult.Finished([]);

            _capture = null;
            _state = MeetingRecorderState.Stopping;
            _clock.Stop();
            _level = 0;
            _inCall = false;
            _silenced = false;
        }

        StopLevelTimer();
        UnwatchCalls();
        Changed?.Invoke();

        IReadOnlyList<MeetingRecordingPart> parts = [];
        string? fault;
        try
        {
            parts = await capture.FinishAsync(DrainTimeout);
        }
        catch (Exception ex)
        {
            logger.LogWarning("A meeting recording did not stop cleanly: {Error}", ex.GetType().Name);
        }
        finally
        {
            StopService();

            // Never left at Stopping: a recorder stuck there refuses every later start.
            lock (_gate)
            {
                _state = MeetingRecorderState.Idle;
                fault = _fault;
            }
        }

        Changed?.Invoke();
        return fault is null ? MeetingRecorderResult.Finished(parts) : MeetingRecorderResult.Failed(fault, parts);
    }

    public async ValueTask DisposeAsync()
    {
        // The parts stay in the recordings folder either way; the next start would attach them
        // as interrupted, so a stop that attaches them now is the kinder end.
        await StopAsync();
    }

    private async Task<MeetingRecorderResult> StartCoreAsync(MeetingRecordingTarget target, CancellationToken cancellationToken)
    {
        PermissionStatus permission;
        try
        {
            permission = await MainThread.InvokeOnMainThreadAsync(Permissions.RequestAsync<Permissions.Microphone>);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // No current activity to ask from answers InvalidOperationException; either way no
            // microphone.
            permission = PermissionStatus.Denied;
        }

        if (permission != PermissionStatus.Granted) return MeetingRecorderResult.Failed(PermissionDenied);

        await AskToNotifyAsync();

        if (cancellationToken.IsCancellationRequested) return MeetingRecorderResult.Failed("Recording was cancelled.");

        var context = Microsoft.Maui.ApplicationModel.Platform.AppContext;

        // The service first: it is what Android refuses when the app is not on screen. The
        // microphone waits until the service is in the foreground, so no failure below can stop
        // a service that has not yet called startForeground — which Android answers with a crash.
        bool promoted;
        try
        {
            promoted = await RecordingService.StartAsync(context).WaitAsync(PromoteTimeout, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            StopService();
            throw;
        }
        catch (Exception ex) when (ex is Java.Lang.Exception or TimeoutException)
        {
            logger.LogWarning("The recording service did not start: {Error}", ex.GetType().Name);
            promoted = false;
        }

        if (!promoted)
        {
            StopService();
            return MeetingRecorderResult.Failed(NotOnScreen);
        }

        var capture = new Capture(target, OnFault, OnSilenced);
        try
        {
            capture.Start(context);
        }
        catch (Exception ex)
        {
            logger.LogWarning("The microphone did not start: {Error}", ex.GetType().Name);
            try
            {
                await capture.AbandonAsync(DrainTimeout);
            }
            catch (Exception abandon) when (abandon is not OperationCanceledException)
            {
                logger.LogWarning("The failed start was not cleaned up: {Error}", abandon.GetType().Name);
            }
            finally
            {
                StopService();
            }
            return MeetingRecorderResult.Failed(Busy);
        }

        lock (_gate)
        {
            _capture = capture;
            _state = MeetingRecorderState.Recording;
            _level = 0;
            _clock.Restart();

            // Under the gate with the state, so a Stop that follows always finds the timer to end.
            Interlocked.Exchange(ref _levelTimer, new Timer(_ => PollLevel(), null, LevelInterval, LevelInterval))?.Dispose();
        }

        WatchCalls(context);
        return MeetingRecorderResult.Started;
    }

    /// <summary>
    /// Follows the audio mode for a call: a listener from Android 12, the level tick before it.
    /// A recording started during a call starts paused by none; the session acts on a call as it
    /// begins.
    /// </summary>
    private void WatchCalls(Android.Content.Context context)
    {
        try
        {
            var audio = context.GetSystemService(Android.Content.Context.AudioService) as AudioManager;
            if (audio is null) return;

            lock (_gate)
            {
                _audio = audio;
                _inCall = IsCall((int)audio.Mode);
            }

            if (OperatingSystem.IsAndroidVersionAtLeast(31) && context.MainExecutor is { } executor)
            {
                var listener = new ModeListener(OnModeChanged);
                audio.AddOnModeChangedListener(executor, listener);
                lock (_gate) _modeListener = listener;
            }
        }
        catch (Java.Lang.Exception ex)
        {
            // No call detection is still a recording; a call then reaches it as a fault, if at all.
            logger.LogWarning("Calls are not followed for this recording: {Error}", ex.GetType().Name);
        }
    }

    private void UnwatchCalls()
    {
        AudioManager? audio;
        Java.Lang.Object? listener;
        lock (_gate)
        {
            audio = _audio;
            listener = _modeListener;
            _audio = null;
            _modeListener = null;
        }

        if (audio is null || listener is null || !OperatingSystem.IsAndroidVersionAtLeast(31)) return;

        try
        {
            audio.RemoveOnModeChangedListener((AudioManager.IOnModeChangedListener)listener);
        }
        catch (Java.Lang.Exception)
        {
            // Already gone with the recording.
        }
        finally
        {
            listener.Dispose();
        }
    }

    private void OnModeChanged(int mode)
    {
        if (SetInCall(IsCall(mode))) Changed?.Invoke();
    }

    /// <summary>The mode read on the level tick, before Android 12 has a listener for it.</summary>
    private bool PollMode()
    {
        AudioManager? audio;
        lock (_gate) audio = _audio;
        if (audio is null) return false;

        try
        {
            return SetInCall(IsCall((int)audio.Mode));
        }
        catch (Java.Lang.Exception)
        {
            return false;
        }
    }

    /// <summary>Answers whether the call state changed.</summary>
    private bool SetInCall(bool inCall)
    {
        lock (_gate)
        {
            if (_capture is null || _inCall == inCall) return false;
            _inCall = inCall;
        }

        logger.LogInformation(inCall ? "A phone call took the microphone from a recording." : "The phone call ended.");
        return true;
    }

    private static bool IsCall(int mode) =>
        mode == (int)Mode.InCall || mode == ModeCallScreening || mode == ModeCallRedirect;

    private void OnSilenced(Capture capture, bool silenced)
    {
        lock (_gate)
        {
            if (!ReferenceEquals(_capture, capture) || _silenced == silenced) return;
            _silenced = silenced;
        }

        logger.LogInformation(silenced ? "Another app silenced a recording." : "A recording hears the microphone again.");
        Changed?.Invoke();
    }

    /// <summary>
    /// Android 13 and later show the recording's notification only once the person allowed
    /// notifications. <see cref="MainActivity"/> asks at start; a person who has not answered yet
    /// is asked again here. The recording runs whatever they answer.
    /// </summary>
    private static async Task AskToNotifyAsync()
    {
        if (!OperatingSystem.IsAndroidVersionAtLeast(33)) return;

        try
        {
            await MainThread.InvokeOnMainThreadAsync(async () =>
            {
                if (await Permissions.CheckStatusAsync<Permissions.PostNotifications>() != PermissionStatus.Granted)
                {
                    await Permissions.RequestAsync<Permissions.PostNotifications>();
                }
            });
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // No notification is still a recording.
        }
    }

    private void PollLevel()
    {
        Capture? capture;
        MeetingRecorderState state;
        lock (_gate)
        {
            capture = _capture;
            state = _state;
        }

        if (capture is null) return;

        // Paused too: a call's end has to be seen while the recording waits for Resume.
        var callChanged = !OperatingSystem.IsAndroidVersionAtLeast(31) && PollMode();

        if (state != MeetingRecorderState.Recording)
        {
            if (callChanged) Changed?.Invoke();
            return;
        }

        var level = Math.Clamp((double)capture.Amplitude() / MaxAmplitude, 0, 1);

        lock (_gate)
        {
            if (!ReferenceEquals(_capture, capture)) return;
            _level = level;
        }

        // The tick is also what moves the bar's timer.
        Changed?.Invoke();
    }

    private void OnFault(Capture capture, string fault)
    {
        lock (_gate)
        {
            if (!ReferenceEquals(_capture, capture) || _fault is not null) return;

            _fault = fault;
            _level = 0;
        }

        logger.LogWarning("A meeting recording stopped: {Fault}", fault);

        // Stops capturing now; the session's Stop collects the parts. Raised from the pool, not
        // inline: a fault comes from the pipe reader, and a Stop run on that thread would wait
        // on a recorder whose only drainer it is.
        _ = Task.Run(() =>
        {
            capture.Halt();
            Changed?.Invoke();
        });
    }

    private void StopLevelTimer()
    {
        var timer = Interlocked.Exchange(ref _levelTimer, null);
        timer?.Dispose();
    }

    private void StopService()
    {
        try
        {
            RecordingService.Stop(Microsoft.Maui.ApplicationModel.Platform.AppContext);
        }
        catch (Java.Lang.Exception ex)
        {
            logger.LogWarning("The recording service did not stop: {Error}", ex.GetType().Name);
        }
    }

    /// <summary>The audio mode as it changes, from Android 12.</summary>
    [System.Runtime.Versioning.SupportedOSPlatform("android31.0")]
    private sealed class ModeListener(Action<int> changed) : Java.Lang.Object, AudioManager.IOnModeChangedListener
    {
        public void OnModeChanged(int mode) => changed(mode);
    }

    /// <summary>
    /// One recording: the <see cref="MediaRecorder"/>, the pipe it writes into, and the reader
    /// that feeds the pipe through <see cref="AdtsPartWriter"/> into part files.
    /// </summary>
    private sealed class Capture(MeetingRecordingTarget target, Action<Capture, string> onFault, Action<Capture, bool> onSilenced)
    {
        private readonly Lock _gate = new();
        private readonly Dictionary<int, RecordingPartFile> _files = [];

        private MediaRecorder? _recorder;
        private AudioManager.AudioRecordingCallback? _silencing;
        private ParcelFileDescriptor? _read;
        private Task? _reader;
        private AdtsPartWriter? _writer;
        private IReadOnlyList<AdtsPart>? _parts;
        private bool _halted;

        public void Start(Android.Content.Context context)
        {
            var pipe = ParcelFileDescriptor.CreatePipe()
                ?? throw new IOException("Android made no pipe.");

            _read = pipe[0];
            var write = pipe[1];

            var recorder = OperatingSystem.IsAndroidVersionAtLeast(31)
                ? new MediaRecorder(context)
#pragma warning disable CA1422 // The constructor API 31 replaced; the head runs from 24.
                : new MediaRecorder();
#pragma warning restore CA1422

            lock (_gate) _recorder = recorder;

            try
            {
                recorder.SetAudioSource(AudioSource.Mic);
                recorder.SetOutputFormat(OutputFormat.AacAdts);
                recorder.SetAudioEncoder(AudioEncoder.Aac);
                recorder.SetAudioChannels(1);
                recorder.SetAudioSamplingRate(SamplingRate);
                recorder.SetAudioEncodingBitRate(BitRate);
                recorder.SetOutputFile(write.FileDescriptor);
                recorder.Error += (_, _) => onFault(this, MicrophoneLost);

                recorder.Prepare();
            }
            finally
            {
                // The recorder holds its own copy from Prepare on. Closing ours is what lets the
                // reader see the end of the stream once the recorder lets go of its copy.
                write.Close();
                write.Dispose();
            }

            _writer = new AdtsPartWriter(OpenPart);
            _reader = Task.Run(Read);

            // Before the start, as Android asks: from 10 on, a capture another app takes the
            // input from keeps running on silence, and this is how it says so.
            if (OperatingSystem.IsAndroidVersionAtLeast(29) && context.MainExecutor is { } executor)
            {
                var silencing = new SilencingCallback(silenced => onSilenced(this, silenced));
                recorder.RegisterAudioRecordingCallback(executor, silencing);
                lock (_gate) _silencing = silencing;
            }

            recorder.Start();
        }

        public bool Pause() => Call(recorder => recorder.Pause());

        public bool Resume() => Call(recorder => recorder.Resume());

        /// <summary>The loudest sample since the last call, 0 when there is no recorder.</summary>
        public int Amplitude()
        {
            lock (_gate)
            {
                try
                {
                    return _halted || _recorder is null ? 0 : _recorder.MaxAmplitude;
                }
                catch (Java.Lang.Exception)
                {
                    return 0;
                }
            }
        }

        /// <summary>Stops and releases the recorder once; its end of the pipe closes with it.</summary>
        public void Halt()
        {
            MediaRecorder? recorder;
            AudioManager.AudioRecordingCallback? silencing;
            lock (_gate)
            {
                if (_halted) return;
                _halted = true;
                recorder = _recorder;
                _recorder = null;
                silencing = _silencing;
                _silencing = null;
            }

            if (recorder is null) return;

            if (silencing is not null && OperatingSystem.IsAndroidVersionAtLeast(29))
            {
                try
                {
                    recorder.UnregisterAudioRecordingCallback(silencing);
                }
                catch (Java.Lang.Exception)
                {
                    // Released with the recorder either way.
                }
            }

            try
            {
                recorder.Stop();
            }
            catch (Java.Lang.Exception)
            {
                // A stop with no audio yet throws; what was written is still in the pipe.
            }

            try
            {
                recorder.Release();
            }
            catch (Java.Lang.Exception)
            {
            }

            recorder.Dispose();
        }

        /// <summary>Stops the recorder, waits for the reader to write the last frame, and answers
        /// every part in order.</summary>
        public async Task<IReadOnlyList<MeetingRecordingPart>> FinishAsync(TimeSpan drainTimeout)
        {
            Halt();
            await DrainAsync(drainTimeout);

            var parts = _parts ?? [];
            var finished = new List<MeetingRecordingPart>(parts.Count);

            foreach (var part in parts)
            {
                if (_files.TryGetValue(part.Number, out var file))
                {
                    finished.Add(MeetingRecordingPart.Of(file, ContentType, part.Duration, part.SizeBytes));
                }
            }

            return finished;
        }

        /// <summary>A start that failed: stops whatever began and deletes what it wrote, so the
        /// next start does not attach a fragment as interrupted.</summary>
        public async Task AbandonAsync(TimeSpan drainTimeout)
        {
            Halt();
            await DrainAsync(drainTimeout);

            foreach (var file in _files.Values)
            {
                try
                {
                    File.Delete(file.Path);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    // Recovery attaches it on the next start; a stray second is not worth more.
                }
            }
        }

        private async Task DrainAsync(TimeSpan drainTimeout)
        {
            if (_reader is { } reader)
            {
                try
                {
                    await reader.WaitAsync(drainTimeout);
                }
                catch (TimeoutException)
                {
                    // The recorder never closed its end: closing ours ends the read with an error.
                    CloseRead();
                    await reader;
                }
            }
            else
            {
                CloseRead();
            }
        }

        private System.IO.Stream OpenPart(int number)
        {
            var file = target.NewPart(number, Extension);
            _files[number] = file;
            return File.Create(file.Path);
        }

        private void Read()
        {
            var writer = _writer!;
            var faulted = false;
            var buffer = new byte[8 * 1024];

            try
            {
                using var input = new ParcelFileDescriptor.AutoCloseInputStream(_read);

                while (true)
                {
                    int count;
                    try
                    {
                        count = input.Read(buffer);
                    }
                    catch (Java.IO.IOException)
                    {
                        // The read end was closed under us after a stop that did not drain.
                        break;
                    }

                    if (count < 0) break;

                    // After a fault the writer ignores what it is given, but the pipe is still
                    // read: a full one blocks the recorder, and its stop never returns.
                    if (count == 0 || writer.IsFaulted) continue;

                    try
                    {
                        writer.Write(buffer.AsSpan(0, count));
                    }
                    catch (Exception ex)
                    {
                        // A full disk, or a part file that could not be opened at a rollover.
                        faulted = true;
                        onFault(this, ex is IOException ? NoSpace : CouldNotWrite);
                    }
                }

                // The stream ended without a stop: the recorder died.
                bool halted;
                lock (_gate) halted = _halted;
                if (!halted && !faulted) onFault(this, MicrophoneLost);
            }
            catch (Exception)
            {
                // The pipe itself failed. Nothing reads it any more, so it is closed, which ends the
                // recorder's writes instead of blocking them.
                onFault(this, MicrophoneLost);
                CloseRead();
            }
            finally
            {
                // Closes every part, a faulted one included, and answers what reached the disk.
                _parts = writer.Complete();
            }
        }

        private void CloseRead()
        {
            try
            {
                _read?.Close();
            }
            catch (Java.IO.IOException)
            {
            }
        }

        /// <summary>Reports whether this recorder's own capture is silenced. The configurations a
        /// <c>MediaRecorder</c>'s callback hears are its own.</summary>
        [System.Runtime.Versioning.SupportedOSPlatform("android29.0")]
        private sealed class SilencingCallback(Action<bool> changed) : AudioManager.AudioRecordingCallback
        {
            public override void OnRecordingConfigChanged(IList<AudioRecordingConfiguration>? configs) =>
                changed(configs?.Any(config => config.IsClientSilenced) ?? false);
        }

        private bool Call(Action<MediaRecorder> action)
        {
            lock (_gate)
            {
                if (_halted || _recorder is null) return false;

                try
                {
                    action(_recorder);
                    return true;
                }
                catch (Java.Lang.Exception)
                {
                    return false;
                }
            }
        }
    }
}
