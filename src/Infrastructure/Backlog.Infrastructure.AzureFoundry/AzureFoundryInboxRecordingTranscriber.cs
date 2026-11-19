using System.Globalization;
using System.Text;
using Backlog.Modules.Inbox.Abstractions;
using Backlog.Modules.Inbox.Abstractions.DataTransferObjects;
using Backlog.Modules.Inbox.Abstractions.Services;
using Backlog.SharedKernel.Results;
using Microsoft.Extensions.Logging;

namespace Backlog.Infrastructure.AzureFoundry;

/// <summary>
/// Answers the Inbox's <see cref="IInboxRecordingTranscriber"/> over Foundry (local ADR
/// 0049): the recording goes to the transcription deployment in parts, and the joined
/// transcript to the chat deployment for its summary.
/// <list type="bullet">
/// <item><b>Format.</b> AAC in ADTS and an MP4 or M4A recording alike are remuxed to M4A
/// parts of at most five minutes, by <see cref="AdtsRemuxer"/> and <see cref="Mp4Demuxer"/>.
/// A file neither can read fails, as a recording that could not be read.</item>
/// <item><b>Each part is headed by its offset</b>, <c>[00:04:59]</c>, because the model
/// answers <c>json</c> only and gives no timestamps of its own.</item>
/// <item><b>Each part's prompt is the last words of the one before</b>, so a sentence cut
/// at a boundary reads on. No language is sent, and no speaker is labelled.</item>
/// </list>
/// Nothing Foundry said reaches a log or the answer: a failure names the step that failed, and
/// for a refused part the status alone says what a person can fix — a deployment Foundry does
/// not know, or a key it refused.
/// </summary>
public sealed partial class AzureFoundryInboxRecordingTranscriber(
    IAzureFoundryTranscriptionClient transcription,
    IAzureFoundrySummaryClient summary,
    AzureFoundrySettingsStore settingsStore,
    ILogger<AzureFoundryInboxRecordingTranscriber> logger) : IInboxRecordingTranscriber
{
    /// <summary>How much of the part before is carried as the next part's prompt.</summary>
    internal const int PromptTailLength = 200;

    private const string Mp4 = "audio/mp4";

    /// <summary>The summary needs the chat deployment, so the chat configuration is the
    /// test: endpoint, chat deployment and key (ADR 0049 §1).</summary>
    public bool IsAvailable => settingsStore.Current.IsConfigured;

    public async Task<Result<InboxRecordingTranscriptDto>> TranscribeAsync(InboxRecordingDto recording, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(recording);

        if (!IsAvailable) return InboxErrors.TranscriptionFailed("Azure Foundry is not configured.");

        var adts = AdtsRemuxer.LooksLikeAdts(recording.Audio.Span);
        var parts = Parts(recording, adts);
        if (parts.Count == 0)
        {
            return InboxErrors.TranscriptionFailed(adts
                ? "The recording holds no audio to transcribe."
                : "The recording could not be read as AAC or M4A audio.");
        }

        var transcript = new StringBuilder();
        string? previous = null;

        for (var index = 0; index < parts.Count; index++)
        {
            var (audio, offset) = parts[index];
            var answer = await transcription.TranscribeAsync(
                new AzureFoundryTranscriptionRequest(audio, Mp4, Language: null, Prompt: Tail(previous)),
                cancellationToken).ConfigureAwait(false);

            if (!answer.Succeeded)
            {
                LogPartFailed(logger, index + 1, parts.Count);
                return InboxErrors.TranscriptionFailed($"Part {index + 1} of {parts.Count} could not be transcribed{Why(answer.StatusCode)}.");
            }

            var text = answer.Text?.Trim() ?? string.Empty;
            if (transcript.Length > 0) transcript.Append("\n\n");
            transcript.Append('[').Append(Stamp(offset)).Append("] ").Append(text);
            if (text.Length > 0) previous = text;
        }

        if (previous is null) return InboxErrors.TranscriptionFailed("The recording was transcribed to no words.");

        string summaryMd;
        try
        {
            summaryMd = await summary.SummarizeTranscriptAsync(transcript.ToString(), cancellationToken).ConfigureAwait(false);
        }
        catch (AzureFoundryException failure)
        {
            LogSummaryFailed(logger, failure.StatusCode);
            return InboxErrors.TranscriptionFailed("The transcript could not be summarized.");
        }

        return new InboxRecordingTranscriptDto(transcript.ToString(), summaryMd);
    }

    private static List<(ReadOnlyMemory<byte> Audio, TimeSpan Offset)> Parts(InboxRecordingDto recording, bool adts)
    {
        // An M4A is cut as ADTS is, because one file from the browser may run past the
        // synchronous limit (ADR 0049 §3).
        var parts = adts ? AdtsRemuxer.Remux(recording.Audio.Span) : Mp4Demuxer.Remux(recording.Audio.Span);
        return parts.Select(part => ((ReadOnlyMemory<byte>)part.M4a, part.Offset)).ToList();
    }

    /// <summary>What a refusal's status lets a person act on, after the part's number; nothing
    /// when Foundry gave no status. Only the status: Foundry's own words may quote the audio.</summary>
    private string Why(int? statusCode) => statusCode switch
    {
        404 => $": the transcription deployment '{settingsStore.Current.TranscriptionDeployment}' was not found on the Foundry endpoint",
        401 or 403 => ": Foundry refused the API key",
        { } status => $" (Foundry answered {status})",
        null => string.Empty,
    };

    /// <summary>The last words of <paramref name="text"/>, cut on a word boundary.</summary>
    internal static string? Tail(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        if (text.Length <= PromptTailLength) return text;

        var tail = text[^PromptTailLength..];
        var space = tail.IndexOf(' ', StringComparison.Ordinal);
        return space >= 0 && space < tail.Length - 1 ? tail[(space + 1)..] : tail;
    }

    internal static string Stamp(TimeSpan offset) =>
        ((int)offset.TotalHours).ToString("00", CultureInfo.InvariantCulture) + offset.ToString(@"\:mm\:ss", CultureInfo.InvariantCulture);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Foundry did not transcribe part {Part} of {Parts} of a recording.")]
    private static partial void LogPartFailed(ILogger logger, int part, int parts);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Foundry did not summarize a transcript (status {StatusCode}).")]
    private static partial void LogSummaryFailed(ILogger logger, int? statusCode);
}
