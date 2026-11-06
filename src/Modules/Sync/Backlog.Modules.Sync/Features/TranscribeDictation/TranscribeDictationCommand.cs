using System.Diagnostics;
using Backlog.Modules.Sync.Abstractions;
using Backlog.Modules.Sync.Abstractions.DataTransferObjects;
using Backlog.Modules.Sync.DomainModels;
using Backlog.Modules.Sync.Observability;
using Backlog.Modules.Sync.Ports;
using Backlog.SharedKernel.Handlers;
using Backlog.SharedKernel.Results;

namespace Backlog.Modules.Sync.Features.TranscribeDictation;

/// <summary>A device sends one recorded dictation to be turned into text (local ADR 0045).
/// The endpoint has already checked the declared type against the allowlist, the declared
/// length against the cap, and the language hint; what is left is the part only reading the
/// body can settle.</summary>
/// <param name="ContentType">The declared media type without its parameters.</param>
/// <param name="Language">The language hint, lowercase, or null.</param>
/// <param name="DeclaredLength">The body's <c>Content-Length</c>, when it declared one — the
/// size the buffer starts at, never trusted as the size.</param>
/// <param name="MaxBytes">The cap, so a body with no length, or one that lied about it, is
/// stopped where it passes the cap.</param>
public sealed record TranscribeDictationCommand(
    OwnerScope Scope,
    Stream Content,
    string ContentType,
    string? Language,
    long? DeclaredLength,
    long MaxBytes);

/// <summary>
/// Reads the recording into memory, bounded by the cap, and hands it to the transcriber.
/// <para>
/// <b>Memory, never disk.</b> A retry has to send the bytes again, so the body is read once
/// into a buffer of at most the cap and dropped when the call returns. It never goes through
/// request buffering, which spills to a temporary file.
/// </para>
/// <para>
/// <b>Nothing is kept, nothing is told.</b> The text goes back to the caller and nowhere
/// else; the trace and the metrics carry the size, the type, the duration and the outcome,
/// and a failure's detail is this handler's own sentence.
/// </para>
/// </summary>
public sealed class TranscribeDictationCommandHandler(IDictationTranscriber transcriber, TimeProvider clock)
    : ICommandHandler<TranscribeDictationCommand, Result<TranscriptionResponse>>
{
    public async Task<Result<TranscriptionResponse>> Handle(
        TranscribeDictationCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        using var activity = SyncTelemetry.Source.StartActivity("TranscribeDictation");
        activity?.SetTag(SyncTelemetry.OwnerIdTag, command.Scope.OwnerId.ToString());
        activity?.SetTag(SyncTelemetry.ContentTypeTag, command.ContentType);

        var started = clock.GetTimestamp();

        var audio = await ReadBounded(command, cancellationToken);
        if (audio is null)
        {
            Record(activity, command.ContentType, size: null, SyncTelemetry.TranscriptionOutcomes.TooLarge, started);
            return Result.Failure<TranscriptionResponse>(TooLarge(command.MaxBytes));
        }

        activity?.SetTag(SyncTelemetry.SizeTag, audio.Value.Length);

        var answer = await transcriber.TranscribeAsync(
            new DictationAudio(audio.Value, command.ContentType, command.Language),
            cancellationToken);

        switch (answer.Status)
        {
            case DictationTranscriptionStatus.Transcribed:
                Record(activity, command.ContentType, audio.Value.Length, SyncTelemetry.TranscriptionOutcomes.Transcribed, started);
                return new TranscriptionResponse(answer.Text ?? string.Empty);

            case DictationTranscriptionStatus.Unavailable:
                Record(activity, command.ContentType, audio.Value.Length, SyncTelemetry.TranscriptionOutcomes.Unavailable, started);
                return Result.Failure<TranscriptionResponse>(new Error(
                    SyncErrorCodes.TranscriptionUnavailable,
                    "This sync service has no transcription model configured; dictate on the device instead."));

            default:
                Record(activity, command.ContentType, audio.Value.Length, SyncTelemetry.TranscriptionOutcomes.Failed, started);
                activity?.SetStatus(ActivityStatusCode.Error);
                return Result.Failure<TranscriptionResponse>(new Error(
                    SyncErrorCodes.TranscriptionFailed,
                    "The transcription model did not answer with text; try again."));
        }
    }

    /// <summary>The refusal for a recording over the cap, worded the same whether the host saw
    /// the length up front or the body ran past it.</summary>
    public static Error TooLarge(long maxBytes) => new(
        SyncErrorCodes.TranscriptionTooLarge,
        $"A recording's Content-Length may be at most {maxBytes} bytes; this one was larger.",
        ErrorType.Validation);

    /// <summary>The most the buffer starts at, whatever the body declares: a declared length
    /// costs nothing to send, so it buys no memory until the bytes arrive.</summary>
    private const int MaxInitialCapacity = 1024 * 1024;

    /// <summary>The body, or null once it passes the cap. Reads at most one byte past the
    /// cap, which is how it knows.
    /// <para>
    /// One copy: the answer is a view over the buffer's own array rather than a copy of it,
    /// and the buffer starts at no more than <see cref="MaxInitialCapacity"/>, so a request
    /// holds about what it has sent rather than twice what it declared.
    /// </para></summary>
    private static async Task<ReadOnlyMemory<byte>?> ReadBounded(TranscribeDictationCommand command, CancellationToken cancellationToken)
    {
        var initial = (int)Math.Clamp(command.DeclaredLength ?? 64 * 1024, 0, Math.Min(command.MaxBytes, MaxInitialCapacity));
        using var buffer = new MemoryStream(initial);
        var chunk = new byte[81920];

        while (true)
        {
            var read = await command.Content.ReadAsync(chunk, cancellationToken);
            if (read == 0) break;

            if (buffer.Length + read > command.MaxBytes) return null;

            buffer.Write(chunk, 0, read);
        }

        // The segment holds the array itself, so disposing the stream leaves it whole.
        return buffer.TryGetBuffer(out var written) ? written.AsMemory() : buffer.ToArray();
    }

    private void Record(Activity? activity, string contentType, long? size, string outcome, long started)
    {
        var elapsed = clock.GetElapsedTime(started);
        activity?.SetTag(SyncTelemetry.OutcomeTag, outcome);

        var tags = new TagList
        {
            { SyncTelemetry.ContentTypeTag, contentType },
            { SyncTelemetry.OutcomeTag, outcome },
        };

        SyncTelemetry.TranscriptionDuration.Record(elapsed.TotalSeconds, tags);
        if (size is { } bytes) SyncTelemetry.TranscriptionSize.Record(bytes, tags);
    }
}
