using Backlog.Modules.Sync.Ports;

namespace Backlog.Modules.Sync.Adapters;

/// <summary>
/// The transcriber a host without a transcription endpoint runs on — a bare
/// <c>dotnet run</c>, the endpoint tests, and a deployed service whose Foundry setting is
/// missing: every recording is answered "unavailable", so the route says 503 and the phone
/// falls back to on-device dictation (local ADR 0045 §6). Registered with <c>TryAdd</c>, so
/// the Foundry-backed transcriber wins wherever a host configures one.
/// </summary>
public sealed class NoDictationTranscriber : IDictationTranscriber
{
    public Task<DictationTranscription> TranscribeAsync(DictationAudio audio, CancellationToken cancellationToken = default) =>
        Task.FromResult(DictationTranscription.Unavailable);
}
