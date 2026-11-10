using Android.Content;
using Android.OS;
using Android.Runtime;
using Android.Speech;

using Backlog.Mobile.UI.Services;

namespace Backlog.Mobile.Services;

/// <summary>
/// <see cref="ISpeechTranscriber"/> over Android's own recogniser.
/// </summary>
/// <remarks>
/// <para>
/// The platform API is used directly rather than through a wrapper package: the
/// head targets <c>net10.0-android</c> only, so there is no second platform to
/// abstract over, and a new central package version would be carrying a
/// dependency for a single call site.
/// </para>
/// <para>
/// <see cref="SpeechRecognizer"/> is main-thread only — it binds to a service
/// and delivers its callbacks on the looper it was created on — so every touch
/// of it goes through <see cref="MainThread"/>.
/// </para>
/// <para>
/// The language is the Dictation setting's (<see cref="ISpeechSettingsStore"/>), read at
/// the start of every turn and turned into the intent's extras by
/// <see cref="DictationRequest"/>: <c>EXTRA_LANGUAGE</c> always, and on Android 14 and
/// later the language detection and language switch extras for Dutch and English. The
/// phone's language, <c>Locale.Default</c>, is used only when the setting asks for it.
/// </para>
/// <para>
/// Every extra past <c>EXTRA_LANGUAGE</c> is documented as possibly having no effect,
/// depending on the recogniser; whether they help is judged on a device.
/// </para>
/// </remarks>
public sealed class AndroidSpeechTranscriber(ISpeechSettingsStore settings) : ISpeechTranscriber
{
    private const string NoRecognizer =
        "This device has no speech recognition service installed.";

    private const string PermissionDenied = AndroidMicrophoneAccess.Denied;

    private const string NothingHeard =
        "Nothing was heard. Try again and speak once the button is on.";

    private const string NoModelForBoth =
        "Dutch and English need their speech models on this phone. Download both in Google's speech recognition settings, or choose one language in Settings.";

    private const string NoModelForOne =
        "This phone has no speech model for the dictation language. Download it in Google's speech recognition settings, or choose another language in Settings.";

    // How long the recogniser waits on silence before it ends a turn. The defaults end a
    // turn at a breath; these leave room to think between sentences. Android documents
    // both as possibly having no effect.
    private const long CompleteSilenceMillis = 2500;
    private const long PossiblyCompleteSilenceMillis = 2000;

    private SpeechRecognizer? _recognizer;
    private RecognitionCallback? _callback;
    private TaskCompletionSource<SpeechTranscript>? _pending;

    /// <inheritdoc />
    public ValueTask<bool> IsSupportedAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            // Needs the <queries> entry in AndroidManifest.xml to see the
            // recognition service at all on Android 11 and later.
            return ValueTask.FromResult(SpeechRecognizer.IsRecognitionAvailable(Context));
        }
        catch (Java.Lang.Exception)
        {
            return ValueTask.FromResult(false);
        }
    }

    /// <inheritdoc />
    public async Task<SpeechTranscript> ListenAsync(CancellationToken cancellationToken = default)
    {
        // A refused microphone is an ordinary answer to "listen", not a fault:
        // the screen says so and the button goes back to idle.
        var permission = await MainThread.InvokeOnMainThreadAsync(
            Permissions.RequestAsync<Permissions.Microphone>);

        if (permission != PermissionStatus.Granted) return SpeechTranscript.Failed(PermissionDenied);

        if (!await IsSupportedAsync(cancellationToken)) return SpeechTranscript.Failed(NoRecognizer);

        var pending = new TaskCompletionSource<SpeechTranscript>(TaskCreationOptions.RunContinuationsAsynchronously);
        Interlocked.Exchange(ref _pending, pending)?.TrySetResult(SpeechTranscript.Failed("Listening was restarted."));

        var request = DictationRequest.For(
            settings.Language,
            Java.Util.Locale.Default?.ToLanguageTag(),
            supportsDetection: OperatingSystem.IsAndroidVersionAtLeast(34));

        await MainThread.InvokeOnMainThreadAsync(() => StartListening(request));

        await using var registration = cancellationToken.Register(
            () => pending.TrySetResult(SpeechTranscript.Failed("Listening was cancelled.")));

        return await pending.Task;
    }

    /// <inheritdoc />
    public ValueTask StopAsync() => new(MainThread.InvokeOnMainThreadAsync(() =>
    {
        try
        {
            // Ask for the words so far. The result arrives through OnResults.
            _recognizer?.StopListening();
        }
        catch (Java.Lang.Exception)
        {
            // Not listening, or the service already went away.
        }
    }));

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        Settle(SpeechTranscript.Failed("Listening stopped."));

        try
        {
            await MainThread.InvokeOnMainThreadAsync(ReleaseRecognizer);
        }
        catch (Java.Lang.Exception)
        {
        }
    }

    private static Context Context => Microsoft.Maui.ApplicationModel.Platform.AppContext;

    private void StartListening(DictationRequest request)
    {
        ReleaseRecognizer();

        try
        {
            _recognizer = SpeechRecognizer.CreateSpeechRecognizer(Context);

            if (_recognizer is null)
            {
                Settle(SpeechTranscript.Failed(NoRecognizer));
                return;
            }

            _callback = new RecognitionCallback(Settle, request.DetectsLanguage);
            _recognizer.SetRecognitionListener(_callback);

            using var intent = new Intent(RecognizerIntent.ActionRecognizeSpeech);
            intent.PutExtra(RecognizerIntent.ExtraLanguageModel, RecognizerIntent.LanguageModelFreeForm);
            intent.PutExtra(RecognizerIntent.ExtraLanguage, request.Language ?? DictationRequest.English);
            intent.PutExtra(RecognizerIntent.ExtraMaxResults, 1);
            intent.PutExtra(RecognizerIntent.ExtraPartialResults, false);
            intent.PutExtra(RecognizerIntent.ExtraSpeechInputCompleteSilenceLengthMillis, CompleteSilenceMillis);
            intent.PutExtra(RecognizerIntent.ExtraSpeechInputPossiblyCompleteSilenceLengthMillis, PossiblyCompleteSilenceMillis);

            // The guard is the analyser's as much as the device's: these extras exist from
            // API 34, and the head runs from 24.
            if (OperatingSystem.IsAndroidVersionAtLeast(34) && request.DetectsLanguage)
            {
                intent.PutExtra(RecognizerIntent.ExtraEnableLanguageDetection, true);
                intent.PutStringArrayListExtra(
                    RecognizerIntent.ExtraLanguageDetectionAllowedLanguages,
                    [.. request.DetectionLanguages]);

                if (request.SwitchesLanguage)
                {
                    intent.PutExtra(RecognizerIntent.ExtraEnableLanguageSwitch, RecognizerIntent.LanguageSwitchBalanced);

                    // The detection list does not bound a switch; without this a recogniser
                    // could switch mid-turn to a language outside Dutch and English.
                    intent.PutStringArrayListExtra(
                        RecognizerIntent.ExtraLanguageSwitchAllowedLanguages,
                        [.. request.DetectionLanguages]);
                }
            }

            _recognizer.StartListening(intent);
        }
        catch (Java.Lang.Exception ex)
        {
            Settle(SpeechTranscript.Failed(ex.Message ?? NoRecognizer));
        }
    }

    private void ReleaseRecognizer()
    {
        try
        {
            _recognizer?.Cancel();
            _recognizer?.Destroy();
        }
        catch (Java.Lang.Exception)
        {
            // Destroying a recogniser that never bound throws; nothing to undo.
        }

        _recognizer?.Dispose();
        _recognizer = null;

        _callback?.Dispose();
        _callback = null;
    }

    private void Settle(SpeechTranscript result) =>
        Interlocked.Exchange(ref _pending, null)?.TrySetResult(result);

    /// <summary>
    /// The recogniser's callbacks. Only two of them carry an outcome; the rest
    /// are progress the screen does not show in this increment.
    /// </summary>
    /// <param name="settle">Ends the turn with its outcome.</param>
    /// <param name="detectsLanguage">Whether the turn asked for Dutch and English, which
    /// changes what a missing language model is put down to.</param>
    private sealed class RecognitionCallback(Action<SpeechTranscript> settle, bool detectsLanguage)
        : Java.Lang.Object, IRecognitionListener
    {
        public void OnResults(Bundle? results)
        {
            var matches = results?.GetStringArrayList(SpeechRecognizer.ResultsRecognition);
            var text = matches is { Count: > 0 } ? matches[0] : null;

            settle(SpeechTranscript.Heard(text ?? string.Empty));
        }

        public void OnError([GeneratedEnum] SpeechRecognizerError error) =>
            settle(SpeechTranscript.Failed(Describe(error, detectsLanguage)));

        public void OnBeginningOfSpeech()
        {
        }

        public void OnBufferReceived(byte[]? buffer)
        {
        }

        public void OnEndOfSpeech()
        {
        }

        public void OnEvent(int eventType, Bundle? @params)
        {
        }

        public void OnPartialResults(Bundle? partialResults)
        {
        }

        public void OnReadyForSpeech(Bundle? @params)
        {
        }

        /// <summary>Input loudness, delivered many times a second. A level meter
        /// would live here; this increment shows a plain "Listening…" instead.</summary>
        public void OnRmsChanged(float rmsdB)
        {
        }

        /// <summary>
        /// The error codes, in words a person can act on. The two language codes
        /// (API 31) are matched by number, so the switch reads the same on every API
        /// level the head runs on; a recogniser older than 31 never reports them.
        /// Other newer codes fall through to the default.
        /// </summary>
        private static string Describe(SpeechRecognizerError error, bool detectsLanguage) => (int)error switch
        {
            LanguageNotSupported or LanguageUnavailable => detectsLanguage ? NoModelForBoth : NoModelForOne,
            _ => DescribeCommon(error)
        };

        // SpeechRecognizer.ERROR_LANGUAGE_NOT_SUPPORTED and ERROR_LANGUAGE_UNAVAILABLE.
        private const int LanguageNotSupported = 12;
        private const int LanguageUnavailable = 13;

        private static string DescribeCommon(SpeechRecognizerError error) => error switch
        {
            SpeechRecognizerError.InsufficientPermissions => PermissionDenied,
            SpeechRecognizerError.NoMatch => NothingHeard,
            SpeechRecognizerError.SpeechTimeout => NothingHeard,
            SpeechRecognizerError.Audio => "The microphone could not be read.",
            SpeechRecognizerError.Network => "Speech recognition needs the network and could not reach it.",
            SpeechRecognizerError.NetworkTimeout => "Speech recognition timed out waiting for the network.",
            SpeechRecognizerError.Server => "The speech recognition service reported an error.",
            SpeechRecognizerError.RecognizerBusy => "Speech recognition is already running. Try again in a moment.",
            SpeechRecognizerError.Client => "Speech recognition stopped before anything was recognised.",
            _ => $"Speech recognition failed ({error})."
        };
    }
}
