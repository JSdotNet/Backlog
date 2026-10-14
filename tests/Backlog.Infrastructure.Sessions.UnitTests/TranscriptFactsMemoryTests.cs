using Backlog.Infrastructure.Sessions;
using Microsoft.Extensions.Time.Testing;

namespace Backlog.Infrastructure.Sessions.UnitTests;

/// <summary>
/// What the Claude session reader remembers between two reads of the same profile: the
/// facts of every transcript it read, and where it stopped in the ones being written to.
/// <para>
/// Every test here reads through one source more than once, which is the only way the
/// memory shows; the rest of the reader's tests build a source per read and are not
/// about it. The yardstick is a fresh source over the same files: whatever a remembered
/// or resumed read answers, a reader that had never seen the file must answer too.
/// </para>
/// <para>
/// The transcripts are opened through a counting opener rather than the file system's
/// own, so a test can say how many times a file was opened and how many of its bytes
/// were read, and where.
/// </para>
/// </summary>
public sealed class TranscriptFactsMemoryTests : IDisposable
{
    private static readonly DateTimeOffset Noon = new(2026, 8, 19, 12, 0, 0, TimeSpan.Zero);

    private const string Slug = "D--Repos-Backlog";

    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        "backlog-transcript-memory-tests",
        Guid.NewGuid().ToString("n"));

    private string ClaudeHome => Path.Combine(_root, ".claude");

    private string CopilotHome => Path.Combine(_root, ".copilot");

    // --- Unchanged ---------------------------------------------------------------

    /// <summary>A second read of a transcript nothing has touched opens neither the
    /// transcript nor the disk cache: the reader already holds its facts.</summary>
    [Fact]
    public async Task A_transcript_nothing_touched_since_the_last_read_is_answered_without_opening_it_or_the_cache()
    {
        GivenTranscript("still", Noon.AddHours(-1), Header(), Prompt("one"));

        var opener = new CountingOpener();
        var facts = new CountingFacts();
        var source = Source(opener, facts);

        _ = await source.GetSessionsAsync(TestContext.Current.CancellationToken);
        var second = Assert.Single((await source.GetSessionsAsync(TestContext.Current.CancellationToken)).Sessions);

        Assert.Equal(1, opener.Opens);
        Assert.Equal(1, facts.Reads);
        Assert.Equal(2, second.TurnCount);
    }

    /// <summary>
    /// Proof that the remembered facts are what comes back rather than a quiet second
    /// read: the content is swapped at the same length and the write time put back, and
    /// the row still says what the first read found.
    /// </summary>
    [Fact]
    public async Task A_same_length_swap_behind_an_old_write_time_is_answered_from_memory()
    {
        GivenTranscript("swapped", Noon.AddHours(-1), Header(folder: "A"), Prompt("one"));
        var source = Source(new CountingOpener());

        _ = await source.GetSessionsAsync(TestContext.Current.CancellationToken);
        Swap("swapped", Header(folder: "B"), Prompt("one"));

        var session = Assert.Single((await source.GetSessionsAsync(TestContext.Current.CancellationToken)).Sessions);

        Assert.Equal(@"D:\Repos\A", session.WorkingFolder);
    }

    /// <summary>
    /// The same swap on a file written in the instant of the read is not trusted. A
    /// write time is only as fine as the file system keeps it, so a file rewritten in
    /// the same tick it was read in can match on length and time and still be another
    /// file; one written that recently is checked again rather than answered from
    /// memory.
    /// </summary>
    [Fact]
    public async Task A_transcript_written_in_the_instant_of_the_read_is_checked_again_rather_than_trusted()
    {
        GivenTranscript("racy", Noon, Header(folder: "A"), Prompt("one"));
        var source = Source(new CountingOpener());

        _ = await source.GetSessionsAsync(TestContext.Current.CancellationToken);
        Swap("racy", Header(folder: "B"), Prompt("one"));

        var session = Assert.Single((await source.GetSessionsAsync(TestContext.Current.CancellationToken)).Sessions);

        Assert.Equal(@"D:\Repos\B", session.WorkingFolder);
    }

    /// <summary>
    /// And not on any read after it either, once the file has settled. What a read in
    /// the instant of the write took may predate a rewrite in that same tick, and
    /// settling later does not make it a settled reading of the file: the first read
    /// after the file settles checks it again, finds the rewrite, and is the one that is
    /// remembered — and written to disk, which the instant's read never was.
    /// </summary>
    [Fact]
    public async Task What_a_read_in_the_instant_of_the_write_took_is_not_answered_from_memory_once_the_file_settles()
    {
        GivenTranscript("racy", Noon, Header(folder: "A"), Prompt("one"));
        var clock = new FakeTimeProvider(Noon);
        var facts = new CountingFacts();
        var source = Source(new CountingOpener(), facts, clock);

        _ = await source.GetSessionsAsync(TestContext.Current.CancellationToken);
        Swap("racy", Header(folder: "B"), Prompt("one"));
        clock.Advance(TimeSpan.FromSeconds(3));

        var session = await OnlyAsync(source);
        var file = new FileInfo(PathOf("racy"));

        Assert.Equal(@"D:\Repos\B", session.WorkingFolder);
        Assert.Equal(@"D:\Repos\B", facts.TryRead(file.FullName, file.Length, new DateTimeOffset(file.LastWriteTimeUtc, TimeSpan.Zero))?.Folder);
    }

    // --- Grown -------------------------------------------------------------------

    /// <summary>
    /// The saving itself. A transcript of tens of kilobytes grows by two lines, and the
    /// next read reads those two lines past where it stopped — plus the opening bytes it
    /// checks the file by — and answers what a reader that had never seen the file
    /// answers.
    /// </summary>
    [Fact]
    public async Task A_grown_transcript_is_read_from_where_the_last_read_stopped()
    {
        GivenTranscript("grown", Noon.AddHours(-1), [Header(), Prompt("one"), .. Chatter(300)]);
        var stopped = LengthOf("grown");

        var opener = new CountingOpener();
        var source = Source(opener);
        _ = await source.GetSessionsAsync(TestContext.Current.CancellationToken);

        opener.Reset();
        Append("grown", Noon.AddMinutes(-30), Prompt("two"), Usage("m1", input: 10, output: 5));
        var appended = LengthOf("grown") - stopped;

        var resumed = await OnlyAsync(source);

        Assert.Equal(appended, opener.BytesFrom(stopped));
        Assert.True(opener.BytesBefore(stopped) <= TranscriptTail.PrefixBytes);
        Assert.Equal(await FreshAsync(), Facts(resumed));
        Assert.Equal(3, resumed.TurnCount);
    }

    /// <summary>The header is first-wins and line-counted, so a resumed read carries the
    /// branch latched before the folder was found, and the count of lines the folder may
    /// still be found in.</summary>
    [Fact]
    public async Task The_header_found_across_the_boundary_is_the_one_a_whole_read_finds()
    {
        GivenTranscript("header", Noon.AddHours(-1),
            """{"type":"queue-operation","operation":"enqueue","content":"queued"}""",
            """{"type":"user","gitBranch":"first","message":{"role":"user","content":"before the folder"}}""");
        var source = Source(new CountingOpener());
        _ = await source.GetSessionsAsync(TestContext.Current.CancellationToken);

        Append("header", Noon.AddMinutes(-30), Header(folder: "Found", branch: "second"));

        var resumed = await OnlyAsync(source);

        Assert.Equal(await FreshAsync(), Facts(resumed));
        Assert.Equal(@"D:\Repos\Found", resumed.WorkingFolder);
    }

    [Fact]
    public async Task A_folder_stated_past_the_header_lines_is_not_taken_across_the_boundary_either()
    {
        GivenTranscript("late", Noon.AddHours(-1), [.. Chatter(40)]);
        var source = Source(new CountingOpener());
        _ = await source.GetSessionsAsync(TestContext.Current.CancellationToken);

        Append("late", Noon.AddMinutes(-30), Header(folder: "TooLate"));

        var resumed = await OnlyAsync(source);

        Assert.Equal(await FreshAsync(), Facts(resumed));
        Assert.Equal(string.Empty, resumed.WorkingFolder);
    }

    /// <summary>One message's lines repeat its usage and the last one counts; a pull
    /// request linked twice was linked when it was first linked; the entry point is the
    /// first one named. Each of those straddles the boundary here.</summary>
    [Fact]
    public async Task Work_that_straddles_the_boundary_is_counted_as_a_whole_read_counts_it()
    {
        GivenTranscript("work", Noon.AddHours(-1),
            Header(),
            """{"type":"system","entrypoint":"claude-desktop"}""",
            Usage("m1", input: 10, output: 1),
            PullRequest(1, "2026-08-19T09:00:00Z"));
        var source = Source(new CountingOpener());
        _ = await source.GetSessionsAsync(TestContext.Current.CancellationToken);

        Append("work", Noon.AddMinutes(-30),
            """{"type":"system","entrypoint":"cli"}""",
            Usage("m1", input: 10, output: 50),
            PullRequest(1, "2026-08-19T11:00:00Z"),
            PullRequest(2, "2026-08-19T11:30:00Z"),
            Usage("m2", input: 3, output: 4));

        var resumed = await OnlyAsync(source);

        Assert.Equal(await FreshAsync(), Facts(resumed));
        Assert.Equal("claude-desktop", resumed.Entrypoint);
        Assert.Equal(54, Assert.Single(resumed.ModelUsage!).OutputTokens);
        Assert.Equal(new DateTimeOffset(2026, 8, 19, 9, 0, 0, TimeSpan.Zero), resumed.PullRequests![0].LinkedAt);
    }

    [Fact]
    public async Task No_turns_on_either_side_of_the_boundary_is_still_no_turn_count()
    {
        GivenTranscript("silent", Noon.AddHours(-1), """{"type":"user","cwd":"D:\\Repos\\Backlog","gitBranch":"main"}""");
        var source = Source(new CountingOpener());
        _ = await source.GetSessionsAsync(TestContext.Current.CancellationToken);

        Append("silent", Noon.AddMinutes(-30), Chatter(2));

        var resumed = await OnlyAsync(source);

        Assert.Equal(await FreshAsync(), Facts(resumed));
        Assert.Null(resumed.TurnCount);
    }

    // --- Started over ------------------------------------------------------------

    [Fact]
    public async Task A_transcript_shorter_than_where_the_last_read_stopped_is_read_again_whole()
    {
        GivenTranscript("cut", Noon.AddHours(-1), Header(), Prompt("one"), Prompt("two"), Prompt("three"));
        var source = Source(new CountingOpener());
        _ = await source.GetSessionsAsync(TestContext.Current.CancellationToken);

        Rewrite("cut", Noon.AddMinutes(-30), Header(), Prompt("four"));

        var read = await OnlyAsync(source);

        Assert.Equal(await FreshAsync(), Facts(read));
        Assert.Equal(2, read.TurnCount);
    }

    [Fact]
    public async Task A_transcript_rewritten_with_other_opening_bytes_is_read_again_whole()
    {
        GivenTranscript("rewritten", Noon.AddHours(-1), Header(folder: "A"), Prompt("one"));
        var source = Source(new CountingOpener());
        _ = await source.GetSessionsAsync(TestContext.Current.CancellationToken);

        Rewrite("rewritten", Noon.AddMinutes(-30), Header(folder: "B"), Prompt("one"), Prompt("two"), Prompt("three"));

        var read = await OnlyAsync(source);

        Assert.Equal(await FreshAsync(), Facts(read));
        Assert.Equal(@"D:\Repos\B", read.WorkingFolder);
    }

    /// <summary>
    /// A file deleted and written again under the same name, opening with the same
    /// bytes. Only the creation time tells it apart, so the opening line is made longer
    /// than the fingerprint and the difference sits behind it: resumed, the read would
    /// keep the old file's two turns and add the new one's.
    /// </summary>
    [Fact]
    public async Task A_transcript_created_again_is_read_again_whole()
    {
        var opening = "{\"type\":\"queue-operation\",\"operation\":\"enqueue\",\"content\":\"" + new string('x', TranscriptTail.PrefixBytes * 2) + "\"}";

        GivenTranscript("again", Noon.AddHours(-1), opening, Header(), Prompt("one"), Prompt("two"));
        var source = Source(new CountingOpener());
        _ = await source.GetSessionsAsync(TestContext.Current.CancellationToken);

        Rewrite("again", Noon.AddMinutes(-30), [opening, Header(), .. Chatter(4), Prompt("three")]);
        File.SetCreationTimeUtc(PathOf("again"), Noon.AddMinutes(-30).UtcDateTime);

        var read = await OnlyAsync(source);

        Assert.Equal(await FreshAsync(), Facts(read));
        Assert.Equal(2, read.TurnCount);
    }

    /// <summary>
    /// A resumed read cut short by the file going away under it. The row says what a
    /// cut-short read has always said — no count — and the read after it, with the file
    /// readable again, answers what a whole read does: nothing the interrupted pass
    /// folded was kept.
    /// </summary>
    [Fact]
    public async Task A_resumed_read_that_fails_part_way_keeps_nothing_it_read()
    {
        GivenTranscript("failing", Noon.AddHours(-1), Header(), Prompt("one"));
        var stopped = LengthOf("failing");

        var opener = new CountingOpener();
        var source = Source(opener);
        _ = await source.GetSessionsAsync(TestContext.Current.CancellationToken);

        // More than one read's worth, failing past the first, so the pass has folded
        // hundreds of turns by the time it fails.
        Append("failing", Noon.AddMinutes(-30), Enumerable.Range(0, 1100).Select(index => Prompt($"p{index}")));

        opener.FailPast = stopped + 70_000;
        var failed = await OnlyAsync(source);

        opener.FailPast = null;
        var read = await OnlyAsync(source);

        Assert.Null(failed.TurnCount);
        Assert.Equal(await FreshAsync(), Facts(read));
        Assert.Equal(1102, read.TurnCount);
    }

    // --- The disk cache --------------------------------------------------------

    /// <summary>A resumed read that reached the end is a fact about the file at its new
    /// length, and is written through for the next launch.</summary>
    [Fact]
    public async Task A_grown_transcript_read_to_its_end_is_remembered_on_disk_at_its_new_length()
    {
        GivenTranscript("through", Noon.AddHours(-1), Header(), Prompt("one"));
        var facts = new CountingFacts();
        var source = Source(new CountingOpener(), facts);
        _ = await source.GetSessionsAsync(TestContext.Current.CancellationToken);

        Append("through", Noon.AddMinutes(-30), Prompt("two"));
        _ = await OnlyAsync(source);

        var file = new FileInfo(PathOf("through"));
        var remembered = facts.TryRead(file.FullName, file.Length, new DateTimeOffset(file.LastWriteTimeUtc, TimeSpan.Zero));

        // The header line is a prompt too: three turns.
        Assert.Equal(3, remembered?.Turns);
    }

    /// <summary>A transcript that stops in the middle of a line was read short of its
    /// length. That is never written down against the length, and the half line is
    /// still there to be read when the rest of it arrives.</summary>
    [Fact]
    public async Task A_transcript_ending_in_half_a_line_is_not_remembered_on_disk_and_reads_the_line_once_it_is_whole()
    {
        GivenTranscript("open", Noon.AddHours(-1), Header(), Prompt("one"));
        var facts = new CountingFacts();
        var source = Source(new CountingOpener(), facts);
        _ = await source.GetSessionsAsync(TestContext.Current.CancellationToken);

        var line = Prompt("two");
        File.AppendAllText(PathOf("open"), line[..20]);
        File.SetLastWriteTimeUtc(PathOf("open"), Noon.AddMinutes(-30).UtcDateTime);
        var cut = await OnlyAsync(source);

        var file = new FileInfo(PathOf("open"));
        Assert.Null(facts.TryRead(file.FullName, file.Length, new DateTimeOffset(file.LastWriteTimeUtc, TimeSpan.Zero)));

        Append("open", Noon.AddMinutes(-20), line[20..], Prompt("three"));
        var read = await OnlyAsync(source);

        Assert.Equal(2, cut.TurnCount);
        Assert.Equal(await FreshAsync(), Facts(read));
        Assert.Equal(4, read.TurnCount);
    }

    /// <summary>
    /// A finished transcript whose writer never terminated its last line. Once the file
    /// has settled, a last line that is whole JSON is the file's last line and not one
    /// still arriving, so it is counted — the way a reader that stopped at the end of
    /// the file always counted it — and the read is a fact about the file at its length,
    /// written to disk like any other.
    /// </summary>
    [Fact]
    public async Task A_finished_transcript_whose_last_line_has_no_newline_counts_it_and_is_remembered_on_disk()
    {
        GivenTranscript("open", Noon.AddHours(-1), Header(), Prompt("one"));
        var facts = new CountingFacts();
        var source = Source(new CountingOpener(), facts);
        _ = await source.GetSessionsAsync(TestContext.Current.CancellationToken);

        File.AppendAllText(PathOf("open"), Prompt("two"));
        File.SetLastWriteTimeUtc(PathOf("open"), Noon.AddMinutes(-30).UtcDateTime);
        var read = await OnlyAsync(source);

        var file = new FileInfo(PathOf("open"));

        Assert.Equal(3, read.TurnCount);
        Assert.Equal(await FreshAsync(), Facts(read));
        Assert.Equal(3, facts.TryRead(file.FullName, file.Length, new DateTimeOffset(file.LastWriteTimeUtc, TimeSpan.Zero))?.Turns);
    }

    /// <summary>
    /// The writer that left the last line open closes it later, and goes on. The newline
    /// closes the line already read rather than being an empty line of its own — which
    /// shows on the header, the one fact counted in lines: here the folder is stated on
    /// the last line it may be found on, and an extra line would have pushed it past.
    /// </summary>
    [Fact]
    public async Task A_last_line_read_without_its_newline_is_not_read_again_as_a_line_when_the_newline_arrives()
    {
        GivenTranscript("open", Noon.AddHours(-1), [.. Chatter(38)]);
        File.AppendAllText(PathOf("open"), Prompt("two"));
        File.SetLastWriteTimeUtc(PathOf("open"), Noon.AddHours(-1).UtcDateTime);

        var source = Source(new CountingOpener());
        _ = await source.GetSessionsAsync(TestContext.Current.CancellationToken);

        File.AppendAllText(PathOf("open"), Environment.NewLine);
        Append("open", Noon.AddMinutes(-30), Header(folder: "Found"));
        var read = await OnlyAsync(source);

        Assert.Equal(await FreshAsync(), Facts(read));
        Assert.Equal(@"D:\Repos\Found", read.WorkingFolder);
        Assert.Equal(2, read.TurnCount);
    }

    // --- What is kept ----------------------------------------------------------

    /// <summary>Where a read stopped is kept only for the transcripts written in the
    /// last day, decided when the read is kept rather than trimmed afterwards: one older
    /// than that keeps no fold from the read that found it old, is answered from memory
    /// while it stands still, and is read whole when it moves.</summary>
    [Fact]
    public async Task A_transcript_last_written_over_a_day_ago_keeps_no_fold_and_is_read_whole_when_it_grows()
    {
        GivenTranscript("old", Noon.AddHours(-30), [Header(), Prompt("one"), .. Chatter(20)]);
        var opener = new CountingOpener();
        var source = Source(opener);
        _ = await source.GetSessionsAsync(TestContext.Current.CancellationToken);

        var stopped = LengthOf("old");
        Append("old", Noon.AddHours(-26), Prompt("two"));
        opener.Reset();

        var read = await OnlyAsync(source);

        Assert.Equal(stopped, opener.BytesBefore(stopped));
        Assert.Equal(await FreshAsync(), Facts(read));
    }

    /// <summary>And for no more than 32 of them, the most recently written: the 33rd is
    /// read whole when it moves, the 32nd from where it stopped.</summary>
    [Fact]
    public async Task Only_the_32_most_recently_written_transcripts_keep_where_their_read_stopped()
    {
        for (var index = 0; index < 33; index++)
        {
            GivenTranscript($"t{index:00}", Noon.AddHours(-1).AddMinutes(index), [Header(), Prompt("one"), .. Chatter(20)]);
        }

        var opener = new CountingOpener();
        var source = Source(opener);
        _ = await source.GetSessionsAsync(TestContext.Current.CancellationToken);

        var stopped = LengthOf("t00");
        Append("t00", Noon.AddMinutes(-20), Prompt("two"));
        Append("t01", Noon.AddMinutes(-19), Prompt("two"));
        opener.Reset();

        _ = await source.GetSessionsAsync(TestContext.Current.CancellationToken);

        Assert.Equal(stopped, opener.BytesBefore(stopped, PathOf("t00")));
        Assert.True(opener.BytesBefore(stopped, PathOf("t01")) < stopped);
    }

    // --- Helpers -----------------------------------------------------------------

    private LocalAgentSessionSource Source(CountingOpener opener, ITranscriptFactsCache? facts = null, TimeProvider? clock = null) =>
        new(ClaudeHome, CopilotHome, "machine", "DEV-TOWER", clock ?? new FakeTimeProvider(Noon), facts, repositories: null, opener.Open);

    private static async Task<AgentSession> OnlyAsync(LocalAgentSessionSource source) =>
        Assert.Single((await source.GetSessionsAsync(TestContext.Current.CancellationToken)).Sessions);

    /// <summary>What a reader that had never seen the profile answers.</summary>
    private async Task<string> FreshAsync() =>
        Facts(await OnlyAsync(Source(new CountingOpener())));

    /// <summary>Everything a transcript contributes to a row, as one comparable
    /// string.</summary>
    private static string Facts(AgentSession session) => string.Join(
        " | ",
        session.Title,
        session.WorkingFolder,
        session.Branch,
        session.TurnCount,
        session.Entrypoint,
        session.PullRequests is null ? "null" : string.Join(", ", session.PullRequests),
        session.ModelUsage is null ? "null" : string.Join(", ", session.ModelUsage));

    private static string Header(string folder = "Backlog", string branch = "main") =>
        $$"""{"type":"user","cwd":"D:\\Repos\\{{folder}}","gitBranch":"{{branch}}","message":{"role":"user","content":"the first prompt""" + "\"}}";

    private static string Prompt(string text) =>
        $$"""{"type":"user","isSidechain":false,"message":{"role":"user","content":"{{text}}""" + "\"}}";

    private static string Usage(string id, long input, long output) =>
        $$"""{"type":"assistant","message":{"id":"{{id}}","model":"claude-opus-4","role":"assistant","usage":{"input_tokens":{{input}},"output_tokens":{{output}},"cache_creation_input_tokens":0,"cache_read_input_tokens":0""" + "}}}";

    private static string PullRequest(int number, string at) =>
        $$"""{"type":"pr-link","prUrl":"https://github.com/JSdotNet/Backlog/pull/{{number}}","prRepository":"JSdotNet/Backlog","prNumber":{{number}},"timestamp":"{{at}}"}""";

    /// <summary>Lines that are none of the facts, to give a transcript some
    /// length.</summary>
    private static IEnumerable<string> Chatter(int count) =>
        Enumerable.Range(0, count).Select(index =>
            $$"""{"type":"assistant","message":{"role":"assistant","content":[{"type":"text","text":"line {{index}} of an answer that goes on for a while"}]""" + "}}");

    private string PathOf(string id) => Path.Combine(ClaudeHome, "projects", Slug, $"{id}.jsonl");

    private long LengthOf(string id) => new FileInfo(PathOf(id)).Length;

    private void GivenTranscript(string id, DateTimeOffset lastWrite, params IEnumerable<string> lines)
    {
        Directory.CreateDirectory(Path.Combine(ClaudeHome, "projects", Slug));
        File.WriteAllLines(PathOf(id), lines);
        File.SetLastWriteTimeUtc(PathOf(id), lastWrite.UtcDateTime);
    }

    private void Append(string id, DateTimeOffset lastWrite, params IEnumerable<string> lines)
    {
        File.AppendAllLines(PathOf(id), lines);
        File.SetLastWriteTimeUtc(PathOf(id), lastWrite.UtcDateTime);
    }

    /// <summary>New content in place, the creation time kept.</summary>
    private void Rewrite(string id, DateTimeOffset lastWrite, params IEnumerable<string> lines)
    {
        var created = new FileInfo(PathOf(id)).CreationTimeUtc;
        File.WriteAllLines(PathOf(id), lines);
        File.SetCreationTimeUtc(PathOf(id), created);
        File.SetLastWriteTimeUtc(PathOf(id), lastWrite.UtcDateTime);
    }

    /// <summary>Other content of the same length, with the write time and creation time
    /// put back: a change neither the length nor the time can see.</summary>
    private void Swap(string id, params IEnumerable<string> lines)
    {
        var before = new FileInfo(PathOf(id));
        var (length, written, created) = (before.Length, before.LastWriteTimeUtc, before.CreationTimeUtc);

        File.WriteAllLines(PathOf(id), lines);
        File.SetCreationTimeUtc(PathOf(id), created);
        File.SetLastWriteTimeUtc(PathOf(id), written);

        Assert.Equal(length, new FileInfo(PathOf(id)).Length);
    }

    /// <summary>The transcript opener, counting: how many opens, and which bytes each
    /// read covered. It can be told to fail a read that reaches past an offset, which is
    /// the agent truncating or locking the file under a read.</summary>
    private sealed class CountingOpener
    {
        private readonly List<(string Path, long Position, int Count)> _reads = [];

        public int Opens { get; private set; }

        public long? FailPast { get; set; }

        public Stream Open(string path)
        {
            Opens++;

            return new CountingStream(TranscriptTail.OpenShared(path), path, this);
        }

        public void Reset()
        {
            Opens = 0;
            _reads.Clear();
        }

        /// <summary>The bytes read at or after <paramref name="offset"/>.</summary>
        public long BytesFrom(long offset) =>
            _reads.Sum(read => Math.Max(0, read.Position + read.Count - Math.Max(read.Position, offset)));

        /// <summary>The bytes read before <paramref name="offset"/>, of one file or of
        /// all of them.</summary>
        public long BytesBefore(long offset, string? path = null) =>
            _reads
                .Where(read => path is null || string.Equals(read.Path, path, StringComparison.OrdinalIgnoreCase))
                .Sum(read => Math.Max(0, Math.Min(read.Position + read.Count, offset) - read.Position));

        private void Record(string path, long position, int count)
        {
            if (FailPast is { } limit && position + count > limit) throw new IOException("The file went away mid-read.");

            _reads.Add((path, position, count));
        }

        private sealed class CountingStream(Stream inner, string path, CountingOpener owner) : Stream
        {
            public override bool CanRead => true;

            public override bool CanSeek => true;

            public override bool CanWrite => false;

            public override long Length => inner.Length;

            public override long Position
            {
                get => inner.Position;
                set => inner.Position = value;
            }

            public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));

            public override int Read(Span<byte> buffer)
            {
                var position = inner.Position;
                var read = inner.Read(buffer);
                owner.Record(path, position, read);

                return read;
            }

            public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
            {
                var position = inner.Position;
                var read = await inner.ReadAsync(buffer, cancellationToken);
                owner.Record(path, position, read);

                return read;
            }

            public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
                ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

            public override long Seek(long offset, SeekOrigin origin) => inner.Seek(offset, origin);

            public override void Flush()
            {
            }

            public override void SetLength(long value) => throw new NotSupportedException();

            public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

            protected override void Dispose(bool disposing)
            {
                if (disposing) inner.Dispose();

                base.Dispose(disposing);
            }
        }
    }

    /// <summary>The disk cache, in memory and counting the reads it is asked
    /// for.</summary>
    private sealed class CountingFacts : ITranscriptFactsCache
    {
        private readonly Dictionary<string, TranscriptFacts> _entries = new(StringComparer.OrdinalIgnoreCase);

        public int Reads { get; private set; }

        public TranscriptFacts? TryRead(string path, long length, DateTimeOffset writtenAt)
        {
            Reads++;

            return _entries.GetValueOrDefault($"{path}|{length}|{writtenAt.UtcTicks}");
        }

        public void Write(string path, long length, DateTimeOffset writtenAt, TranscriptFacts facts) =>
            _entries[$"{path}|{length}|{writtenAt.UtcTicks}"] = facts;

        public void Forget() => _entries.Clear();
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
        }
    }
}
