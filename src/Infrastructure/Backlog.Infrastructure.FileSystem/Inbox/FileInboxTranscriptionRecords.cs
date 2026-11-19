using System.Globalization;
using System.Text.Json;
using Backlog.Modules.Inbox.Abstractions.Services;

namespace Backlog.Infrastructure.FileSystem.Inbox;

/// <summary>
/// The recordings this desktop transcribed (local ADR 0049 §1, §6), as one JSON file beside
/// the inbox's files: <c>transcriptions.json</c> in the workspace's inbox folder, read per
/// call so a moved workspace takes it along with the notes it describes. The failure records
/// are a sibling, <c>transcription-failures.json</c>, so the done records keep the shape
/// every earlier build wrote and read.
/// <para>
/// Small by design — one entry per recording, a few kilobytes of text each — so a whole
/// file is read and rewritten on each save, under one lock, through a temporary file so a
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

        var records = await LockedAsync(() => ReadAsync<InboxTranscriptionRecordDto>(FileName, setAsideCorrupt: false, cancellationToken), cancellationToken).ConfigureAwait(false) ?? [];
        return records.FirstOrDefault(record => Same(record.Sha256, sha256));
    }

    public Task SaveAsync(InboxTranscriptionRecordDto record, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(record);

        return LockedAsync(async () =>
        {
            var records = await ReadForWriteAsync<InboxTranscriptionRecordDto>(FileName, setAsideCorrupt: false, cancellationToken).ConfigureAwait(false);
            records.RemoveAll(existing => Same(existing.Sha256, record.Sha256));
            records.Add(record with { Sha256 = record.Sha256.ToLowerInvariant() });
            await WriteAsync(FileName, records, cancellationToken).ConfigureAwait(false);
            return true;
        }, cancellationToken);
    }

    public async Task<InboxTranscriptionFailureDto?> FindFailureAsync(string sha256, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sha256);

        var failures = await LockedAsync(() => ReadAsync<InboxTranscriptionFailureDto>(FailuresFileName, setAsideCorrupt: true, cancellationToken), cancellationToken).ConfigureAwait(false) ?? [];
        return failures.FirstOrDefault(failure => Same(failure.Sha256, sha256));
    }

    public Task<InboxTranscriptionFailureDto> RecordFailureAsync(
        string sha256,
        Guid noteId,
        Guid attachmentId,
        string reason,
        DateTimeOffset failedAt,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sha256);
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);

        return LockedAsync(async () =>
        {
            var failures = await ReadForWriteAsync<InboxTranscriptionFailureDto>(FailuresFileName, setAsideCorrupt: true, cancellationToken).ConfigureAwait(false);
            var attempts = failures.FirstOrDefault(existing => Same(existing.Sha256, sha256))?.Attempts ?? 0;
            failures.RemoveAll(existing => Same(existing.Sha256, sha256));

            var failure = new InboxTranscriptionFailureDto(sha256.ToLowerInvariant(), noteId, attachmentId, attempts + 1, reason, failedAt);
            failures.Add(failure);
            await WriteAsync(FailuresFileName, failures, cancellationToken).ConfigureAwait(false);
            return failure;
        }, cancellationToken);
    }

    public Task ClearFailureAsync(string sha256, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sha256);

        return LockedAsync(async () =>
        {
            if (!File.Exists(PathOf(FailuresFileName))) return true;

            var failures = await ReadForWriteAsync<InboxTranscriptionFailureDto>(FailuresFileName, setAsideCorrupt: true, cancellationToken).ConfigureAwait(false);
            if (failures.RemoveAll(existing => Same(existing.Sha256, sha256)) > 0)
            {
                await WriteAsync(FailuresFileName, failures, cancellationToken).ConfigureAwait(false);
            }

            return true;
        }, cancellationToken);
    }

    public async Task<IReadOnlyList<InboxTranscriptionFailureDto>> ListFailuresAsync(CancellationToken cancellationToken = default) =>
        await LockedAsync(() => ReadAsync<InboxTranscriptionFailureDto>(FailuresFileName, setAsideCorrupt: true, cancellationToken), cancellationToken).ConfigureAwait(false) ?? [];

    private static bool Same(string left, string right) => string.Equals(left, right, StringComparison.OrdinalIgnoreCase);

    private async Task<T> LockedAsync<T>(Func<Task<T>> work, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return await work().ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<List<T>> ReadForWriteAsync<T>(string name, bool setAsideCorrupt, CancellationToken cancellationToken) =>
        await ReadAsync<T>(name, setAsideCorrupt, cancellationToken).ConfigureAwait(false)
            ?? throw new IOException($"The transcription records in {PathOf(name)} could not be read, so they were not rewritten.");

    /// <summary>The entries, an empty list when there is no file, or null when there is one
    /// that cannot be read.
    /// <para>
    /// With <paramref name="setAsideCorrupt"/>, a file whose content is not the records is
    /// renamed <c>.corrupt-&lt;timestamp&gt;</c> and read as empty. That is the failure file's
    /// rule: refusing every write, as the done records do, would stop failures being counted
    /// at all and leave a failing recording sent on every pull; starting again gives each file
    /// three more attempts and keeps the bound. A file that cannot be opened is not set aside,
    /// because it may only be locked for the moment.
    /// </para>
    /// </summary>
    private async Task<List<T>?> ReadAsync<T>(string name, bool setAsideCorrupt, CancellationToken cancellationToken)
    {
        var path = PathOf(name);
        if (!File.Exists(path)) return [];

        try
        {
            var stream = File.OpenRead(path);
            await using var _ = stream.ConfigureAwait(false);
            return await JsonSerializer.DeserializeAsync<List<T>>(stream, JsonOptions, cancellationToken).ConfigureAwait(false) ?? [];
        }
        catch (JsonException) when (setAsideCorrupt)
        {
            // A file that cannot be moved aside — locked, or already moved by another
            // instance — reads as unreadable rather than failing the caller.
            try
            {
                File.Move(path, $"{path}.corrupt-{DateTime.UtcNow.ToString("yyyyMMdd'T'HHmmss'Z'", CultureInfo.InvariantCulture)}-{Guid.NewGuid():N}");
                return [];
            }
            catch (Exception failure) when (failure is IOException or UnauthorizedAccessException)
            {
                return null;
            }
        }
        catch (Exception failure) when (failure is IOException or UnauthorizedAccessException or JsonException)
        {
            return null;
        }
    }

    private async Task WriteAsync<T>(string name, List<T> entries, CancellationToken cancellationToken)
    {
        var path = PathOf(name);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        // A name of its own per write, so two writers — another instance of the app on the
        // same workspace — never write through the same temporary file.
        var temporary = $"{path}.{Guid.NewGuid():N}.tmp";
        await File.WriteAllTextAsync(temporary, JsonSerializer.Serialize(entries, JsonOptions), cancellationToken).ConfigureAwait(false);
        File.Move(temporary, path, overwrite: true);
    }

    private string PathOf(string name) => Path.Combine(inboxDirectory(), name);
}
