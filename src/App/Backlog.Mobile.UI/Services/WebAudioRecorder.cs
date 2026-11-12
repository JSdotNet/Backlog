using Backlog.Modules.Sync.Abstractions;

using Microsoft.JSInterop;

namespace Backlog.Mobile.UI.Services;

/// <summary>
/// <see cref="IAudioRecorder"/> over the browser's <c>MediaRecorder</c>, for the mobile web
/// harness's cloud dictation engine (local ADR 0045 §6). Records webm, opus inside, which the
/// sync route takes as <c>audio/webm</c>.
/// </summary>
/// <remarks>
/// <para>
/// The JS lives in <c>recorder.js</c>, a module beside <c>speech.js</c>, for the reason
/// <see cref="WebSpeechTranscriber"/> gives. The cap is the module's own timer, so a turn
/// nobody stops still ends.
/// </para>
/// <para>
/// A cancelled turn and a disposed recorder abandon the recording in the module — the
/// microphone goes off and nothing is reported — rather than stopping it, which would
/// finish the recording and hand it back to a turn nobody is waiting on.
/// </para>
/// <para>
/// The recording comes back as an <see cref="IJSStreamReference"/>: the harness is Blazor
/// Server, so the bytes cross to the circuit in chunks rather than as one SignalR message,
/// and the POST to the sync service leaves from the server. It is read into memory, bounded
/// by the route's cap; five minutes of opus is a few megabytes.
/// </para>
/// </remarks>
public sealed class WebAudioRecorder(IJSRuntime js) : IAudioRecorder
{
    private const string ModulePath = "./_content/Backlog.Mobile.UI/recorder.js";

    private const string Unavailable = "Recording is not available here.";

    private const string Cancelled = "Recording was cancelled.";

    private readonly SemaphoreSlim _moduleGate = new(1, 1);

    private IJSObjectReference? _module;
    private DotNetObjectReference<WebAudioRecorder>? _self;
    private TaskCompletionSource<AudioRecording>? _pending;

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
        catch (Exception ex) when (IsBrowserGone(ex))
        {
            return false;
        }
    }

    /// <inheritdoc />
    public async Task<AudioRecording> RecordAsync(TimeSpan maxDuration, CancellationToken cancellationToken = default)
    {
        var module = await LoadModuleAsync(cancellationToken);
        if (module is null) return AudioRecording.Failed(Unavailable);

        // Before the call into JS, as in WebSpeechTranscriber: a refused microphone can
        // report back while `start` is still awaiting.
        var pending = new TaskCompletionSource<AudioRecording>(TaskCreationOptions.RunContinuationsAsynchronously);
        Interlocked.Exchange(ref _pending, pending)?.TrySetResult(AudioRecording.Failed("Recording was restarted."));

        _self ??= DotNetObjectReference.Create(this);

        // Before the start, which awaits the permission prompt: a turn cancelled while it is
        // up has its microphone turned off as soon as it opens.
        await using var registration = cancellationToken.Register(() =>
        {
            if (!pending.TrySetResult(AudioRecording.Failed(Cancelled))) return;

            Interlocked.CompareExchange(ref _pending, null, pending);
            _ = AbortAsync();
        });

        // Cancelled before it began: the abort above would reach the module ahead of a start.
        if (pending.Task.IsCompleted) return await pending.Task;

        try
        {
            await module.InvokeVoidAsync("start", _self, (long)maxDuration.TotalMilliseconds);
        }
        catch (JSException ex)
        {
            Settle(AudioRecording.Failed(ex.Message));
        }
        catch (Exception ex) when (IsBrowserGone(ex))
        {
            Settle(AudioRecording.Failed(Unavailable));
        }

        return await pending.Task;
    }

    /// <inheritdoc />
    public async ValueTask StopAsync()
    {
        if (_module is null) return;

        try
        {
            await _module.InvokeVoidAsync("stop");
        }
        catch (JSException)
        {
            // Already stopped; the recording is on its way.
        }
        catch (Exception ex) when (IsBrowserGone(ex))
        {
        }
    }

    /// <summary>Called from <c>recorder.js</c> with the finished recording.</summary>
    [JSInvokable]
    public async Task OnRecordedAsync(IJSStreamReference audio, string contentType)
    {
        ArgumentNullException.ThrowIfNull(audio);

        await using (audio)
        {
            try
            {
                // The route's cap plus one, so a recording past it fails here rather than
                // being cut short and sent.
                await using var stream = await audio.OpenReadStreamAsync(SyncTranscriptionLimits.DefaultMaxBytes + 1);
                using var buffer = new MemoryStream();
                await stream.CopyToAsync(buffer);

                Settle(buffer.Length > SyncTranscriptionLimits.DefaultMaxBytes
                    ? AudioRecording.Failed(CloudSpeechTranscriber.RecordingTooLarge)
                    : AudioRecording.Recorded(buffer.ToArray(), string.IsNullOrWhiteSpace(contentType) ? "audio/webm" : contentType));
            }
            catch (ArgumentOutOfRangeException)
            {
                // OpenReadStreamAsync's refusal of a stream longer than the cap.
                Settle(AudioRecording.Failed(CloudSpeechTranscriber.RecordingTooLarge));
            }
            catch (Exception ex) when (ex is JSException || IsBrowserGone(ex))
            {
                Settle(AudioRecording.Failed(Unavailable));
            }
        }
    }

    /// <summary>Called from <c>recorder.js</c> when the turn was stopped before the microphone
    /// opened: a recording with nothing in it, which reads as nothing heard.</summary>
    [JSInvokable]
    public Task OnStoppedBeforeRecordingAsync(string contentType)
    {
        Settle(AudioRecording.Recorded([], string.IsNullOrWhiteSpace(contentType) ? "audio/webm" : contentType));
        return Task.CompletedTask;
    }

    /// <summary>Called from <c>recorder.js</c> with the <c>getUserMedia</c> error's name.</summary>
    [JSInvokable]
    public Task OnFailedAsync(string error)
    {
        Settle(AudioRecording.Failed(Describe(error)));
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        Settle(AudioRecording.Failed("Recording stopped."));

        if (_module is not null)
        {
            try
            {
                // Abandoned, not stopped: a stop would finish the recording and call back
                // into a reference disposed below.
                await _module.InvokeVoidAsync("abort");
                await _module.DisposeAsync();
            }
            catch (JSException)
            {
            }
            catch (Exception ex) when (IsBrowserGone(ex))
            {
                // The circuit is already gone, as in WebSpeechTranscriber's teardown.
            }

            _module = null;
        }

        _self?.Dispose();
        _self = null;
        _moduleGate.Dispose();
    }

    /// <summary>Turns the module's microphone off without a report, for a turn that ended
    /// on this side.</summary>
    private async Task AbortAsync()
    {
        if (_module is not { } module) return;

        try
        {
            await module.InvokeVoidAsync("abort");
        }
        catch (JSException)
        {
            // Nothing to abandon.
        }
        catch (Exception ex) when (IsBrowserGone(ex))
        {
        }
    }

    private void Settle(AudioRecording result) =>
        Interlocked.Exchange(ref _pending, null)?.TrySetResult(result);

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
        catch (Exception ex) when (IsBrowserGone(ex))
        {
            return null;
        }
        finally
        {
            _moduleGate.Release();
        }
    }

    /// <summary>No browser to talk to right now; see <see cref="WebSpeechTranscriber"/>.</summary>
    private static bool IsBrowserGone(Exception ex) =>
        ex is JSDisconnectedException or InvalidOperationException or OperationCanceledException or ObjectDisposedException;

    /// <summary>The <c>getUserMedia</c> error names, in words a person can act on.</summary>
    private static string Describe(string? error) => error switch
    {
        "NotAllowedError" or "SecurityError" => MicrophoneAccess.DeniedInBrowser,
        "NotFoundError" or "OverconstrainedError" => "No microphone was found.",
        "NotReadableError" or "AbortError" or "audio-capture" => "The microphone could not be read.",
        "not-supported" => Unavailable,
        null or "" => "Recording failed.",
        _ => $"Recording failed ({error})."
    };
}
