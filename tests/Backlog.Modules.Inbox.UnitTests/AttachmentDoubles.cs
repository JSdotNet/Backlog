using System.Security.Cryptography;

using Backlog.Modules.Inbox.Abstractions.DataTransferObjects;
using Backlog.Modules.Inbox.Abstractions.Services;
using Backlog.SharedKernel.Results;

using Microsoft.Extensions.Logging;

namespace Backlog.Modules.Inbox.UnitTests;

/// <summary>One entry a <see cref="RecordingLogger{T}"/> saw: its level, its
/// structured fields by name, and the exception it carried.</summary>
internal sealed record LoggedEntry(LogLevel Level, IReadOnlyDictionary<string, object?> Fields, Exception? Exception);

/// <summary>A logger that keeps what it is told, so a test can say what a
/// failure left in the structured logs.</summary>
internal sealed class RecordingLogger<T> : ILogger<T>
{
    public List<LoggedEntry> Entries { get; } = [];

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        var fields = state is IEnumerable<KeyValuePair<string, object?>> pairs
            ? pairs.ToDictionary(pair => pair.Key, pair => pair.Value)
            : [];

        Entries.Add(new LoggedEntry(logLevel, fields, exception));
    }
}

/// <summary>A file as a test wants one: bytes, and the capture metadata that
/// names them with the digest they really hash to.</summary>
internal sealed record TestFile(Guid Id, string Name, string ContentType, byte[] Bytes)
{
    public static TestFile Jpeg(string name = "whiteboard.jpg") =>
        new(Guid.CreateVersion7(), name, "image/jpeg", [0xFF, 0xD8, 0xFF, 0xE0, 1, 2, 3]);

    public static TestFile Pdf(string name = "contract.pdf") =>
        new(Guid.CreateVersion7(), name, "application/pdf", "%PDF-1.7 test"u8.ToArray());

    public string Sha256 => Convert.ToHexStringLower(SHA256.HashData(Bytes));

    public InboxCaptureAttachmentDto Named => new(Id, Name, ContentType, Bytes.Length, Sha256);
}

/// <summary>
/// The attachment store behind the sync service, from memory: whatever a test
/// put in <see cref="Blobs"/> is there, an id it did not is not found, and
/// <see cref="FailWith"/> makes every fetch fail the way an unreachable service
/// does. Counts fetches, which is half of what "downloaded once" means.
/// </summary>
internal sealed class FakeAttachmentSource : IInboxAttachmentSource
{
    public Dictionary<Guid, byte[]> Blobs { get; } = [];

    public List<Guid> Fetches { get; } = [];

    public Error? FailWith { get; set; }

    /// <summary>Throws instead of answering — an adapter bug, or a transport
    /// failure it did not translate.</summary>
    public Exception? ThrowWith { get; set; }

    public FakeAttachmentSource Holding(params TestFile[] files)
    {
        foreach (var file in files) Blobs[file.Id] = file.Bytes;
        return this;
    }

    public Task<Result<byte[]>> FetchAsync(Guid attachmentId, CancellationToken cancellationToken = default)
    {
        Fetches.Add(attachmentId);

        if (ThrowWith is { } thrown) throw thrown;
        if (FailWith is { } error) return Task.FromResult(Result.Failure<byte[]>(error));

        return Task.FromResult(Blobs.TryGetValue(attachmentId, out var bytes)
            ? Result.Success(bytes)
            : Result.Failure<byte[]>(Error.NotFound("sync.attachment_not_found", "The file is no longer on the sync service.")));
    }
}

/// <summary>
/// The workspace's attachment folders, from memory: a path is the item id and
/// the file name joined, and a write is recorded. Counts writes, which is the
/// other half of "downloaded once".
/// </summary>
internal sealed class FakeAttachmentFiles : IInboxAttachmentFiles
{
    public Dictionary<string, byte[]> Files { get; } = new(StringComparer.OrdinalIgnoreCase);

    public List<string> Writes { get; } = [];

    public List<string> OpenedPaths { get; } = [];

    public string FolderFor(Guid itemId) => $"inbox/attachments/{itemId:D}";

    public string PathFor(Guid itemId, string fileName) => $"{FolderFor(itemId)}/{fileName}";

    /// <summary>Thrown by every write — a full disk, a locked folder.</summary>
    public Exception? ThrowOnWrite { get; set; }

    public Task<string> WriteAsync(Guid itemId, string fileName, byte[] content, CancellationToken cancellationToken = default)
    {
        if (ThrowOnWrite is { } thrown) throw thrown;

        var path = PathFor(itemId, fileName);
        Files[path] = content;
        Writes.Add(path);
        return Task.FromResult(path);
    }

    public Task<bool> HoldsAsync(string path, string sha256, CancellationToken cancellationToken = default) =>
        Task.FromResult(Files.TryGetValue(path, out var bytes)
            && string.Equals(Convert.ToHexStringLower(SHA256.HashData(bytes)), sha256, StringComparison.OrdinalIgnoreCase));

    public Task<byte[]?> ReadAsync(string path, CancellationToken cancellationToken = default) =>
        Task.FromResult(Files.GetValueOrDefault(path));

    public Result Open(string path)
    {
        OpenedPaths.Add(path);
        return Result.Success();
    }

    public List<Guid> RemovedFolders { get; } = [];

    public Task RemoveFolderAsync(Guid itemId, CancellationToken cancellationToken = default)
    {
        RemovedFolders.Add(itemId);
        foreach (var path in Files.Keys.Where(path => path.StartsWith(FolderFor(itemId) + "/", StringComparison.OrdinalIgnoreCase)).ToList())
        {
            Files.Remove(path);
        }

        return Task.CompletedTask;
    }
}
