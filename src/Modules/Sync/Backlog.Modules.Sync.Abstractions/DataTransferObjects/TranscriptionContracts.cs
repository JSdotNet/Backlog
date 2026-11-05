namespace Backlog.Modules.Sync.Abstractions.DataTransferObjects;

/// <summary>
/// What <c>POST /api/sync/transcriptions</c> answers with on success (local ADR 0045 §1):
/// the text of the one recording it was sent, as <c>{ "text": "…" }</c>. The service keeps
/// neither the audio nor this text.
/// </summary>
public sealed record TranscriptionResponse(string Text);
