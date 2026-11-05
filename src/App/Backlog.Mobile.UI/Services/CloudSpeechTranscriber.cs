using System.Net;

using Backlog.Infrastructure.Sync;

namespace Backlog.Mobile.UI.Services;

/// <summary>
/// <see cref="ISpeechTranscriber"/> through the sync service (local ADR 0045 §6): the phone
/// records the turn, then posts the recording and shows the text that comes back.
/// </summary>
/// <remarks>
/// <para>
/// Record, then send — not streaming. The turn ends when the person stops or at
/// <see cref="MaxRecording"/>, and only then does anything leave the phone, which is why the
/// screen says "Recording…" and then "Transcribing…" (<see cref="SpeechTurnStage"/>).
/// </para>
/// <para>
/// The Dictation language reaches the service as its hint, read at the start of every turn
/// like the on-device recognisers read it (<see cref="DictationRequest.TranscriptionHintFor"/>).
/// </para>
/// <para>
/// A service that cannot transcribe here — no route, no model, no answer — ends the turn
/// with <see cref="UnavailableNotice"/> and marks <see cref="CloudDictationAvailability"/>,
/// so <see cref="SelectingSpeechTranscriber"/> hands every later turn to the device. A
/// refusal of this one recording is an ordinary failed turn.
/// </para>
/// </remarks>
public sealed class CloudSpeechTranscriber(
    IAudioRecorder recorder,
    CloudTranscriptionClient client,
    ISpeechSettingsStore settings,
    CloudDictationAvailability availability) : ISpeechTranscriber
{
    /// <summary>The longest one cloud turn records (local ADR 0045 §6).</summary>
    public static readonly TimeSpan MaxRecording = TimeSpan.FromMinutes(5);

    /// <summary>What the turn says when the cloud cannot transcribe here.</summary>
    public const string UnavailableNotice = "Cloud dictation is unavailable here; using on-device.";

    /// <summary>What the turn says when the model failed this recording (502), or the service
    /// failed it some way no other sentence names.</summary>
    public const string TranscriptionFailed = "Transcription failed, try again.";

    /// <summary>What the turn says when the recording was over the service's cap (413).</summary>
    public const string RecordingTooLarge = "The recording is too long to transcribe. Try a shorter one.";

    /// <summary>What the turn says when the service does not take this recording's type (415).</summary>
    public const string RecordingTypeRefused = "This phone records in a format the cloud does not take. Use on-device dictation in Settings.";

    /// <summary>What the turn says when the service refused this phone's credential (401):
    /// another try sends the same one.</summary>
    public const string PairingRefused = "The sync service no longer accepts this phone. Forget this device and pair it again to dictate in the cloud.";

    /// <inheritdoc />
    public ValueTask<bool> IsSupportedAsync(CancellationToken cancellationToken = default) =>
        recorder.IsSupportedAsync(cancellationToken);

    /// <inheritdoc />
    public Task<SpeechTranscript> ListenAsync(CancellationToken cancellationToken = default) =>
        ListenAsync(onStage: null, cancellationToken);

    /// <inheritdoc />
    public async Task<SpeechTranscript> ListenAsync(Action<SpeechTurnStage>? onStage, CancellationToken cancellationToken = default)
    {
        var hint = DictationRequest.TranscriptionHintFor(settings.Language);

        onStage?.Invoke(SpeechTurnStage.Recording);

        AudioRecording recording;
        try
        {
            recording = await recorder.RecordAsync(MaxRecording, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            return SpeechTranscript.Failed("Listening was cancelled.");
        }

        if (recording.IsError) return SpeechTranscript.Failed(recording.Error!);

        // Nothing recorded is nothing heard, and not worth a round trip.
        if (recording.Content.Length == 0) return SpeechTranscript.Heard(string.Empty);

        onStage?.Invoke(SpeechTurnStage.Transcribing);

        CloudTranscription result;
        try
        {
            result = await client.TranscribeAsync(recording, hint, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            return SpeechTranscript.Failed("Listening was cancelled.");
        }

        switch (result.Outcome)
        {
            case CloudTranscriptionOutcome.Heard:
                return SpeechTranscript.Heard(result.Text);

            case CloudTranscriptionOutcome.Unavailable:
                availability.MarkUnavailable();
                return SpeechTranscript.Failed(UnavailableNotice);

            default:
                return SpeechTranscript.Failed(Describe(result.Status));
        }
    }

    /// <inheritdoc />
    public ValueTask StopAsync() => recorder.StopAsync();

    /// <summary>The recorder is the host's, disposed with the scope that made it.</summary>
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    private static string Describe(HttpStatusCode? status) => status switch
    {
        HttpStatusCode.RequestEntityTooLarge => RecordingTooLarge,
        HttpStatusCode.UnsupportedMediaType => RecordingTypeRefused,
        HttpStatusCode.Unauthorized => PairingRefused,

        // The sync pipeline's own answer for a service newer than this app; nothing was sent.
        HttpStatusCode.UpgradeRequired => SyncCompatibility.UpdateRequiredMessage,
        _ => TranscriptionFailed
    };
}
