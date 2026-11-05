using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;

using Backlog.Modules.Sync.Abstractions;
using Backlog.Modules.Sync.Abstractions.DataTransferObjects;

namespace Backlog.Mobile.UI.Services;

/// <summary>How one cloud transcription ended, as the dictation engine acts on it.</summary>
public enum CloudTranscriptionOutcome
{
    /// <summary>The service transcribed the recording.</summary>
    Heard,

    /// <summary>The service cannot transcribe here — no route, no model configured, or no
    /// answer at all. The phone falls back to the device (local ADR 0045 §6).</summary>
    Unavailable,

    /// <summary>The service answered and refused or failed this one recording. A failed
    /// turn, not a reason to stop asking.</summary>
    Failed
}

/// <summary>What <see cref="CloudTranscriptionClient.TranscribeAsync"/> came back with.</summary>
/// <param name="Outcome">How it ended.</param>
/// <param name="Text">The words, when <see cref="CloudTranscriptionOutcome.Heard"/>; empty otherwise.</param>
/// <param name="Status">The service's status, or <c>null</c> when there was no answer.</param>
public sealed record CloudTranscription(CloudTranscriptionOutcome Outcome, string Text, HttpStatusCode? Status)
{
    public static CloudTranscription Heard(string text) => new(CloudTranscriptionOutcome.Heard, text, HttpStatusCode.OK);

    public static CloudTranscription Unavailable(HttpStatusCode? status) => new(CloudTranscriptionOutcome.Unavailable, string.Empty, status);

    public static CloudTranscription Failed(HttpStatusCode status) => new(CloudTranscriptionOutcome.Failed, string.Empty, status);
}

/// <summary>
/// The phone's call to the sync service's <c>POST /api/sync/transcriptions</c>
/// (local ADR 0045 §1): one recording, as its raw bytes under its own Content-Type, with the
/// optional language hint, answered by its text.
/// </summary>
/// <remarks>
/// <para>
/// Its own typed client rather than a method on <see cref="CloudSyncClient"/>, because its
/// HTTP pipeline differs: each head registers it with the sync bearer
/// (<c>AddSyncPipeline()</c>) and without any retry. The browser harness's
/// <c>AddServiceDefaults()</c> standard pipeline would cut an upload off at ten seconds and
/// send it again, three times over; a recording is sent once.
/// </para>
/// <para>
/// The sixty-second bound is this client's own, on <paramref name="clock"/>, so it holds in
/// both heads whatever their <see cref="HttpClient.Timeout"/> is — the MAUI head composes no
/// ServiceDefaults at all. The service answers within its own fifty-second budget.
/// </para>
/// </remarks>
public sealed class CloudTranscriptionClient(HttpClient http, TimeProvider clock)
{
    /// <summary>How long the phone waits for an answer before it treats the cloud as
    /// unavailable.</summary>
    public static readonly TimeSpan Timeout = TimeSpan.FromSeconds(60);

    /// <summary>
    /// Sends <paramref name="recording"/> and returns what the service made of it. Throws
    /// only <see cref="OperationCanceledException"/>, when <paramref name="cancellationToken"/>
    /// ended the call.
    /// </summary>
    /// <param name="recording">A recording with audio in it.</param>
    /// <param name="language">The hint — <c>nl</c>, <c>en</c> — or <c>null</c> to let the model detect it.</param>
    /// <param name="cancellationToken">The turn's own end.</param>
    public async Task<CloudTranscription> TranscribeAsync(AudioRecording recording, string? language, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(recording);

        var route = SyncRoutes.Absolute(SyncRoutes.Transcriptions);
        if (!string.IsNullOrWhiteSpace(language))
        {
            route = $"{route}?{SyncRoutes.TranscriptionLanguageQuery}={Uri.EscapeDataString(language)}";
        }

        using var body = new ByteArrayContent(recording.Content);
        body.Headers.ContentType = MediaTypeHeaderValue.Parse(recording.ContentType);

        using var request = new HttpRequestMessage(HttpMethod.Post, route) { Content = body };

        using var timeout = new CancellationTokenSource(Timeout, clock);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);

        try
        {
            using var response = await http.SendAsync(request, linked.Token);

            if (response.IsSuccessStatusCode)
            {
                var answer = await response.Content.ReadFromJsonAsync<TranscriptionResponse>(linked.Token);
                return CloudTranscription.Heard(answer?.Text ?? string.Empty);
            }

            // An older service with no such route, or one with no model configured: neither
            // will transcribe on a later turn of this run either.
            if (response.StatusCode == HttpStatusCode.NotFound) return CloudTranscription.Unavailable(response.StatusCode);

            if (response.StatusCode == HttpStatusCode.ServiceUnavailable)
            {
                var problem = await SyncProblem.ReadAsync(response, linked.Token);
                if (problem.Code == SyncErrorCodes.TranscriptionUnavailable) return CloudTranscription.Unavailable(response.StatusCode);
            }

            return CloudTranscription.Failed(response.StatusCode);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // HttpClient's own timeout lands here too, as a TaskCanceledException.
            return CloudTranscription.Unavailable(null);
        }
        catch (HttpRequestException)
        {
            return CloudTranscription.Unavailable(null);
        }
        catch (System.Text.Json.JsonException)
        {
            // A success whose body is not the transcription's: something answered, and
            // not with words.
            return CloudTranscription.Failed(HttpStatusCode.OK);
        }
    }
}
