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
/// <item><b>Format.</b> AAC in ADTS is remuxed to M4A parts of at most five minutes by
/// <see cref="AdtsRemuxer"/>; an M4A file is sent as it is, whole.</item>
/// <item><b>Each part is headed by its offset</b>, <c>[00:04:59]</c>, because the model
/// answers <c>json</c> only and gives no timestamps of its own.</item>
/// <item><b>Each part's prompt is the last words of the one before</b>, so a sentence cut
/// at a boundary reads on. No language is sent, and no speaker is labelled.</item>
/// </list>
/// Nothing Foundry said reaches a log or the answer: a failure names the step that failed.
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

        var parts = Parts(recording);
        if (parts.Count == 0) return InboxErrors.TranscriptionFailed("The recording holds no audio to transcribe.");

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
                return InboxErrors.TranscriptionFailed($"Part {index + 1} of {parts.Count} could not be transcribed.");
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

    private static List<(ReadOnlyMemory<byte> Audio, TimeSpan Offset)> Parts(InboxRecordingDto recording)
    {
        if (AdtsRemuxer.LooksLikeAdts(recording.Audio.Span))
        {
            return AdtsRemuxer.Remux(recording.Audio.Span)
                .Select(part => ((ReadOnlyMemory<byte>)part.M4a, part.Offset))
                .ToList();
        }

        // Already a container (audio/mp4, audio/x-m4a): sent as it is, with no remux.
        return recording.Audio.IsEmpty ? [] : [(recording.Audio, TimeSpan.Zero)];
    }

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
