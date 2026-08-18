using Microsoft.JSInterop;

namespace Backlog.Mobile.UI.Services;

/// <summary>
/// <see cref="ISpeechTranscriber"/> over the browser's Web Speech API, for the
/// mobile web harness.
/// </summary>
/// <remarks>
/// <para>
/// This is the implementation Playwright can drive: the harness runs the same
/// Inbox screen in a real browser, so the feature can be reviewed without an
/// Android device in the room. It is deliberately not registered in the MAUI
/// head — the Android System WebView generally has no
/// <c>webkitSpeechRecognition</c>, and a shared registration would look like it
/// worked right up until someone ran it on a phone.
/// </para>
/// <para>
/// The JS lives in a module rather than a host <c>&lt;script&gt;</c> tag, so it
/// arrives through the same static-asset route as <c>app.css</c> and neither
/// host document has to be edited.
/// </para>
/// </remarks>
public sealed class WebSpeechTranscriber(IJSRuntime js) : ISpeechTranscriber
{
    private const string ModulePath = "./_content/Backlog.Mobile.UI/speech.js";

    private const string Unavailable = "Speech recognition is not available here.";

    private readonly SemaphoreSlim _moduleGate = new(1, 1);

    private IJSObjectReference? _module;
    private DotNetObjectReference<WebSpeechTranscriber>? _self;
    private TaskCompletionSource<SpeechTranscript>? _pending;

    /// <inheritdoc />
    public async ValueTask<bool> IsSupportedAsync(CancellationToken cancellationToken = default)
    {
        var module = await LoadModuleAsync(cancellationToken);
        if (module is null) return false;

        try
        {
            return await module.InvokeAsync<bool>("isSupported", cancellationToken);
        }
        catch (Exception ex) when (IsBrowserGone(ex))
        {
            return false;
        }
    }

    /// <inheritdoc />
    public async Task<SpeechTranscript> ListenAsync(CancellationToken cancellationToken = default)
    {
        var module = await LoadModuleAsync(cancellationToken);
        if (module is null) return SpeechTranscript.Failed(Unavailable);

        // Created before the call into JS: the recogniser can fail fast enough
        // that OnFailed arrives while `start` is still awaiting.
        var pending = new TaskCompletionSource<SpeechTranscript>(TaskCreationOptions.RunContinuationsAsynchronously);
        Interlocked.Exchange(ref _pending, pending)?.TrySetResult(SpeechTranscript.Failed("Listening was restarted."));

        _self ??= DotNetObjectReference.Create(this);

        try
        {
            await module.InvokeVoidAsync("start", cancellationToken, _self);
        }
        catch (JSException ex)
        {
            Settle(SpeechTranscript.Failed(ex.Message));
        }
        catch (Exception ex) when (IsBrowserGone(ex))
        {
            Settle(SpeechTranscript.Failed(Unavailable));
        }

        await using var registration = cancellationToken.Register(
            () => pending.TrySetResult(SpeechTranscript.Failed("Listening was cancelled.")));

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
            // The recogniser was already finished; the result is on its way.
        }
        catch (Exception ex) when (IsBrowserGone(ex))
        {
        }
    }

    /// <summary>Called from <c>speech.js</c> when the recogniser produced words.</summary>
    [JSInvokable]
    public Task OnRecognizedAsync(string text)
    {
        Settle(SpeechTranscript.Heard(text ?? string.Empty));
        return Task.CompletedTask;
    }

    /// <summary>Called from <c>speech.js</c> with the Web Speech error code.</summary>
    [JSInvokable]
    public Task OnFailedAsync(string error)
    {
        Settle(SpeechTranscript.Failed(Describe(error)));
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        Settle(SpeechTranscript.Failed("Listening stopped."));

        if (_module is not null)
        {
            try
            {
                await _module.InvokeVoidAsync("stop");
                await _module.DisposeAsync();
            }
            catch (JSException)
            {
            }
            catch (Exception ex) when (IsBrowserGone(ex))
            {
                // The circuit is already gone; there is nothing left to tidy on
                // the other side. Same reasoning as CopyButton's teardown.
            }

            _module = null;
        }

        _self?.Dispose();
        _self = null;
        _moduleGate.Dispose();
    }

    private void Settle(SpeechTranscript result) =>
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

    /// <summary>
    /// The three ways "there is no browser to talk to right now" arrives:
    /// prerendering (nothing is attached yet), a dropped circuit, and a call
    /// cancelled while the circuit was tearing down. None of them is a fault of
    /// this feature, and none of them should reach the screen as an exception.
    /// </summary>
    private static bool IsBrowserGone(Exception ex) =>
        ex is JSDisconnectedException or InvalidOperationException or OperationCanceledException or ObjectDisposedException;

    /// <summary>
    /// The Web Speech error codes, in words a person can act on. The raw code is
    /// kept for anything unrecognised so an unexpected browser still says
    /// something specific.
    /// </summary>
    private static string Describe(string? error) => error switch
    {
        "not-allowed" or "service-not-allowed" =>
            "Microphone access was denied. Allow it for this site and try again.",
        "no-speech" => "Nothing was heard. Try again and speak once the button is on.",
        "audio-capture" => "No microphone was found.",
        "network" => "Speech recognition needs the network and could not reach it.",
        "aborted" => "Listening stopped before anything was recognised.",
        "language-not-supported" => "Speech recognition does not support this device's language.",
        "not-supported" => Unavailable,
        null or "" => "Speech recognition failed.",
        _ => $"Speech recognition failed ({error})."
    };
}
