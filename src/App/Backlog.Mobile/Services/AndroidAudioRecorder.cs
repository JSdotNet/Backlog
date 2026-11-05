using Android.Media;

using Backlog.Mobile.UI.Services;
using Backlog.Modules.Sync.Abstractions;

namespace Backlog.Mobile.Services;

/// <summary>
/// <see cref="IAudioRecorder"/> over Android's own <see cref="MediaRecorder"/>, for the cloud
/// dictation engine (local ADR 0045 §6): AAC in MPEG-4, sent as <c>audio/mp4</c>.
/// </summary>
/// <remarks>
/// <para>
/// The platform API directly, for the reason <see cref="AndroidSpeechTranscriber"/> gives.
/// </para>
/// <para>
/// <see cref="MediaRecorder"/> writes to a file, not to memory, so a turn records into the
/// app's cache directory — private to the app — and the file is read back and deleted as the
/// turn ends, whether it recorded or failed. Nothing of a recording outlives its turn on the
/// phone either: a file left by a process killed mid-turn is swept at the start of the next.
/// </para>
/// <para>
/// The turn exists from the press, before the permission request: a stop pressed while the
/// request is up ends it with nothing recorded, and the microphone never starts. A stop so
/// soon after the start that no audio was captured is the same empty recording — "nothing
/// was heard", as the browser says it — not a microphone that failed.
/// </para>
/// <para>
/// The cap is a timer of this class's own that stops the recorder the way the stop button
/// does, rather than <c>setMaxDuration</c>, after which a later <c>stop()</c> throws.
/// </para>
/// </remarks>
public sealed class AndroidAudioRecorder : IAudioRecorder
{
    private const string ContentType = "audio/mp4";

    private const string PermissionDenied =
        "Microphone access was denied. Allow it in Android settings and try again.";

    private const string CouldNotRecord = "The microphone could not be read.";

    private const string FilePrefix = "dictation-";
    private const string FileExtension = ".m4a";

    // Speech, not music: mono at 16 kHz is what the transcription model listens at, and
    // 64 kbps AAC keeps five minutes near 2.4 MB.
    private const int SamplingRate = 16_000;
    private const int BitRate = 64_000;

    private readonly Lock _gate = new();

    private Turn? _turn;

    /// <inheritdoc />
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

    /// <inheritdoc />
    public async Task<AudioRecording> RecordAsync(TimeSpan maxDuration, CancellationToken cancellationToken = default)
    {
        var path = Path.Combine(FileSystem.CacheDirectory, $"{FilePrefix}{Guid.NewGuid():N}{FileExtension}");
        var turn = new Turn(path);

        Turn? previous;
        lock (_gate)
        {
            previous = _turn;
            _turn = turn;
        }

        previous?.Finish(AudioRecording.Failed("Recording was restarted."));
        SweepLeftovers(path);

        await using var cancelled = cancellationToken.Register(
            () => turn.Finish(AudioRecording.Failed("Recording was cancelled.")));

        var permission = await MainThread.InvokeOnMainThreadAsync(Permissions.RequestAsync<Permissions.Microphone>);
        if (permission != PermissionStatus.Granted) turn.Finish(AudioRecording.Failed(PermissionDenied));

        try
        {
            turn.Start();
        }
        catch (Java.Lang.Exception)
        {
            turn.Finish(AudioRecording.Failed(CouldNotRecord));
        }

        using var cap = new CancellationTokenSource(maxDuration);
        await using var capped = cap.Token.Register(turn.Stop);

        var result = await turn.Result;

        lock (_gate)
        {
            if (ReferenceEquals(_turn, turn)) _turn = null;
        }

        return result;
    }

    /// <inheritdoc />
    public ValueTask StopAsync()
    {
        Turn? turn;
        lock (_gate) turn = _turn;

        turn?.Stop();
        return ValueTask.CompletedTask;
    }

    /// <inheritdoc />
    public ValueTask DisposeAsync()
    {
        Turn? turn;
        lock (_gate)
        {
            turn = _turn;
            _turn = null;
        }

        turn?.Finish(AudioRecording.Failed("Recording stopped."));
        return ValueTask.CompletedTask;
    }

    /// <summary>Deletes every recording file but <paramref name="current"/>'s: what a turn
    /// that never reached its end — the process killed mid-turn — left behind.</summary>
    private static void SweepLeftovers(string current)
    {
        try
        {
            foreach (var file in Directory.EnumerateFiles(FileSystem.CacheDirectory, $"{FilePrefix}*{FileExtension}"))
            {
                if (string.Equals(file, current, StringComparison.Ordinal)) continue;

                try
                {
                    File.Delete(file);
                }
                catch (IOException)
                {
                    // In use or already gone; the next turn tries again.
                }
                catch (UnauthorizedAccessException)
                {
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // No cache directory to sweep is nothing left over.
        }
    }

    /// <summary>One recording: its recorder, its file, and how it ended. First end wins.</summary>
    private sealed class Turn(string path)
    {
        private readonly TaskCompletionSource<AudioRecording> _result =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        private readonly Lock _gate = new();

        private MediaRecorder? _recorder;
        private bool _ended;

        public Task<AudioRecording> Result => _result.Task;

        /// <summary>Opens the microphone, unless the turn already ended — stopped or
        /// cancelled while the permission request was up.</summary>
        public void Start()
        {
            lock (_gate)
            {
                if (_ended) return;
            }

            var recorder = OperatingSystem.IsAndroidVersionAtLeast(31)
                ? new MediaRecorder(Microsoft.Maui.ApplicationModel.Platform.AppContext)
#pragma warning disable CA1422 // The constructor API 31 replaced; the head runs from 24.
                : new MediaRecorder();
#pragma warning restore CA1422

            recorder.SetAudioSource(AudioSource.Mic);
            recorder.SetOutputFormat(OutputFormat.Mpeg4);
            recorder.SetAudioEncoder(AudioEncoder.Aac);
            recorder.SetAudioChannels(1);
            recorder.SetAudioSamplingRate(SamplingRate);
            recorder.SetAudioEncodingBitRate(BitRate);
            recorder.SetOutputFile(path);

            lock (_gate)
            {
                if (_ended)
                {
                    recorder.Release();
                    recorder.Dispose();
                    return;
                }

                _recorder = recorder;
            }

            recorder.Prepare();
            recorder.Start();
        }

        /// <summary>The stop button, or the cap: ends the recording and hands it back. A stop
        /// before the recorder started, or before it captured anything — when its own stop
        /// throws — is an empty recording.</summary>
        public void Stop()
        {
            if (!Release(out var stopped)) return;

            Settle(stopped ? Read() : AudioRecording.Recorded([], ContentType));
        }

        /// <summary>Ends the turn with <paramref name="result"/>, keeping nothing recorded.</summary>
        public void Finish(AudioRecording result)
        {
            Release(out _);
            Settle(result);
        }

        private void Settle(AudioRecording result)
        {
            _result.TrySetResult(result);
            Delete();
        }

        /// <summary>Stops and releases the recorder once. <paramref name="stopped"/> says
        /// whether it stopped cleanly — a stop straight after start, with no audio yet,
        /// throws — and is <c>false</c> when there was no recorder yet.</summary>
        private bool Release(out bool stopped)
        {
            MediaRecorder? recorder;
            bool first;
            lock (_gate)
            {
                recorder = _recorder;
                _recorder = null;
                first = !_ended;
                _ended = true;
            }

            stopped = false;
            if (recorder is null) return first;

            try
            {
                recorder.Stop();
                stopped = true;
            }
            catch (Java.Lang.Exception)
            {
            }

            try
            {
                recorder.Release();
            }
            catch (Java.Lang.Exception)
            {
            }

            recorder.Dispose();
            return true;
        }

        private AudioRecording Read()
        {
            try
            {
                var info = new FileInfo(path);
                if (!info.Exists) return AudioRecording.Recorded([], ContentType);
                if (info.Length > SyncTranscriptionLimits.DefaultMaxBytes) return AudioRecording.Failed(CloudSpeechTranscriber.RecordingTooLarge);

                return AudioRecording.Recorded(File.ReadAllBytes(path), ContentType);
            }
            catch (IOException)
            {
                return AudioRecording.Failed(CouldNotRecord);
            }
        }

        private void Delete()
        {
            try
            {
                if (File.Exists(path)) File.Delete(path);
            }
            catch (IOException)
            {
                // The cache directory is the OS's to clear; a leftover is not worth a failed turn.
            }
        }
    }
}
