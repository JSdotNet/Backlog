using System.Runtime.ExceptionServices;

namespace Backlog.Mobile.UI.Recordings;

/// <summary>
/// One finished part of a recording, as <see cref="AdtsPartWriter"/> closed it.
/// </summary>
/// <param name="Number">1 for the first part, then 2, 3 and so on.</param>
/// <param name="SizeBytes">The bytes written to it, a cut-short last frame included.</param>
/// <param name="Frames">The whole frames in it.</param>
/// <param name="Duration">The audio those frames hold.</param>
public sealed record AdtsPart(int Number, long SizeBytes, int Frames, TimeSpan Duration);

/// <summary>
/// Splits an ADTS stream into part files no larger than a cap, cutting only between frames
/// (local ADR 0048). ADTS frames are self-delimiting — a 12-bit sync word and a 13-bit length
/// in each header — so a part that starts on a frame plays alone, and the parts played one
/// after the other are the whole recording, with nothing lost or repeated.
/// </summary>
/// <remarks>
/// <para>
/// Bytes arrive in whatever chunks the source reads — a pipe on Android — so a frame may be
/// split across writes; it is held until it is whole and then written to one part. A part
/// closes when the next frame would take it past the cap, and the next part starts with that
/// frame. Each write is flushed, so a process that is killed leaves every frame it had on disk.
/// </para>
/// <para>
/// Bytes that are not a frame — a stream cut mid-frame, or noise before the first sync word —
/// are still written, so the files are always the stream; they only do not count towards the
/// duration. Until it has found a frame, and again after bytes that were none, the writer takes
/// a header as a frame only when another header follows it, so a sync-shaped run in the audio
/// cannot swallow real frames.
/// </para>
/// <para>
/// A write that fails — a part that cannot be opened, a full disk — is thrown once, and the
/// writer is faulted from then on: later writes are ignored, and <see cref="Complete"/> closes
/// what it can without writing again and hands back the parts as far as they got. Every byte
/// counted in a part reached its file; none is written twice.
/// </para>
/// <para>
/// One thread writes. <see cref="Duration"/> may be read from any thread.
/// </para>
/// </remarks>
public sealed class AdtsPartWriter : IDisposable
{
    /// <summary>24 MiB: under the sync service's 25 MiB per-file cap.</summary>
    public const long DefaultPartCap = 24L * 1024 * 1024;

    private const int HeaderLength = 7;
    private const int CrcHeaderLength = 9;
    private const int SamplesPerBlock = 1024;

    /// <summary>The sampling-frequency index of an ADTS header, in Hz.</summary>
    private static readonly int[] SampleRates =
        [96000, 88200, 64000, 48000, 44100, 32000, 24000, 22050, 16000, 12000, 11025, 8000, 7350];

    private readonly Func<int, Stream> _openPart;
    private readonly long _partCap;
    private readonly List<AdtsPart> _closed = [];

    private byte[] _pending = new byte[16 * 1024];
    private int _pendingCount;

    private Stream? _current;
    private int _currentNumber;
    private long _currentBytes;
    private int _currentFrames;
    private double _currentSeconds;

    private double _closedSeconds;
    private long _durationTicks;

    /// <summary>Whether the last unit was a frame, so the next header needs no confirming.</summary>
    private bool _locked;
    private bool _faulted;
    private bool _completed;

    /// <param name="openPart">Opens part <c>n</c>'s file, 1-based; the writer disposes it when
    /// the part closes.</param>
    /// <param name="partCap">The most bytes one part may hold.</param>
    public AdtsPartWriter(Func<int, Stream> openPart, long partCap = DefaultPartCap)
    {
        ArgumentNullException.ThrowIfNull(openPart);
        ArgumentOutOfRangeException.ThrowIfLessThan(partCap, CrcHeaderLength);

        _openPart = openPart;
        _partCap = partCap;
    }

    /// <summary>The audio written so far, across every part. Safe to read from any thread.</summary>
    public TimeSpan Duration => TimeSpan.FromTicks(Volatile.Read(ref _durationTicks));

    /// <summary>Whether a write failed; from then on writes are ignored.</summary>
    public bool IsFaulted => _faulted;

    /// <summary>Takes the next bytes of the stream. Throws the first failure to write them;
    /// ignores bytes once faulted.</summary>
    public void Write(ReadOnlySpan<byte> chunk)
    {
        ObjectDisposedException.ThrowIf(_completed, this);
        if (_faulted || chunk.IsEmpty) return;

        Append(chunk);

        try
        {
            Drain(final: false);
            _current?.Flush();
        }
        catch
        {
            _faulted = true;
            throw;
        }
    }

    /// <summary>
    /// Ends the stream: whatever is left, a cut-short frame included, goes into the last part,
    /// which closes. Answers every part in order. Never throws for a part file that failed:
    /// that part is answered as far as it got. Safe to call twice.
    /// </summary>
    public IReadOnlyList<AdtsPart> Complete()
    {
        if (_completed) return [.. _closed];

        if (!_faulted)
        {
            try
            {
                Drain(final: true);
                if (_pendingCount > 0) WriteUnit(0, _pendingCount, frame: null);
                _pendingCount = 0;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                _faulted = true;
            }
        }

        ClosePart(tolerant: true);
        _completed = true;

        return [.. _closed];
    }

    public void Dispose() => Complete();

    private void Append(ReadOnlySpan<byte> chunk)
    {
        if (_pendingCount + chunk.Length > _pending.Length)
        {
            Array.Resize(ref _pending, Math.Max(_pending.Length * 2, _pendingCount + chunk.Length));
        }

        chunk.CopyTo(_pending.AsSpan(_pendingCount));
        _pendingCount += chunk.Length;
    }

    /// <summary>
    /// Writes every whole frame in the buffer, and any bytes that cannot start one. Each unit
    /// leaves the buffer once it is written, so a write that throws leaves only what did not
    /// reach a file. <paramref name="final"/> is the end of the stream, where a frame that ends
    /// exactly at the end of the buffer needs no header after it.
    /// </summary>
    private void Drain(bool final)
    {
        var offset = 0;

        try
        {
            while (_pendingCount - offset >= HeaderLength)
            {
                var span = _pending.AsSpan(offset, _pendingCount - offset);

                var header = ReadHeader(span);
                if (ReferenceEquals(header, Header.NeedMore)) break;

                if (header is Header.Frame(var length, var seconds))
                {
                    if (span.Length < length) break;

                    var confirmed = _locked
                        || (span.Length == length && final)
                        || (span.Length > length && StartsFrame(span[length..]) is { } next && next);

                    // Not yet known: wait for the bytes after it, unless the stream has ended.
                    if (!confirmed && span.Length - length < HeaderLength && !final) break;

                    if (confirmed)
                    {
                        WriteUnit(offset, length, seconds);
                        offset += length;
                        _locked = true;
                        continue;
                    }
                }

                // Not a frame here: everything up to the next byte that could be a sync word goes
                // as it is.
                var nextSync = span[1..].IndexOf((byte)0xFF);
                var skip = nextSync < 0 ? span.Length : nextSync + 1;
                WriteUnit(offset, skip, frame: null);
                offset += skip;
                _locked = false;
            }
        }
        finally
        {
            if (offset > 0)
            {
                _pending.AsSpan(offset, _pendingCount - offset).CopyTo(_pending);
                _pendingCount -= offset;
            }
        }
    }

    /// <summary>Writes one frame, or a run of bytes that is none, to the current part, closing
    /// it first when the bytes would take it past the cap. Counts it only once it is written.</summary>
    private void WriteUnit(int offset, int count, double? frame)
    {
        if (_current is not null && _currentBytes > 0 && _currentBytes + count > _partCap)
        {
            ClosePart(tolerant: false);
        }

        if (_current is null)
        {
            var stream = _openPart(_currentNumber + 1);
            _currentNumber++;
            _current = stream;
        }

        _current.Write(_pending, offset, count);
        _currentBytes += count;

        if (frame is { } seconds)
        {
            _currentFrames++;
            _currentSeconds += seconds;
            Volatile.Write(ref _durationTicks, TimeSpan.FromSeconds(_closedSeconds + _currentSeconds).Ticks);
        }
    }

    /// <summary>Closes the current part and records it, whether or not its file closes cleanly;
    /// a failure to close is thrown unless <paramref name="tolerant"/>.</summary>
    private void ClosePart(bool tolerant)
    {
        if (_current is not { } stream) return;

        _current = null;
        _closed.Add(new AdtsPart(_currentNumber, _currentBytes, _currentFrames, TimeSpan.FromSeconds(_currentSeconds)));
        _closedSeconds += _currentSeconds;
        _currentBytes = 0;
        _currentFrames = 0;
        _currentSeconds = 0;

        Exception? failure = null;

        try
        {
            stream.Flush();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ObjectDisposedException)
        {
            failure = ex;
        }

        try
        {
            stream.Dispose();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            failure ??= ex;
        }

        if (failure is not null && !tolerant) ExceptionDispatchInfo.Throw(failure);
    }

    /// <summary>Whether the bytes start a frame header; <c>null</c> when too few to tell.</summary>
    private static bool? StartsFrame(ReadOnlySpan<byte> bytes)
    {
        var header = ReadHeader(bytes);
        return header is Header.Frame ? true : ReferenceEquals(header, Header.NeedMore) ? null : false;
    }

    /// <summary>
    /// Reads an ADTS header: the 12-bit sync word, layer 0, the sampling frequency index, the
    /// 13-bit frame length — at least the header, which is 9 bytes with a CRC — and the count
    /// of raw data blocks, each holding 1024 samples.
    /// </summary>
    private static Header ReadHeader(ReadOnlySpan<byte> header)
    {
        if (header.Length < 2) return Header.NeedMore;
        if (header[0] != 0xFF || (header[1] & 0xF0) != 0xF0 || (header[1] & 0x06) != 0) return Header.None;

        var headerLength = (header[1] & 0x01) == 1 ? HeaderLength : CrcHeaderLength;
        if (header.Length < HeaderLength) return Header.NeedMore;

        var rateIndex = (header[2] >> 2) & 0x0F;
        var length = ((header[3] & 0x03) << 11) | (header[4] << 3) | (header[5] >> 5);

        if (rateIndex >= SampleRates.Length || length < headerLength) return Header.None;

        var blocks = (header[6] & 0x03) + 1;
        return new Header.Frame(length, (double)blocks * SamplesPerBlock / SampleRates[rateIndex]);
    }

    private abstract record Header
    {
        public static readonly Header None = new NotAFrame();
        public static readonly Header NeedMore = new TooShort();

        public sealed record Frame(int Length, double Seconds) : Header;

        private sealed record NotAFrame : Header;

        private sealed record TooShort : Header;
    }
}
