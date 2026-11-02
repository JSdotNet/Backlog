namespace Backlog.Infrastructure.FileSystem;

/// <summary>
/// The pool writer the shell's own stores hand their file writes to, so that none is
/// written on the click path — <see cref="ShellNavigationStore"/> and
/// <see cref="ShellWindowStore"/>.
/// <para>
/// The JSON is taken by the caller, in call order, and the writer only ever writes the
/// newest snapshot it holds, so the last value set is the last one written however the
/// writes interleave. One writer per file, shared by every store on it, so two stores on
/// one path — a shell remounted, a test restarting — cannot land their writes out of
/// order, and a store constructed on a path can wait for the writes still queued for it
/// before it reads.
/// </para>
/// </summary>
internal static class QueuedFileWrites
{
    /// <summary>Guards <see cref="PendingWrites"/>. Writes are rare — one per press
    /// that changes something, or one per burst of window moves — so one gate for every
    /// path is enough.</summary>
    private static readonly Lock WriteGate = new();

    /// <summary>The file writes queued or running, by full path. An entry leaves
    /// when its writer has nothing left to write.</summary>
    private static readonly Dictionary<string, PendingWrite> PendingWrites = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>The newest snapshot not yet written, and the pool task writing.</summary>
    private sealed class PendingWrite
    {
        public string? Json { get; set; }

        public Task Writer { get; set; } = Task.CompletedTask;
    }

    static QueuedFileWrites()
    {
        // A value set in the last moment before the window closed is still in a
        // writer's hands; the process waits a little for it rather than dropping it.
        // The write itself is atomic, so a process that does not wait loses that
        // value and never the file.
        AppDomain.CurrentDomain.ProcessExit += (_, _) => WaitForAll(TimeSpan.FromSeconds(2));
    }

    /// <summary>Queues <paramref name="json"/> as the newest content of the file at
    /// <paramref name="path"/> and returns at once.</summary>
    public static void Enqueue(string path, string json)
    {
        var key = KeyOf(path);

        lock (WriteGate)
        {
            if (PendingWrites.TryGetValue(key, out var pending))
            {
                pending.Json = json;
                return;
            }

            pending = new PendingWrite { Json = json };
            PendingWrites[key] = pending;

            // Assigned under the gate the writer takes first, so it can never look
            // for its own entry before the entry knows it.
            pending.Writer = Task.Run(() => DrainWrites(key));
        }
    }

    /// <summary>
    /// Waits until every write queued for the file at <paramref name="path"/> — by any
    /// store on it in this process — has landed.
    /// </summary>
    public static void WaitFor(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        Task? writer;
        lock (WriteGate)
        {
            writer = PendingWrites.TryGetValue(KeyOf(path), out var pending) ? pending.Writer : null;
        }

        try
        {
            writer?.Wait();
        }
        catch (AggregateException)
        {
            // A writer that faulted has already retired its entry and lost only its
            // own write; waiting for it is over either way, and a reader goes on
            // to the file as it stands.
        }
    }

    private static string KeyOf(string path) => Path.GetFullPath(path);

    /// <summary>Writes the newest snapshot for <paramref name="key"/> until none is
    /// left, then retires the entry — under the gate, so a save arriving as it
    /// finishes either lands in this loop or starts a writer of its own.</summary>
    private static void DrainWrites(string key)
    {
        var retired = false;
        try
        {
            while (true)
            {
                string json;
                lock (WriteGate)
                {
                    var pending = PendingWrites[key];
                    if (pending.Json is null)
                    {
                        PendingWrites.Remove(key);
                        retired = true;
                        return;
                    }

                    json = pending.Json;
                    pending.Json = null;
                }

                try
                {
                    WriteFile(key, json);
                }
                catch (Exception ex) when (ex is not OutOfMemoryException)
                {
                    // One write lost, and only that one: the loop goes on to the
                    // next snapshot, so a failure nobody foresaw cannot leave the
                    // path with an entry and no writer behind it.
                }
            }
        }
        finally
        {
            // Out of the loop without retiring — something above the write
            // itself threw. The entry goes anyway, so the next save starts a
            // writer of its own instead of queueing behind a dead one.
            if (!retired)
            {
                lock (WriteGate)
                {
                    PendingWrites.Remove(key);
                }
            }
        }
    }

    private static void WaitForAll(TimeSpan timeout)
    {
        Task[] writers;
        lock (WriteGate)
        {
            writers = [.. PendingWrites.Values.Select(pending => pending.Writer)];
        }

        try
        {
            Task.WaitAll(writers, timeout);
        }
        catch (AggregateException)
        {
            // Every write already swallows its own IO failure; nothing to report
            // on the way out.
        }
    }

    /// <summary>Written beside the file and moved over it, so a process that ends
    /// mid-write leaves the previous file rather than half of a new one. The name
    /// beside it is this write's own: several app instances share one debug
    /// workspace and so this file, and two writers on one temporary name would fail
    /// each other's writes or move each other's half-written file.</summary>
    private static void WriteFile(string path, string json)
    {
        var temporary = $"{path}.{Guid.NewGuid():n}.tmp";
        try
        {
            File.WriteAllText(temporary, json);
            File.Move(temporary, path, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Losing this write only costs the next launch its restore; what
            // is on screen right now is unaffected. Its temporary file goes with
            // it rather than lingering beside the real one.
            TryDelete(temporary);
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Left behind; a later write never reads it.
        }
    }
}
