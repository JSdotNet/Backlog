using Backlog.Modules.Sync.Abstractions;
using Backlog.Modules.Sync.Abstractions.DataTransferObjects;
using Backlog.Modules.Sync.Api.Options;
using Backlog.Modules.Sync.Api.Security;
using Backlog.Modules.Sync.Features.TranscribeDictation;
using Backlog.SharedKernel.Handlers;
using Backlog.SharedKernel.Results;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Microsoft.Net.Http.Headers;

namespace Backlog.Modules.Sync.Api.Endpoints;

/// <summary>
/// The dictation relay (local ADR 0045 §1): a paired device posts one recording, and the
/// service hands it to Foundry and answers the text. Behind the same bearer, fallback-deny
/// policy and owner scope as every other route, and a pure function — no id, no digest, no
/// store — so a retry is simply a second call.
/// <para>
/// The cap, the allowlist and the language hint are refused here, before the body is read,
/// and the cap once more while it is read, as the attachment route does (local ADR 0014).
/// </para>
/// </summary>
internal static class TranscriptionEndpoints
{
    internal static IEndpointRouteBuilder MapTranscriptionEndpoints(this IEndpointRouteBuilder sync)
    {
        var limits = sync.ServiceProvider.GetRequiredService<IOptions<SyncTranscriptionOptions>>().Value;

        var transcriptions = sync.MapGroup(string.Empty)
            .WithTags("Transcriptions")
            .RequireAuthorization(SyncPolicies.PairedDevice)
            .RequireOwnerScope();

        // The cap as the host's own request-body limit too, so Kestrel refuses a declared
        // length over it before a byte is read and cuts off a body that runs past it. The
        // handler's bounded read is the same cap for the bodies that limit does not reach.
        transcriptions.MapPost(SyncRoutes.Transcriptions, Transcribe)
            .WithMetadata(new RequestBodyLimit(limits.MaxBytes))
            .WithSummary("Transcribes one recorded dictation and keeps none of it.");

        return sync;
    }

    /// <remarks>200 with <c>{ text }</c>; 503 with no transcription model configured; 502
    /// when the model failed.</remarks>
    private static async Task<IResult> Transcribe(
        HttpContext context,
        [FromQuery(Name = SyncRoutes.TranscriptionLanguageQuery)] string? language,
        IOptions<SyncTranscriptionOptions> options,
        ICommandHandler<TranscribeDictationCommand, Result<TranscriptionResponse>> handler,
        CancellationToken cancellationToken)
    {
        var limits = options.Value;
        var request = context.Request;

        if (Refusal(request, language, limits, out var mediaType) is { } refusal)
        {
            return SyncResults.From(context, Result.Failure<TranscriptionResponse>(refusal), Results.Ok);
        }

        Result<TranscriptionResponse> result;
        try
        {
            result = await handler.Handle(
                new TranscribeDictationCommand(
                    context.GetOwnerScope(),
                    request.Body,
                    mediaType,
                    string.IsNullOrWhiteSpace(language) ? null : language.Trim().ToLowerInvariant(),
                    request.ContentLength,
                    limits.MaxBytes),
                cancellationToken);
        }
        catch (BadHttpRequestException limit) when (limit.StatusCode == StatusCodes.Status413PayloadTooLarge)
        {
            // Kestrel enforcing the request-body limit above, mid-stream: the same refusal the
            // bounded read gives, so one mistake reads one way whichever layer noticed.
            result = Result.Failure<TranscriptionResponse>(TranscribeDictationCommandHandler.TooLarge(limits.MaxBytes));
        }

        return SyncResults.From(context, result, Results.Ok);
    }

    /// <summary>What is wrong with a recording before its body is read, or null when nothing
    /// is; <paramref name="mediaType"/> is the declared type without its parameters.</summary>
    private static Error? Refusal(HttpRequest request, string? language, SyncTranscriptionOptions limits, out string mediaType)
    {
        mediaType = string.Empty;

        if (!MediaTypeHeaderValue.TryParse(request.ContentType, out var declared) || declared.MediaType.Length == 0)
        {
            return new Error(
                SyncErrorCodes.TranscriptionTypeNotAllowed,
                "A recording has to declare its Content-Type.",
                ErrorType.Validation);
        }

        mediaType = declared.MediaType.ToString();

        if (!limits.Allows(mediaType))
        {
            return new Error(
                SyncErrorCodes.TranscriptionTypeNotAllowed,
                $"The Content-Type '{mediaType}' is not an audio type the sync service transcribes.",
                ErrorType.Validation);
        }

        if (request.ContentLength > limits.MaxBytes)
        {
            return TranscribeDictationCommandHandler.TooLarge(limits.MaxBytes);
        }

        if (!string.IsNullOrWhiteSpace(language)
            && !SyncTranscriptionLimits.Languages.Contains(language.Trim(), StringComparer.OrdinalIgnoreCase))
        {
            return Error.Validation(
                SyncErrorCodes.TranscriptionInvalid,
                $"The {SyncRoutes.TranscriptionLanguageQuery} hint '{language}' is not one the sync service passes on; "
                + $"send {string.Join(" or ", SyncTranscriptionLimits.Languages)}, or none.");
        }

        return null;
    }
}
