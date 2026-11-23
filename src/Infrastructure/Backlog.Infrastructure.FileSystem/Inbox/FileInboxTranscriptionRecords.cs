using System.Text.Json;
using Backlog.Modules.Inbox.Abstractions.Services;

namespace Backlog.Infrastructure.FileSystem.Inbox;

/// <summary>
/// The recordings this desktop transcribed (local ADR 0049 §1, §6), as one JSON file beside
/// the inbox's files: <c>transcriptions.json</c> in the workspace's inbox folder, read per
/// call so a moved workspace takes it along with the notes it describes. The failed
/// attempts sit beside it in <c>transcription-failures.json</c>, a file of their own so a
/// failure record that cannot be read never stands in the way of the done records.
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

    public const string FailuresFileName = "transcription-failures.json";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    private readonly SemaphoreSlim _gate = new(1, 1);

    public async Task<InboxTranscriptionRecordDto?> FindAsync(string sha256, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sha256);

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var records = await ReadAsync<InboxTranscriptionRecordDto>(FileName, cancellationToken).ConfigureAwait(false) ?? [];
            return records.FirstOrDefault(record => Same(record.Sha256, sha256));
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
            var records = await ReadAsync<InboxTranscriptionRecordDto>(FileName, cancellationToken).ConfigureAwait(false)
                ?? throw Unreadable(FileName);

            records.RemoveAll(existing => Same(existing.Sha256, record.Sha256));
            records.Add(record with { Sha256 = record.Sha256.ToLowerInvariant() });

            await WriteAsync(FileName, records, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<InboxTranscriptionFailureDto?> FindFailureAsync(string sha256, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sha256);

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var failures = await ReadAsync<InboxTranscriptionFailureDto>(FailuresFileName, cancellationToken).ConfigureAwait(false) ?? [];
            return failures.FirstOrDefault(failure => Same(failure.Sha256, sha256));
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<InboxTranscriptionFailureDto> CountFailureAsync(string sha256, string reason, DateTimeOffset at, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sha256);
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var failures = await ReadAsync<InboxTranscriptionFailureDto>(FailuresFileName, cancellationToken).ConfigureAwait(false)
                ?? throw Unreadable(FailuresFileName);

            var attempts = (failures.FirstOrDefault(failure => Same(failure.Sha256, sha256))?.Attempts ?? 0) + 1;
            var counted = new InboxTranscriptionFailureDto(sha256.ToLowerInvariant(), attempts, reason, at);

            failures.RemoveAll(failure => Same(failure.Sha256, sha256));
            failures.Add(counted);

            await WriteAsync(FailuresFileName, failures, cancellationToken).ConfigureAwait(false);
            return counted;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task ClearFailureAsync(string sha256, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sha256);

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var failures = await ReadAsync<InboxTranscriptionFailureDto>(FailuresFileName, cancellationToken).ConfigureAwait(false)
                ?? throw Unreadable(FailuresFileName);

            if (failures.RemoveAll(failure => Same(failure.Sha256, sha256)) == 0) return;

            await WriteAsync(FailuresFileName, failures, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<InboxTranscriptionFailureDto?> LatestFailureAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var failures = await ReadAsync<InboxTranscriptionFailureDto>(FailuresFileName, cancellationToken).ConfigureAwait(false) ?? [];
            var latest = failures.MaxBy(failure => failure.At);
            if (latest is null) return null;

            // A failure record outlives a note archived or deleted before its recording was
            // ever written, since only a success or a Retry removes it. A recording
            // transcribed since means the last transcription did not fail.
            var records = await ReadAsync<InboxTranscriptionRecordDto>(FileName, cancellationToken).ConfigureAwait(false) ?? [];
            return records.Any(record => record.WrittenAt > latest.At) ? null : latest;
        }
        finally
        {
            _gate.Release();
        }
    }

    private static bool Same(string left, string right) => string.Equals(left, right, StringComparison.OrdinalIgnoreCase);

    private IOException Unreadable(string fileName) =>
        new($"The transcription records in {PathOf(fileName)} could not be read, so they were not rewritten.");

    /// <summary>The entries, an empty list when there is no file, or null when there is one
    /// that cannot be read.</summary>
    private async Task<List<T>?> ReadAsync<T>(string fileName, CancellationToken cancellationToken)
    {
        var path = PathOf(fileName);
        if (!File.Exists(path)) return [];

        try
        {
            var stream = File.OpenRead(path);
            await using var _ = stream.ConfigureAwait(false);
            return await JsonSerializer.DeserializeAsync<List<T>>(stream, JsonOptions, cancellationToken).ConfigureAwait(false) ?? [];
        }
        catch (Exception failure) when (failure is IOException or UnauthorizedAccessException or JsonException)
        {
            return null;
        }
    }

    private async Task WriteAsync<T>(string fileName, List<T> entries, CancellationToken cancellationToken)
    {
        var path = PathOf(fileName);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporary = path + ".tmp";
        await File.WriteAllTextAsync(temporary, JsonSerializer.Serialize(entries, JsonOptions), cancellationToken).ConfigureAwait(false);
        File.Move(temporary, path, overwrite: true);
    }

    private string PathOf(string fileName) => Path.Combine(inboxDirectory(), fileName);
}
