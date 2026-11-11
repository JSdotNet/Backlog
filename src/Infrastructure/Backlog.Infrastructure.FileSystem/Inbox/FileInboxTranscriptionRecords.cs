using System.Text.Json;
using Backlog.Modules.Inbox.Abstractions.Services;

namespace Backlog.Infrastructure.FileSystem.Inbox;

/// <summary>
/// The recordings this desktop transcribed (local ADR 0049 §1, §6), as one JSON file beside
/// the inbox's files: <c>transcriptions.json</c> in the workspace's inbox folder, read per
/// call so a moved workspace takes it along with the notes it describes.
/// <para>
/// Small by design — one entry per recording, a few kilobytes of text each — so the whole
/// file is read and rewritten on each save, under a lock, through a temporary file so a
/// crash mid-write cannot leave half a file. A file that cannot be read is treated as empty
/// for a lookup, and a save then refuses rather than overwrite records it could not read.
/// </para>
/// </summary>
public sealed class FileInboxTranscriptionRecords(Func<string> inboxDirectory) : IInboxTranscriptionRecords
{
    public const string FileName = "transcriptions.json";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    private readonly SemaphoreSlim _gate = new(1, 1);

    public async Task<InboxTranscriptionRecordDto?> FindAsync(string sha256, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sha256);

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var records = await ReadAsync(cancellationToken).ConfigureAwait(false) ?? [];
            return records.FirstOrDefault(record => string.Equals(record.Sha256, sha256, StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task SaveAsync(InboxTranscriptionRecordDto record, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(record);

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var records = await ReadAsync(cancellationToken).ConfigureAwait(false)
                ?? throw new IOException($"The transcription records in {PathOf()} could not be read, so they were not rewritten.");

            records.RemoveAll(existing => string.Equals(existing.Sha256, record.Sha256, StringComparison.OrdinalIgnoreCase));
            records.Add(record with { Sha256 = record.Sha256.ToLowerInvariant() });

            var path = PathOf();
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var temporary = path + ".tmp";
            await File.WriteAllTextAsync(temporary, JsonSerializer.Serialize(records, JsonOptions), cancellationToken).ConfigureAwait(false);
            File.Move(temporary, path, overwrite: true);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>The records, an empty list when there is no file, or null when there is one
    /// that cannot be read.</summary>
    private async Task<List<InboxTranscriptionRecordDto>?> ReadAsync(CancellationToken cancellationToken)
    {
        var path = PathOf();
        if (!File.Exists(path)) return [];

        try
        {
            var stream = File.OpenRead(path);
            await using var _ = stream.ConfigureAwait(false);
            return await JsonSerializer.DeserializeAsync<List<InboxTranscriptionRecordDto>>(stream, JsonOptions, cancellationToken).ConfigureAwait(false) ?? [];
        }
        catch (Exception failure) when (failure is IOException or UnauthorizedAccessException or JsonException)
        {
            return null;
        }
    }

    private string PathOf() => Path.Combine(inboxDirectory(), FileName);
}
