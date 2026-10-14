using Backlog.Infrastructure.Sessions;

namespace Backlog.Infrastructure.Sessions.UnitTests;

/// <summary>
/// The memory both transcript readers keep, tested on its own for the rules only one of
/// them pins.
/// <para>
/// Every other rule — the settled-only answer, the same-tick re-check, the day's window,
/// the cap, a failed read keeping nothing, the disk written only for a whole and settled
/// pass — is held through both readers, by <c>TranscriptFactsMemoryTests</c> and
/// <c>ActivityEventsMemoryTests</c>, and is not restated here. What is left is what a
/// second reader built on this type would otherwise only inherit by luck.
/// </para>
/// </summary>
public sealed class ResumableFileMemoryTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 10, 13, 12, 0, 0, TimeSpan.Zero);
    private static readonly DateTime AnHourAgo = Now.UtcDateTime.AddHours(-1);

    private readonly string _folder = Path.Combine(
        Path.GetTempPath(),
        "backlog-resumable-file-memory-tests",
        Guid.NewGuid().ToString("n"));

    private readonly Dictionary<(string Path, long Length, DateTimeOffset WrittenAt), Lines> _disk = [];
    private int _opens;
    private int _recalls;
    private int _kept;

    public ResumableFileMemoryTests() => Directory.CreateDirectory(_folder);

    /// <summary>A file the walk stopped returning is dropped, so one written again at the
    /// same length and write time is read rather than answered with what the old one
    /// held. No disk, because the disk keys on that stamp and is trusted on it by
    /// design.</summary>
    [Fact]
    public async Task A_file_the_walk_no_longer_returns_is_forgotten()
    {
        var memory = Memory(disk: false);
        var path = Given("gone.jsonl", "{\"a\":1}\n", AnHourAgo);

        await ReadAsync(memory, path);
        memory.Prune([]);
        Rewrite(path, "{\"b\":2}\n", AnHourAgo);

        var lines = await ReadAsync(memory, path);

        Assert.Equal(["{\"b\":2}"], lines.All);
        Assert.Equal(2, _opens);
    }

    /// <summary>The walk returned it, so it is kept: the next read opens nothing.</summary>
    [Fact]
    public async Task A_file_the_walk_still_returns_is_kept()
    {
        var memory = Memory(disk: false);
        var path = Given("kept.jsonl", "{\"a\":1}\n", AnHourAgo);

        await ReadAsync(memory, path);
        memory.Prune([path]);

        var lines = await ReadAsync(memory, path);

        Assert.Equal(["{\"a\":1}"], lines.All);
        Assert.Equal(1, _opens);
    }

    /// <summary>A file with a read to resume has moved since it was read, and the disk
    /// holds nothing newer than the memory does — so it is not asked.</summary>
    [Fact]
    public async Task The_disk_is_not_asked_for_a_file_whose_read_can_be_resumed()
    {
        var memory = Memory(disk: true);
        var path = Given("growing.jsonl", "{\"a\":1}\n", AnHourAgo);

        await ReadAsync(memory, path);
        Append(path, "{\"b\":2}\n", AnHourAgo.AddMinutes(1));

        var lines = await ReadAsync(memory, path);

        Assert.Equal(["{\"a\":1}", "{\"b\":2}"], lines.All);
        Assert.Equal(1, _recalls);
        Assert.Equal(2, _kept);
    }

    private ResumableFileMemory<LineList, Lines> Memory(bool disk) =>
        new(
            path =>
            {
                Interlocked.Increment(ref _opens);

                return TranscriptTail.OpenShared(path);
            },
            fold => new Lines([.. fold.All]),
            _ => new Lines([]),
            (path, length, writtenAt) =>
            {
                Interlocked.Increment(ref _recalls);

                return disk ? _disk.GetValueOrDefault((path, length, writtenAt)) : null;
            },
            (path, length, writtenAt, lines) =>
            {
                Interlocked.Increment(ref _kept);

                if (disk) _disk[(path, length, writtenAt)] = lines;
            });

    private static Task<Lines> ReadAsync(ResumableFileMemory<LineList, Lines> memory, string path) =>
        memory.ReadAsync(new FileInfo(path), () => new LineList(), Now, TestContext.Current.CancellationToken);

    private string Given(string name, string content, DateTime writtenUtc)
    {
        var path = Path.Combine(_folder, name);
        File.WriteAllText(path, content);
        File.SetLastWriteTimeUtc(path, writtenUtc);

        return path;
    }

    /// <summary>The content replaced in place, keeping the creation time and putting the
    /// write time back, so nothing a stat sees has changed.</summary>
    private static void Rewrite(string path, string content, DateTime writtenUtc)
    {
        var created = new FileInfo(path).CreationTimeUtc;
        File.WriteAllText(path, content);
        File.SetCreationTimeUtc(path, created);
        File.SetLastWriteTimeUtc(path, writtenUtc);
    }

    private static void Append(string path, string content, DateTime writtenUtc)
    {
        File.AppendAllText(path, content);
        File.SetLastWriteTimeUtc(path, writtenUtc);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_folder)) Directory.Delete(_folder, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    /// <summary>What a read of the file answers: every line it holds.</summary>
    private sealed record Lines(IReadOnlyList<string> All);

    /// <summary>The simplest fold there is, every line kept in order.</summary>
    private sealed class LineList : ILineFold<LineList>
    {
        private readonly List<string> _lines;

        public LineList() => _lines = [];

        private LineList(LineList other) => _lines = [.. other._lines];

        public IReadOnlyList<string> All => _lines;

        public void Observe(string line) => _lines.Add(line);

        public LineList Clone() => new(this);
    }
}
