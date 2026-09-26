using System.Globalization;

namespace Backlog.Modules.Inbox.DomainModels;

/// <summary>
/// One file an inbox item arrived with, and whether this machine has it.
/// <para>
/// Two halves with two lifetimes. The first five members are what the capture
/// said — the id the phone minted, the name, the type, the size and the digest
/// (local ADR 0014 §The capture document) — and they never change once the item
/// has recorded them. The last three are this desktop's own bookkeeping about
/// the local copy, and change on every fetch: a file is downloaded, or it is not
/// and the last attempt's reason is kept.
/// </para>
/// <para>
/// A value object, so a change is a new instance the aggregate swaps in; the
/// aggregate is the only thing that does, through
/// <see cref="InboxItem.MarkAttachmentDownloaded"/> and
/// <see cref="InboxItem.MarkAttachmentFailed"/>.
/// </para>
/// </summary>
public sealed record InboxAttachment
{
    private InboxAttachment(
        Guid id,
        string name,
        string contentType,
        long sizeBytes,
        string sha256,
        string? localPath,
        DateTimeOffset? downloadedAt,
        string? lastError)
    {
        Id = id;
        Name = name;
        ContentType = contentType;
        SizeBytes = sizeBytes;
        Sha256 = sha256;
        LocalPath = localPath;
        DownloadedAt = downloadedAt;
        LastError = lastError;
    }

    public Guid Id { get; }

    /// <summary>The name the capture gave the file — what the row shows, and the
    /// basis of the name it is written under.</summary>
    public string Name { get; }

    public string ContentType { get; }

    public long SizeBytes { get; }

    /// <summary>Lowercase hex digest of the bytes, which a fetched file must
    /// match before it is kept.</summary>
    public string Sha256 { get; }

    /// <summary>Where the file is on this machine; null until it is.</summary>
    public string? LocalPath { get; }

    public DateTimeOffset? DownloadedAt { get; }

    /// <summary>Why the last fetch failed, or null when none has — including
    /// when none has been tried yet.</summary>
    public string? LastError { get; }

    /// <summary>On this machine: a path and the instant it was written, together.</summary>
    public bool IsDownloaded => LocalPath is not null && DownloadedAt is not null;

    /// <summary>Recorded and never fetched — the one state a replayed page may
    /// still fetch in; a failed file waits for a person's Retry.</summary>
    public bool IsWaiting => !IsDownloaded && LastError is null;

    /// <summary>A picture, by its declared type — the one question the pane and
    /// the kind detector ask of a file.</summary>
    public bool IsImage => ContentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// The file as a capture names it, waiting for its first fetch. Refuses what
    /// no fetch could ever honour: an empty id, a negative size, a digest that is
    /// not 64 hex characters. A blank name reads as the id, and a blank type as
    /// <c>application/octet-stream</c>, because a file with no name is still a
    /// file somebody sent.
    /// </summary>
    public static InboxAttachment Named(Guid id, string? name, string? contentType, long sizeBytes, string? sha256)
    {
        if (id == Guid.Empty) throw new ArgumentException("An attachment needs an id.", nameof(id));
        if (sizeBytes < 0) throw new ArgumentOutOfRangeException(nameof(sizeBytes), "An attachment's size cannot be negative.");

        var digest = (sha256 ?? string.Empty).Trim().ToLowerInvariant();
        if (digest.Length != 64 || !digest.All(char.IsAsciiHexDigit))
            throw new ArgumentException("An attachment's sha256 must be 64 hex characters.", nameof(sha256));

        return new(
            id,
            string.IsNullOrWhiteSpace(name) ? id.ToString("D", CultureInfo.InvariantCulture) : name.Trim(),
            string.IsNullOrWhiteSpace(contentType) ? "application/octet-stream" : contentType.Trim(),
            sizeBytes,
            digest,
            localPath: null,
            downloadedAt: null,
            lastError: null);
    }

    /// <summary>A stored row, as it was written. No rule, for the reason
    /// <see cref="InboxItem.LoadTags"/> gives none.</summary>
    public static InboxAttachment Load(
        Guid id,
        string name,
        string contentType,
        long sizeBytes,
        string sha256,
        string? localPath,
        DateTimeOffset? downloadedAt,
        string? lastError) =>
        new(id, name, contentType, sizeBytes, sha256, localPath, downloadedAt, lastError);

    internal InboxAttachment Downloaded(string localPath, DateTimeOffset at) =>
        new(Id, Name, ContentType, SizeBytes, Sha256, localPath, at, lastError: null);

    internal InboxAttachment Failed(string error) =>
        new(Id, Name, ContentType, SizeBytes, Sha256, localPath: null, downloadedAt: null, error);
}
