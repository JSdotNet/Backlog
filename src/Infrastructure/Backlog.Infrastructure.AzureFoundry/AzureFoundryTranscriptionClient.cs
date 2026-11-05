using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;
using Azure.Core;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Polly.Timeout;

namespace Backlog.Infrastructure.AzureFoundry;

/// <summary>How the transcription client authenticates to Foundry.</summary>
public enum AzureFoundryTranscriptionCredential
{
    /// <summary>An Entra token from the host's managed identity — the deployed sync service's
    /// user-assigned <c>id-sync-…</c>, which holds Cognitive Services OpenAI User on the
    /// account (local ADR 0045 §5).</summary>
    ManagedIdentity,

    /// <summary>No credential at all — the local stand-in in
    /// <c>Backlog.AzureFoundry.TestService</c>, which takes none.</summary>
    None,
}

/// <summary>
/// Where Foundry's transcription deployment is and how it is reached. Its own options,
/// apart from the desktop's <see cref="AzureFoundrySettings"/>: the sync service is
/// configured from its settings and holds no key, and the DPAPI store those settings live in
/// cannot run in its Linux container (local ADR 0045 §3, ADR 0030).
/// </summary>
public sealed class AzureFoundryTranscriptionOptions
{
    /// <summary>The deployment ADR 0045 names; a setting, so a newer model is a deployment and
    /// a setting rather than a change of code.</summary>
    public const string DefaultDeployment = "gpt-4o-transcribe";

    /// <summary>The api-version of Microsoft's deployment-style example.</summary>
    public const string DefaultApiVersion = "2025-03-01-preview";

    /// <summary>The Foundry account endpoint, such as
    /// <c>https://backlog-foundry.cognitiveservices.azure.com/</c>.</summary>
    public Uri? Endpoint { get; set; }

    public string Deployment { get; set; } = DefaultDeployment;

    public string ApiVersion { get; set; } = DefaultApiVersion;

    public AzureFoundryTranscriptionCredential Credential { get; set; } = AzureFoundryTranscriptionCredential.ManagedIdentity;

    /// <summary>The client id of the user-assigned identity to present, or null for the
    /// system-assigned one. The deployed container sets <c>AZURE_CLIENT_ID</c>.</summary>
    public string? ManagedIdentityClientId { get; set; }

    /// <summary>The whole call, retry included (local ADR 0045 §3): 50 seconds, so the service
    /// answers before the phone's 60-second upload timeout.</summary>
    public TimeSpan TotalBudget { get; set; } = TimeSpan.FromSeconds(50);

    /// <summary>The base of the one retry's exponential, jittered backoff.</summary>
    public TimeSpan RetryDelay { get; set; } = TimeSpan.FromSeconds(2);
}

/// <summary>The credential the client asks for tokens with, or null for none. A holder
/// rather than a bare <see cref="TokenCredential"/>, because "no credential" is a
/// configuration of its own and a container cannot resolve a null.</summary>
public sealed class AzureFoundryTranscriptionTokens(TokenCredential? credential)
{
    /// <summary>The scope an Azure OpenAI data-plane token is issued for.</summary>
    public const string Scope = "https://cognitiveservices.azure.com/.default";

    public TokenCredential? Credential { get; } = credential;
}

/// <summary>One recording to transcribe.</summary>
/// <param name="ContentType">The media type without parameters; it names the file.</param>
/// <param name="Language">An ISO-639-1 hint, or null for the model to detect.</param>
public sealed record AzureFoundryTranscriptionRequest(ReadOnlyMemory<byte> Audio, string ContentType, string? Language);

/// <summary>The text, or that there is none. A failure carries nothing Foundry said: its
/// message may quote the input.</summary>
public sealed record AzureFoundryTranscriptionResult(bool Succeeded, string? Text)
{
    public static AzureFoundryTranscriptionResult Failure { get; } = new(false, null);

    public static AzureFoundryTranscriptionResult Success(string text) => new(true, text);
}

/// <summary>Transcribes one recording through a Foundry transcription deployment.</summary>
public interface IAzureFoundryTranscriptionClient
{
    /// <summary>Answers the text, or <see cref="AzureFoundryTranscriptionResult.Failure"/> when
    /// Foundry refused, failed, was unreachable or ran past the budget. Throws only when
    /// <paramref name="cancellationToken"/> is cancelled.</summary>
    Task<AzureFoundryTranscriptionResult> TranscribeAsync(AzureFoundryTranscriptionRequest request, CancellationToken cancellationToken = default);
}

/// <summary>
/// <c>POST {Endpoint}/openai/deployments/{Deployment}/audio/transcriptions?api-version=…</c>
/// with a multipart body — <c>file</c> under an ASCII name derived from the type,
/// <c>response_format=json</c>, and <c>language</c> only when there is a hint — and an Entra
/// bearer token or nothing (local ADR 0045 §3, §5).
/// <para>
/// <b>Nothing it sees is logged.</b> A failure is logged by its status code and Foundry's
/// error <i>code</i>; never by Foundry's message, the audio or the text.
/// </para>
/// <para>
/// The retry and the budget are the pipeline's, put on by
/// <see cref="AzureFoundryTranscriptionRegistration.AddAzureFoundryTranscriptionClient"/>; the
/// client holds the token request to the same budget. The body is built from bytes in memory,
/// so the retry sends it again whole.
/// </para>
/// </summary>
public sealed partial class AzureFoundryTranscriptionClient(
    HttpClient httpClient,
    IOptions<AzureFoundryTranscriptionOptions> options,
    AzureFoundryTranscriptionTokens tokens,
    ILogger<AzureFoundryTranscriptionClient> logger) : IAzureFoundryTranscriptionClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<AzureFoundryTranscriptionResult> TranscribeAsync(
        AzureFoundryTranscriptionRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var settings = options.Value;
        if (settings.Endpoint is null)
        {
            LogNotConfigured(logger);
            return AzureFoundryTranscriptionResult.Failure;
        }

        // The whole call shares one budget (local ADR 0045 §3): the token as well as the send,
        // because the identity endpoint answers on its own schedule — slow on a cold start —
        // and the pipeline's timeout only starts once the request is sent. The pipeline keeps
        // its own bound on the send and the retry; this one is the outer edge.
        using var budget = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        budget.CancelAfter(settings.TotalBudget);

        try
        {
            using var message = new HttpRequestMessage(HttpMethod.Post, RequestUri(settings))
            {
                Content = Body(request),
            };

            if (tokens.Credential is { } credential)
            {
                var token = await credential.GetTokenAsync(new TokenRequestContext([AzureFoundryTranscriptionTokens.Scope]), budget.Token)
                    .ConfigureAwait(false);
                message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token.Token);
            }

            using var response = await httpClient.SendAsync(message, budget.Token).ConfigureAwait(false);
            var payload = await response.Content.ReadAsStringAsync(budget.Token).ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                LogRefused(logger, (int)response.StatusCode, ErrorCode(payload));
                return AzureFoundryTranscriptionResult.Failure;
            }

            var text = Text(payload);
            if (text is null)
            {
                LogNoText(logger);
                return AzureFoundryTranscriptionResult.Failure;
            }

            return AzureFoundryTranscriptionResult.Success(text);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (exception is HttpRequestException or TimeoutRejectedException or OperationCanceledException or Azure.Identity.AuthenticationFailedException)
        {
            // The type alone: an exception's message is not Foundry's, but nothing about the
            // call needs more than which way it failed.
            LogUnreachable(logger, exception.GetType().Name);
            return AzureFoundryTranscriptionResult.Failure;
        }
    }

    /// <summary>The ASCII file name Foundry wants, from the media type it names.</summary>
    internal static string FileName(string contentType) => contentType.Trim().ToLowerInvariant() switch
    {
        "audio/mp4" or "audio/x-m4a" or "audio/m4a" => "dictation.m4a",
        "audio/webm" => "dictation.webm",
        "audio/wav" or "audio/x-wav" or "audio/wave" => "dictation.wav",
        "audio/mpeg" or "audio/mp3" => "dictation.mp3",
        var other when other.Split('/') is [_, var subtype] && subtype.Length is > 0 and <= 8 && subtype.All(char.IsAsciiLetterOrDigit)
            => $"dictation.{subtype}",
        _ => "dictation.bin",
    };

    private static Uri RequestUri(AzureFoundryTranscriptionOptions settings) => new(
        settings.Endpoint!,
        $"/openai/deployments/{Uri.EscapeDataString(settings.Deployment)}/audio/transcriptions?api-version={Uri.EscapeDataString(settings.ApiVersion)}");

    private static MultipartFormDataContent Body(AzureFoundryTranscriptionRequest request)
    {
        var file = new ReadOnlyMemoryContent(request.Audio);
        file.Headers.ContentType = new MediaTypeHeaderValue(request.ContentType.Trim().ToLowerInvariant());

        var body = new MultipartFormDataContent
        {
            { file, "file", FileName(request.ContentType) },
            { new StringContent("json"), "response_format" },
        };

        if (!string.IsNullOrWhiteSpace(request.Language))
        {
            body.Add(new StringContent(request.Language), "language");
        }

        return body;
    }

    private static string? Text(string payload)
    {
        try
        {
            return JsonSerializer.Deserialize<TranscriptionPayload>(payload, JsonOptions)?.Text;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>Foundry's <c>error.code</c>, cut short and kept to the characters a code is
    /// made of, so even a malformed answer cannot carry text into the log.</summary>
    private static string ErrorCode(string payload)
    {
        try
        {
            var code = JsonSerializer.Deserialize<ErrorPayload>(payload, JsonOptions)?.Error?.Code;
            if (string.IsNullOrWhiteSpace(code)) return "none";

            var kept = new string(code.Where(c => char.IsAsciiLetterOrDigit(c) || c is '_' or '-' or '.').Take(64).ToArray());
            return kept.Length == 0 ? "none" : kept;
        }
        catch (JsonException)
        {
            return "unreadable";
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Foundry transcription refused with status {StatusCode} and error code {ErrorCode}.")]
    private static partial void LogRefused(ILogger logger, int statusCode, string errorCode);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Foundry transcription answered success without text.")]
    private static partial void LogNoText(ILogger logger);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Foundry transcription did not complete: {Failure}.")]
    private static partial void LogUnreachable(ILogger logger, string failure);

    [LoggerMessage(Level = LogLevel.Error, Message = "Foundry transcription has no endpoint configured.")]
    private static partial void LogNotConfigured(ILogger logger);

    private sealed record TranscriptionPayload([property: JsonPropertyName("text")] string? Text);

    private sealed record ErrorPayload([property: JsonPropertyName("error")] ErrorBody? Error);

    private sealed record ErrorBody([property: JsonPropertyName("code")] string? Code);
}
