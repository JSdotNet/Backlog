using System.Security.Cryptography;

namespace Backlog.Modules.Sync.Features.StoreAttachment;

/// <summary>
/// Reads an upload through to the store, hashing and counting as it goes, and
/// stops it the moment it passes the cap.
/// <para>
/// The cap is checked here as well as by the host's request-body limit because
/// the two catch different liars. The host refuses a body whose
/// <c>Content-Length</c> is too large before a byte is read, and Kestrel cuts off
/// a declared body that runs long; this catches every other way of sending more
/// than the cap — a chunked body with no length at all, and any host that
/// enforces no limit, which includes the test server.
/// </para>
/// </summary>
internal sealed class DigestingStream(Stream inner, long maxBytes) : Stream
{
    private readonly IncrementalHash _hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);

    /// <summary>How many bytes have been read through so far.</summary>
    internal long BytesRead { get; private set; }

    /// <summary>The lowercase hex SHA-256 of everything read. Call once, after
    /// the stream has been read to its end.</summary>
    internal string Sha256Hex() => Convert.ToHexStringLower(_hash.GetHashAndReset());

    public override bool CanRead => true;

    public override bool CanSeek => false;

    public override bool CanWrite => false;

    public override long Length => throw new NotSupportedException();

    public override long Position
    {
        get => BytesRead;
        set => throw new NotSupportedException();
    }

    public override int Read(byte[] buffer, int offset, int count) =>
        Account(buffer.AsSpan(offset, inner.Read(buffer, offset, count)));

    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        var read = await inner.ReadAsync(buffer, cancellationToken);
        return Account(buffer.Span[..read]);
    }

    public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
        ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

    private int Account(ReadOnlySpan<byte> read)
    {
        BytesRead += read.Length;

        if (BytesRead > maxBytes) throw new AttachmentTooLargeException(maxBytes);

        _hash.AppendData(read);
        return read.Length;
    }

    public override void Flush() { }

    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

    public override void SetLength(long value) => throw new NotSupportedException();

    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    protected override void Dispose(bool disposing)
    {
        if (disposing) _hash.Dispose();
        base.Dispose(disposing);
    }
}

/// <summary>The upload went past the cap. Thrown out of the read so the store
/// stops pulling bytes, and caught by the handler, which answers it as a
/// refusal rather than a store failure.</summary>
internal sealed class AttachmentTooLargeException(long maxBytes)
    : Exception($"The upload passed the {maxBytes}-byte cap.")
{
    internal long MaxBytes { get; } = maxBytes;
}
