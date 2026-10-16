using System.Buffers;
using System.Text;
using System.Text.Json;

namespace Backlog.Infrastructure.Sessions;

/// <summary>
/// Where the last complete read of a transcript stopped, and enough about the file it
/// was taken on to tell whether it is still that file.
/// <para>
/// Treated as immutable once a read has returned it: a reader keeps it beside the state
/// that read folded, and hands it back to resume.
/// </para>
/// </summary>
/// <param name="Offset">The byte just past the last newline read. Bytes, not
/// characters: a character count drifts on the first line holding anything outside
/// ASCII.</param>
/// <param name="CreatedUtc">The file's creation time when the mark was taken. A file
/// deleted and written again under the same name is a different file.</param>
/// <param name="Prefix">The file's first bytes, up to
/// <see cref="TranscriptTail.PrefixBytes"/> and never past <paramref name="Offset"/>. A
/// file rewritten in place keeps its name and its creation time; its opening bytes are
/// what changes.</param>
/// <param name="Unterminated">Whether the last line read had no newline: the last line
/// of a settled file, taken as one because it was whole JSON. A newline the writer
/// appends to it later closes that line rather than being an empty one.</param>
internal sealed record TranscriptMark(long Offset, DateTime CreatedUtc, byte[] Prefix, bool Unterminated);

/// <summary>What one read of a transcript reached.</summary>
/// <param name="Mark">Where the next read resumes.</param>
/// <param name="Whole">Whether every byte up to the length the walk saw was read as
/// a whole line: no unterminated last line still to come, and no file that came up
/// shorter than it was walked as. Only a whole pass is a fact about the file at that length, which is
/// what a disk cache keyed on the length may remember.</param>
internal readonly record struct TranscriptPass(TranscriptMark Mark, bool Whole);

/// <summary>
/// Reads the whole lines a transcript holds from where the last read stopped, so a
/// transcript that is being written to costs what was appended rather than its whole
/// length on every read.
/// <para>
/// Shared by the two passes made over the same files — the session list's facts and
/// the activity reader's events — because what a line is, where the next read starts
/// and when to start over are the same question for both, and the answer has three
/// edge cases each of them would otherwise have to get right alone. What a line
/// <em>means</em> stays with each reader: this hands over text, one line at a time,
/// and remembers nothing.
/// </para>
/// <para>
/// Two steps rather than one call, because the reader has to know whether the read
/// resumes before the first line arrives: a resumed read is folded into a copy of the
/// state the last one left, a fresh one into new state. <see cref="OpenAsync"/>
/// decides; <see cref="ReadAsync"/> reads.
/// </para>
/// <para>
/// The precedent is <c>DeliveryRunTelemetry.Fold</c>: start over when the file is
/// shorter than the mark, read whole lines only, count in bytes. Two checks are added,
/// because a transcript's mark lives for the app's lifetime rather than for one hook
/// event: the creation time, and a fingerprint of the opening bytes.
/// </para>
/// </summary>
internal sealed class TranscriptTail : IDisposable
{
    /// <summary>How much of a file's opening the mark fingerprints. A rewritten
    /// transcript starts with a different first line — a different session id, a
    /// different queued prompt — well inside this; and reading this much again on every
    /// resume is the price of noticing.</summary>
    internal const int PrefixBytes = 1024;

    /// <summary>One read's worth. A line longer than this is carried across reads, so
    /// it bounds the I/O and not the line.</summary>
    private const int ChunkBytes = 64 * 1024;

    private static ReadOnlySpan<byte> ByteOrderMark => [0xEF, 0xBB, 0xBF];

    private readonly Stream _stream;
    private readonly long _length;
    private readonly DateTime _createdUtc;
    private readonly long _start;
    private readonly byte[] _prefix;
    private readonly bool _unterminated;

    private TranscriptTail(Stream stream, long length, DateTime createdUtc, long start, byte[] prefix, bool unterminated)
    {
        _stream = stream;
        _length = length;
        _createdUtc = createdUtc;
        _start = start;
        _prefix = prefix;
        _unterminated = unterminated;
    }

    /// <summary>Whether this read continues from the mark it was handed. False for a
    /// first read, and for a mark the file no longer matches — read from the start,
    /// into new state.</summary>
    internal bool Resumes => _start > 0 || _prefix.Length > 0;

    /// <summary>
    /// The opener a host uses: shared for reading, writing and deletion, because the
    /// agent is appending to the file while it is read and must not be the one that
    /// fails. Unbuffered, because <see cref="ReadAsync"/> reads in chunks of its own.
    /// </summary>
    internal static Stream OpenShared(string path) =>
        new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, bufferSize: 1);

    /// <summary>
    /// Opens <paramref name="file"/> and decides where reading starts: at the mark when
    /// the file is at least as long as the mark, was created at the same instant and
    /// opens with the same bytes; at the start otherwise.
    /// </summary>
    /// <param name="open">The opener; <see cref="OpenShared"/> outside a test.</param>
    /// <param name="file">The file as the directory walk saw it. Its length is where
    /// this read stops, so what a caller remembers against that length is what that
    /// length held.</param>
    /// <param name="mark">Where the last read stopped, or null for a first read.</param>
    /// <exception cref="IOException">The file could not be opened or read.</exception>
    /// <exception cref="UnauthorizedAccessException">The file is not ours to open.</exception>
    internal static async Task<TranscriptTail> OpenAsync(
        Func<string, Stream> open,
        FileInfo file,
        TranscriptMark? mark,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(open);
        ArgumentNullException.ThrowIfNull(file);

        var length = file.Length;
        var created = file.CreationTimeUtc;
        var stream = open(file.FullName);

        try
        {
            var holds = mark is not null
                && mark.Offset <= length
                && mark.CreatedUtc == created
                && await OpensWithAsync(stream, mark.Prefix, cancellationToken).ConfigureAwait(false);

            return holds
                ? new TranscriptTail(stream, length, created, mark!.Offset, mark.Prefix, mark.Unterminated)
                : new TranscriptTail(stream, length, created, 0, [], unterminated: false);
        }
        catch
        {
            await stream.DisposeAsync().ConfigureAwait(false);

            throw;
        }
    }

    /// <summary>
    /// Hands every whole line from the start position up to the walked length to
    /// <paramref name="line"/>, with its <c>\r\n</c> or <c>\n</c> removed and a leading
    /// byte order mark dropped from the file's first line. An unterminated last line is
    /// left for the next read, unless the file is settled and that line is whole JSON.
    /// <para>
    /// Throws what the stream throws, and the caller's state is the caller's to keep:
    /// fold into a copy, and keep the copy only when this returns.
    /// </para>
    /// </summary>
    /// <param name="line">Handed each line, in the order the file holds them.</param>
    /// <param name="settled">Whether the caller judges the file no longer being written:
    /// its write time far enough behind the read. Then an unterminated last line that is
    /// whole JSON is a line its writer never terminated rather than one still arriving,
    /// and is handed over. Both transcript formats are JSON lines, so a line cut off
    /// part way through is one that does not parse; whether it parses decides where the
    /// line ends, and what it means stays the caller's. A file still being written keeps
    /// the rule that a line ends at its newline: an object can be closed and still have
    /// its newline to come.</param>
    /// <param name="cancellationToken">Stops the read.</param>
    internal async Task<TranscriptPass> ReadAsync(Action<string> line, bool settled, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(line);

        var offset = _start;
        var position = _start;
        var prefix = new byte[PrefixBytes];
        _prefix.CopyTo(prefix, 0);

        // Resumed behind a last line that was read without its newline: the newline,
        // when it comes, closes that line, and what sits in front of it is no line.
        var unterminated = _unterminated;

        var pending = new ArrayBufferWriter<byte>();
        var buffer = ArrayPool<byte>.Shared.Rent(ChunkBytes);

        try
        {
            _stream.Seek(_start, SeekOrigin.Begin);

            while (position < _length)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var wanted = (int)Math.Min(buffer.Length, _length - position);
                var read = await _stream.ReadAsync(buffer.AsMemory(0, wanted), cancellationToken).ConfigureAwait(false);

                // Shorter than it was walked as: truncated mid-read. What was read
                // stands; the pass is not whole.
                if (read == 0) break;

                if (position < PrefixBytes)
                {
                    var into = (int)position;
                    buffer.AsSpan(0, Math.Min(read, PrefixBytes - into)).CopyTo(prefix.AsSpan(into));
                }

                var chunk = buffer.AsSpan(0, read);
                var from = 0;

                while (chunk[from..].IndexOf((byte)'\n') is var found and >= 0)
                {
                    var end = from + found;
                    ReadOnlySpan<byte> text;

                    if (pending.WrittenCount > 0)
                    {
                        pending.Write(chunk[from..end]);
                        text = pending.WrittenSpan;
                    }
                    else
                    {
                        text = chunk[from..end];
                    }

                    var bytes = text.Length + 1;

                    if (!(unterminated && Trim(text, atStart: false).IsEmpty)) Emit(text, atStart: offset == 0, line);

                    unterminated = false;
                    offset += bytes;
                    pending.ResetWrittenCount();
                    from = end + 1;
                }

                pending.Write(chunk[from..]);
                position += read;
            }

            // The end of a settled file, reached rather than cut short, behind a last
            // line with no newline: that line is taken when it parses.
            if (settled
                && position == _length
                && pending.WrittenCount > 0
                && IsWholeJson(Trim(pending.WrittenSpan, atStart: offset == 0)))
            {
                Emit(pending.WrittenSpan, atStart: offset == 0, line);

                offset += pending.WrittenCount;
                unterminated = true;
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }

        var mark = new TranscriptMark(offset, _createdUtc, prefix[..(int)Math.Min(offset, PrefixBytes)], unterminated);

        return new TranscriptPass(mark, Whole: offset == _length);
    }

    public void Dispose() => _stream.Dispose();

    private static void Emit(ReadOnlySpan<byte> text, bool atStart, Action<string> line) =>
        line(Encoding.UTF8.GetString(Trim(text, atStart)));

    /// <summary>The line without its <c>\r</c>, and without a byte order mark when it is
    /// the file's first.</summary>
    private static ReadOnlySpan<byte> Trim(ReadOnlySpan<byte> text, bool atStart)
    {
        if (atStart && text.StartsWith(ByteOrderMark)) text = text[ByteOrderMark.Length..];
        if (text.Length > 0 && text[^1] == (byte)'\r') text = text[..^1];

        return text;
    }

    /// <summary>Whether the bytes are one JSON value and nothing after it. A line cut off
    /// part way through is not: whatever object, array or string it opened is still
    /// open.</summary>
    private static bool IsWholeJson(ReadOnlySpan<byte> text)
    {
        var reader = new Utf8JsonReader(text);

        try
        {
            if (!reader.Read()) return false;

            reader.Skip();

            return !reader.Read();
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static async Task<bool> OpensWithAsync(Stream stream, byte[] prefix, CancellationToken cancellationToken)
    {
        if (prefix.Length == 0) return true;

        var opening = new byte[prefix.Length];

        stream.Seek(0, SeekOrigin.Begin);

        var read = await stream.ReadAtLeastAsync(opening, opening.Length, throwOnEndOfStream: false, cancellationToken).ConfigureAwait(false);

        return read == opening.Length && opening.AsSpan().SequenceEqual(prefix);
    }
}
