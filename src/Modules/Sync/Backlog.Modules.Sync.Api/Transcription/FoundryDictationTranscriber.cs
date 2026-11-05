using Backlog.Infrastructure.AzureFoundry;
using Backlog.Modules.Sync.Ports;

namespace Backlog.Modules.Sync.Api.Transcription;

/// <summary>
/// The module's transcriber port answered by the Foundry transcription client (local ADR
/// 0045 §3). It lives in the host rather than in <c>Backlog.Infrastructure.AzureFoundry</c>
/// so that adapter takes no reference to the Sync module: the desktop composes that adapter
/// too, and would otherwise carry the server-side module with it. The host is what picks an
/// adapter for a port, and this is that choice written down.
/// </summary>
internal sealed class FoundryDictationTranscriber(IAzureFoundryTranscriptionClient client) : IDictationTranscriber
{
    public async Task<DictationTranscription> TranscribeAsync(DictationAudio audio, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(audio);

        var result = await client.TranscribeAsync(
            new AzureFoundryTranscriptionRequest(audio.Content, audio.ContentType, audio.Language),
            cancellationToken);

        return result.Succeeded
            ? DictationTranscription.Transcribed(result.Text ?? string.Empty)
            : DictationTranscription.Failed;
    }
}
