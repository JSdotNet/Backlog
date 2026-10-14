using System.Collections.Concurrent;

namespace Backlog.Infrastructure.Sessions;

/// <summary>
/// What a reader of growing JSON-lines files remembers between reads, and the rules it
/// remembers on: one copy of them for every reader that keeps such a memory.
/// <para>
/// <see cref="ClaudeSessionReader"/> folds a transcript into the facts the session list
/// shows; <see cref="LocalAgentActivitySource"/> folds the same transcripts, the agents
/// they spawned and Copilot's event streams into when an agent was producing. Both ask the
/// same of a file — answer it without opening it when nothing moved, read only what was
/// appended when it grew, and never remember a read the file can still prove wrong — and a
/// fix to one set of those rules that the other did not get is how two readers of the same
/// files start to disagree. So the rules live here. What a line means
/// (<see cref="ILineFold{TSelf}"/>), what the fold comes to, and the disk cache behind
/// the memory stay each reader's, and are handed in.
/// </para>
/// <para>
/// A read costs what it costs once rather than on every read, three ways, in order:
/// </para>
/// <list type="number">
/// <item><description><b>In memory.</b> A file read before, at the same length and write
/// time, is answered from what that read found — nothing opened, not even the disk
/// cache's entry. Only what a read of the settled file found: one taken while the write
/// time was within <see cref="Settling"/> of that read is checked again, however long
/// after it is asked for.</description></item>
/// <item><description><b>From where the last read stopped.</b> For the files written
/// within <see cref="ResumptionWindow"/>, the read keeps its fold and its
/// <see cref="TranscriptMark"/>, so the file being written to right now — the one whose
/// answer must not be stale, and which misses every other way — costs what was appended.
/// A file that is shorter than the mark, opens with other bytes or was created again is
/// read from the start (<see cref="TranscriptTail"/> decides).</description></item>
/// <item><description><b>On disk</b>, which is what survives a restart. Asked only when
/// the file is settled and there is no fold to resume — a file with one has moved since
/// it was read, and the disk holds nothing newer than the memory. Written only for a pass
/// over a settled file that read every line of it: one that stopped in front of half a
/// line would be remembered one line short.</description></item>
/// </list>
/// <para>
/// Safe to share between concurrent reads. Entries are immutable, a resumed read folds
/// into a copy of the state it resumes, and every change <see cref="Prune"/> makes
/// replaces only the entry it read — so a read that loses a race repeats work rather
/// than spoiling it. The memory ends with the process: forgetting the disk cache does not
/// forget it.
/// </para>
/// </summary>
/// <typeparam name="TFold">The reader's line fold, which is the state a resumed read goes
/// on from.</typeparam>
/// <typeparam name="TValue">What a read of the file answers, which is what is
/// remembered. A reference type, so "the disk has nothing" is a null.</typeparam>
internal sealed class ResumableFileMemory<TFold, TValue>
    where TFold : class, ILineFold<TFold>
    where TValue : class?
{
    /// <summary>
    /// How close to the read a write time may be and still be trusted on its own. A
    /// write time is only as fine as the file system keeps it, so a file written in the
    /// tick it is read can be written again in that tick at the same length — and match
    /// on both. What a read of a file written this recently found is never answered from
    /// memory, on that read or any later one, and is not written to the disk; the file is
    /// checked again instead, by the tail read, which costs its opening bytes when nothing
    /// moved. This is also when a settled file's unterminated last line is read as its
    /// last line rather than one still arriving.
    /// </summary>
    internal static readonly TimeSpan Settling = TimeSpan.FromSeconds(2);

    /// <summary>How recently a file must have been written for the memory to keep where
    /// its last read stopped. A day: the files that grow are the sessions running now and
    /// the ones resumed today, and the rest are read whole on the rare occasion they move.
    /// Decided when a read is kept, so a reading over a whole profile never holds a fold
    /// for every file it parsed.</summary>
    internal static readonly TimeSpan ResumptionWindow = TimeSpan.FromHours(24);

    /// <summary>How many files keep where their last read stopped, the most recently
    /// written first. What a resumed read needs is the whole fold, so it is kept for the
    /// files likely to grow and not for the hundreds that will not.</summary>
    internal const int ResumptionLimit = 32;

    private readonly Func<string, Stream> _open;
    private readonly Func<TFold, TValue> _answer;
    private readonly Func<TFold?, TValue> _interrupted;
    private readonly Func<string, long, DateTimeOffset, TValue?> _recall;
    private readonly Action<string, long, DateTimeOffset, TValue> _keep;

    /// <summary>What every file read was found to say, by path, and — for the few most
    /// recently written — where the read stopped and the fold it left.</summary>
    private readonly ConcurrentDictionary<string, Remembered> _remembered = new(StringComparer.OrdinalIgnoreCase);

    /// <param name="open">How a file is opened; a test's seam.</param>
    /// <param name="answer">What a completed fold comes to.</param>
    /// <param name="interrupted">What a read cut short by an I/O fault answers, given
    /// what it folded before the fault, or null when it failed before it folded
    /// anything. Never remembered.</param>
    /// <param name="recall">The disk's answer for a file at a length and write time, or
    /// null. A reader with no disk answers null.</param>
    /// <param name="keep">Writes a whole, settled pass's answer to the disk. A reader with
    /// no disk does nothing.</param>
    internal ResumableFileMemory(
        Func<string, Stream> open,
        Func<TFold, TValue> answer,
        Func<TFold?, TValue> interrupted,
        Func<string, long, DateTimeOffset, TValue?> recall,
        Action<string, long, DateTimeOffset, TValue> keep)
    {
        _open = open;
        _answer = answer;
        _interrupted = interrupted;
        _recall = recall;
        _keep = keep;
    }

    /// <summary>
    /// What <paramref name="file"/> says, from memory, the disk or a pass over it — the
    /// three ways the type's summary lists, in that order.
    /// <para>
    /// An <see cref="IOException"/> or <see cref="UnauthorizedAccessException"/> from the
    /// open or the pass is not let out: the file is being written as it is read, or is not
    /// ours to open, and that costs this file's answer and never the reader's. The read
    /// answers <c>interrupted</c> and keeps nothing, so the entry the last complete read
    /// left stays. Cancellation is let out.
    /// </para>
    /// </summary>
    /// <param name="file">The file as the walk found it: its length and write time are
    /// the walk's, and the pass stops at that length.</param>
    /// <param name="start">A new fold, for a file read from its start.</param>
    /// <param name="now">The instant the reading began, which settledness and the window
    /// are measured from.</param>
    internal async Task<TValue> ReadAsync(
        FileInfo file,
        Func<TFold> start,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var path = file.FullName;
        var writtenUtc = file.LastWriteTimeUtc;
        var length = file.Length;
        var writtenAt = new DateTimeOffset(writtenUtc, TimeSpan.Zero);
        var settled = writtenAt < now - Settling;

        _remembered.TryGetValue(path, out var known);

        // Settled when it was taken, not only now: an entry taken in the tick of a write
        // may predate a same-length rewrite in that tick, and the file settling since
        // does not make what that read found any truer.
        if (known is { Settled: true } && known.Length == length && known.WrittenUtc == writtenUtc)
        {
            return known.Value;
        }

        // The disk is asked only when there is no fold to resume: a file with one has
        // changed since it was read, and the disk holds nothing newer than memory.
        if (settled && known?.Resumption is null && _recall(path, length, writtenAt) is { } recalled)
        {
            _remembered[path] = new Remembered(length, writtenUtc, recalled, Settled: true, Resumption: null);

            return recalled;
        }

        var (value, resumption, whole) = await FoldAsync(file, start, known?.Resumption, settled, cancellationToken).ConfigureAwait(false);

        if (resumption is null) return value;

        // The fold is kept for a file likely to grow, and only for one: a reading over a
        // whole profile parses hundreds written weeks ago, and holding every one of their
        // folds until the read ends is the history this memory is not.
        var resumable = writtenAt >= now - ResumptionWindow;

        _remembered[path] = new Remembered(length, writtenUtc, value, settled, resumable ? resumption : null);

        if (whole && settled) _keep(path, length, writtenAt, value);

        return value;
    }

    /// <summary>
    /// Keeps the memory the size of what is on disk rather than the size of its history.
    /// A file <paramref name="walked"/> does not name is forgotten; a fold is kept only for
    /// the <see cref="ResumptionLimit"/> most recently written, and the rest keep their
    /// answer alone. Which files may keep one at all — those written inside
    /// <see cref="ResumptionWindow"/> — was decided when each read was kept. Each change
    /// replaces only the entry it read, so a read that raced this one and stored
    /// something newer keeps it.
    /// </summary>
    /// <param name="walked">The path of every file the reading's walk returned.</param>
    internal void Prune(IEnumerable<string> walked)
    {
        var paths = walked.ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var entry in _remembered)
        {
            if (!paths.Contains(entry.Key)) _remembered.TryRemove(entry);
        }

        foreach (var entry in _remembered
            .Where(entry => entry.Value.Resumption is not null)
            .OrderByDescending(entry => entry.Value.WrittenUtc)
            .ThenBy(entry => entry.Key, StringComparer.OrdinalIgnoreCase)
            .Skip(ResumptionLimit)
            .ToList())
        {
            _remembered.TryUpdate(entry.Key, entry.Value with { Resumption = null }, entry.Value);
        }
    }

    /// <summary>
    /// The pass itself, apart from the remembering: from the mark when it still holds,
    /// into a copy of the fold it left, or from the start into a new one.
    /// <para>
    /// <c>Resumption</c> is null when the read was cut short, which is the one outcome
    /// that must not be kept as if it were a fact about the file — and since the lines
    /// were folded into a copy, the fold the last complete read left is untouched by it.
    /// <c>Whole</c> is whether the pass read every line up to the length the walk saw.
    /// </para>
    /// </summary>
    private async Task<(TValue Value, Resumption? Resumption, bool Whole)> FoldAsync(
        FileInfo file,
        Func<TFold> start,
        Resumption? known,
        bool settled,
        CancellationToken cancellationToken)
    {
        TFold? fold = null;

        try
        {
            using var tail = await TranscriptTail.OpenAsync(_open, file, known?.Mark, cancellationToken).ConfigureAwait(false);

            fold = tail.Resumes ? known!.Fold.Clone() : start();

            var pass = await tail.ReadAsync(fold.Observe, settled, cancellationToken).ConfigureAwait(false);

            return (_answer(fold), new Resumption(pass.Mark, fold), pass.Whole);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            return (_interrupted(fold), null, false);
        }
    }

    /// <summary>What one file was found to say at one length and write time, and —
    /// while it is likely to grow — how to go on reading it.</summary>
    /// <param name="Settled">Whether the file was settled when this was taken — its
    /// write time more than <see cref="Settling"/> behind the read. Only a settled entry
    /// is answered from memory; any other is checked again.</param>
    private sealed record Remembered(long Length, DateTime WrittenUtc, TValue Value, bool Settled, Resumption? Resumption);

    /// <summary>Where a read stopped and the fold it left, which is what the next read
    /// resumes from. Never changed once kept: the next read folds into a copy.</summary>
    private sealed record Resumption(TranscriptMark Mark, TFold Fold);
}
