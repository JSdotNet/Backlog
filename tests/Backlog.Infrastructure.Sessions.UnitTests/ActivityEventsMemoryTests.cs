using System.Globalization;
using Backlog.Infrastructure.Sessions;
using Microsoft.Extensions.Time.Testing;

namespace Backlog.Infrastructure.Sessions.UnitTests;

/// <summary>
/// What the activity reader remembers between two reads of the same profile: the
/// whole-file fold of every event file it read, and where it stopped in the ones being
/// written to — Claude transcripts, the subagent transcripts beside them, and Copilot's
/// event streams, which all go through one path.
/// <para>
/// Every test here reads through one source more than once, which is the only way the
/// memory shows; <see cref="AgentActivitySourceTests"/> builds a source per read and is
/// not about it. The yardstick is a fresh source over the same files: whatever a
/// remembered or resumed read answers, a reader that had never seen the files must
/// answer too.
/// </para>
/// <para>
/// The files are opened through a counting opener rather than the file system's own, so
/// a test can say how many times a file was opened and how many of its bytes were read,
/// and where.
/// </para>
/// </summary>
public sealed class ActivityEventsMemoryTests : IDisposable
{
    private static readonly DateTimeOffset Noon = new(2026, 8, 19, 12, 0, 0, TimeSpan.Zero);

    private static readonly DateTimeOffset Horizon = Noon.AddDays(-7);

    /// <summary>When the events in these files happened: well inside the horizon, and
    /// well before the files were last written.</summary>
    private static readonly DateTimeOffset Yesterday = Noon.AddDays(-1);

    private const string Slug = "D--Repos-Backlog";

    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        "backlog-activity-memory-tests",
        Guid.NewGuid().ToString("n"));

    private string ClaudeHome => Path.Combine(_root, ".claude");

    private string CopilotHome => Path.Combine(_root, ".copilot");

    // --- Unchanged ---------------------------------------------------------------

    /// <summary>A second read of files nothing has touched opens none of them and asks
    /// the disk cache about none of them: the reader already holds their folds. A
    /// session, an agent it spawned and a Copilot session, because all three go through
    /// the one path.</summary>
    [Fact]
    public async Task Files_nothing_touched_since_the_last_read_are_answered_without_opening_them_or_the_cache()
    {
        GivenFile(Transcript("still"), Noon.AddHours(-1), Assistant("still", Yesterday), Assistant("still", Yesterday.AddMinutes(2)));
        GivenFile(Spawned("still", "a1"), Noon.AddHours(-1), Sidechain("still", "a1", Yesterday), Sidechain("still", "a1", Yesterday.AddMinutes(3)));
        GivenFile(Copilot("pilot"), Noon.AddHours(-1), CopilotEvent(Yesterday), CopilotEvent(Yesterday.AddMinutes(4)));

        var opener = new CountingOpener();
        var cache = new CountingCache();
        var source = Source(opener, cache);

        var first = Describe(await ReadAsync(source));
        var second = Describe(await ReadAsync(source));

        Assert.Equal(3, opener.Opens);
        Assert.Equal(3, cache.Reads);
        Assert.Equal(first, second);
        Assert.Equal(await FreshAsync(), second);
    }

    /// <summary>
    /// Proof that the remembered fold is what comes back rather than a quiet second
    /// read: the content is swapped at the same length and the write time put back, and
    /// the record still says what the first read found.
    /// </summary>
    [Fact]
    public async Task A_same_length_swap_behind_an_old_write_time_is_answered_from_memory()
    {
        GivenFile(Transcript("swapped"), Noon.AddHours(-1), Assistant("swapped", Yesterday), Assistant("swapped", Yesterday.AddMinutes(2)));
        var source = Source(new CountingOpener());

        _ = await ReadAsync(source);
        Swap(Transcript("swapped"), Assistant("swapped", Yesterday), Assistant("swapped", Yesterday.AddMinutes(3)));

        var run = Assert.Single(Assert.Single((await ReadAsync(source)).Sessions).Runs);

        Assert.Equal(Yesterday.AddMinutes(2), run.EndedAt);
    }

    /// <summary>
    /// The same swap on a file written in the instant of the read is not trusted, and
    /// what that read found is not written to disk either. A write time is only as fine
    /// as the file system keeps it, so a file rewritten in the tick it was read in can
    /// match on length and time and still be another file.
    /// </summary>
    [Fact]
    public async Task A_file_written_in_the_instant_of_the_read_is_checked_again_and_not_remembered_on_disk()
    {
        GivenFile(Transcript("racy"), Noon, Assistant("racy", Yesterday), Assistant("racy", Yesterday.AddMinutes(2)));
        var cache = new CountingCache();
        var source = Source(new CountingOpener(), cache);

        _ = await ReadAsync(source);
        Swap(Transcript("racy"), Assistant("racy", Yesterday), Assistant("racy", Yesterday.AddMinutes(3)));

        var run = Assert.Single(Assert.Single((await ReadAsync(source)).Sessions).Runs);

        Assert.Equal(Yesterday.AddMinutes(3), run.EndedAt);
        Assert.Equal(0, cache.Writes);
    }

    /// <summary>
    /// And not on any read after it either, once the file has settled. What a read in
    /// the instant of the write took may predate a rewrite in that same tick; the first
    /// read after the file settles checks it again, finds the rewrite, and is the one
    /// that is remembered — and written to disk.
    /// </summary>
    [Fact]
    public async Task What_a_read_in_the_instant_of_the_write_took_is_not_answered_from_memory_once_the_file_settles()
    {
        GivenFile(Transcript("racy"), Noon, Assistant("racy", Yesterday), Assistant("racy", Yesterday.AddMinutes(2)));
        var clock = new FakeTimeProvider(Noon);
        var cache = new CountingCache();
        var source = Source(new CountingOpener(), cache, clock);

        _ = await ReadAsync(source);
        Swap(Transcript("racy"), Assistant("racy", Yesterday), Assistant("racy", Yesterday.AddMinutes(3)));
        clock.Advance(TimeSpan.FromSeconds(3));

        var run = Assert.Single(Assert.Single((await ReadAsync(source)).Sessions).Runs);
        var remembered = cache.Peek(Transcript("racy"));

        Assert.Equal(Yesterday.AddMinutes(3), run.EndedAt);
        Assert.Equal(Yesterday.AddMinutes(3), Assert.Single(remembered!.Runs).EndedAt);
    }

    // --- Grown -------------------------------------------------------------------

    /// <summary>
    /// The saving itself. A transcript of a few hundred kilobytes grows by two lines, and
    /// the next read reads those two lines past where it stopped — plus the opening bytes
    /// it checks the file by — and answers what a reader that had never seen the file
    /// answers.
    /// </summary>
    [Fact]
    public async Task A_grown_transcript_is_read_from_where_the_last_read_stopped()
    {
        GivenFile(Transcript("grown"), Noon.AddHours(-1), Turns("grown", Yesterday, 300));
        var stopped = LengthOf(Transcript("grown"));

        var opener = new CountingOpener();
        var source = Source(opener);
        _ = await ReadAsync(source);

        opener.Reset();
        Append(Transcript("grown"), Noon.AddMinutes(-30), Prompt("grown", Yesterday.AddHours(2)), Assistant("grown", Yesterday.AddHours(2).AddMinutes(1)));
        var appended = LengthOf(Transcript("grown")) - stopped;

        var resumed = Describe(await ReadAsync(source));

        Assert.Equal(appended, opener.BytesFrom(stopped));
        Assert.True(opener.BytesBefore(stopped) <= TranscriptTail.PrefixBytes);
        Assert.Equal(await FreshAsync(), resumed);
    }

    /// <summary>
    /// A question asked before the boundary and answered after it. The answer is a human
    /// turn only because the read that finds it still knows the question was asked —
    /// which a resumed read knows only if it carries on the last read's state.
    /// </summary>
    [Fact]
    public async Task An_answer_appended_after_the_question_was_read_is_still_a_human_turn()
    {
        GivenFile(Transcript("asked"), Noon.AddHours(-1),
            HumanPrompt("asked", Yesterday),
            ToolCall("asked", Yesterday.AddMinutes(9), "toolu_ask", "AskUserQuestion"));
        var source = Source(new CountingOpener());
        _ = await ReadAsync(source);

        Append(Transcript("asked"), Noon.AddMinutes(-30),
            ToolAnswer("asked", Yesterday.AddMinutes(25), "toolu_ask", "User has answered your questions."),
            Assistant("asked", Yesterday.AddMinutes(26)));

        var log = await ReadAsync(source);

        Assert.Equal([Yesterday, Yesterday.AddMinutes(25)], Assert.Single(log.Sessions).HumanTurns);
        Assert.Equal(await FreshAsync(), Describe(log));
    }

    /// <summary>
    /// A line appended later can be dated earlier than lines already read. The runs are
    /// folded again over every event rather than continued, so an event that lands
    /// inside a gap closes it the way a whole read closes it: two runs become one.
    /// </summary>
    [Fact]
    public async Task An_appended_event_dated_before_the_ones_already_read_is_folded_where_its_time_puts_it()
    {
        GivenFile(Transcript("unsorted"), Noon.AddHours(-1),
            Assistant("unsorted", Yesterday),
            Assistant("unsorted", Yesterday.AddMinutes(1)),
            Assistant("unsorted", Yesterday.AddMinutes(7)),
            Assistant("unsorted", Yesterday.AddMinutes(8)));
        var source = Source(new CountingOpener());

        Assert.Equal(2, Assert.Single((await ReadAsync(source)).Sessions).Runs.Count);

        Append(Transcript("unsorted"), Noon.AddMinutes(-30), Assistant("unsorted", Yesterday.AddMinutes(4)));

        var log = await ReadAsync(source);

        Assert.Equal(new AgentActivityRun(Yesterday, Yesterday.AddMinutes(8)), Assert.Single(Assert.Single(log.Sessions).Runs));
        Assert.Equal(await FreshAsync(), Describe(log));
    }

    [Fact]
    public async Task A_grown_copilot_event_stream_is_read_from_where_the_last_read_stopped()
    {
        GivenFile(Copilot("pilot"), Noon.AddHours(-1), [.. Enumerable.Range(0, 200).Select(index => CopilotEvent(Yesterday.AddSeconds(index)))]);
        var stopped = LengthOf(Copilot("pilot"));

        var opener = new CountingOpener();
        var source = Source(opener);
        _ = await ReadAsync(source);

        opener.Reset();
        Append(Copilot("pilot"), Noon.AddMinutes(-30), CopilotEvent(Yesterday.AddHours(1)), CopilotEvent(Yesterday.AddHours(1).AddMinutes(2)));
        var appended = LengthOf(Copilot("pilot")) - stopped;

        var resumed = Describe(await ReadAsync(source));

        Assert.Equal(appended, opener.BytesFrom(stopped));
        Assert.True(opener.BytesBefore(stopped) <= TranscriptTail.PrefixBytes);
        Assert.Equal(await FreshAsync(), resumed);
    }

    [Fact]
    public async Task A_grown_subagent_transcript_is_read_from_where_the_last_read_stopped()
    {
        GivenFile(Transcript("parent"), Noon.AddHours(-1), Assistant("parent", Yesterday), Assistant("parent", Yesterday.AddMinutes(1)));
        GivenFile(Spawned("parent", "a1"), Noon.AddHours(-1), [.. Enumerable.Range(0, 200).Select(index => Sidechain("parent", "a1", Yesterday.AddSeconds(index)))]);
        var stopped = LengthOf(Spawned("parent", "a1"));

        var opener = new CountingOpener();
        var source = Source(opener);
        _ = await ReadAsync(source);

        opener.Reset();
        Append(Spawned("parent", "a1"), Noon.AddMinutes(-30), Sidechain("parent", "a1", Yesterday.AddHours(1)), Sidechain("parent", "a1", Yesterday.AddHours(1).AddMinutes(2)));
        var appended = LengthOf(Spawned("parent", "a1")) - stopped;

        var resumed = await ReadAsync(source);

        Assert.Equal(appended, opener.BytesFrom(stopped, Spawned("parent", "a1")));
        Assert.True(opener.BytesBefore(stopped, Spawned("parent", "a1")) <= TranscriptTail.PrefixBytes);
        Assert.Equal(2, Assert.Single(resumed.Subagents).Runs.Count);
        Assert.Equal(await FreshAsync(), Describe(resumed));
    }

    // --- Started over ------------------------------------------------------------

    [Fact]
    public async Task A_file_shorter_than_where_the_last_read_stopped_is_read_again_whole()
    {
        GivenFile(Transcript("cut"), Noon.AddHours(-1), Turns("cut", Yesterday, 6));
        var source = Source(new CountingOpener());
        _ = await ReadAsync(source);

        Rewrite(Transcript("cut"), Noon.AddMinutes(-30), Assistant("cut", Yesterday.AddHours(3)), Assistant("cut", Yesterday.AddHours(3).AddMinutes(1)));

        var log = await ReadAsync(source);

        Assert.Equal(new AgentActivityRun(Yesterday.AddHours(3), Yesterday.AddHours(3).AddMinutes(1)), Assert.Single(Assert.Single(log.Sessions).Runs));
        Assert.Equal(await FreshAsync(), Describe(log));
    }

    [Fact]
    public async Task A_file_rewritten_with_other_opening_bytes_is_read_again_whole()
    {
        GivenFile(Transcript("rewritten"), Noon.AddHours(-1), Assistant("rewritten", Yesterday), Assistant("rewritten", Yesterday.AddMinutes(1)));
        var source = Source(new CountingOpener());
        _ = await ReadAsync(source);

        Rewrite(Transcript("rewritten"), Noon.AddMinutes(-30), Turns("rewritten", Yesterday.AddHours(3), 4));

        var log = await ReadAsync(source);

        Assert.Equal(Yesterday.AddHours(3), Assert.Single(Assert.Single(log.Sessions).Runs).StartedAt);
        Assert.Equal(await FreshAsync(), Describe(log));
    }

    /// <summary>
    /// A file deleted and written again under the same name, opening with the same
    /// bytes. Only the creation time tells it apart, so the opening line is made longer
    /// than the fingerprint and the difference sits behind it: resumed, the read would
    /// keep the old file's events and add whatever lies past the old offset.
    /// </summary>
    [Fact]
    public async Task A_file_created_again_is_read_again_whole()
    {
        var opening = Assistant("again", Yesterday, new string('x', TranscriptTail.PrefixBytes * 2));

        GivenFile(Transcript("again"), Noon.AddHours(-1), opening, Assistant("again", Yesterday.AddMinutes(1)), Assistant("again", Yesterday.AddMinutes(2)));
        var source = Source(new CountingOpener());
        _ = await ReadAsync(source);

        Rewrite(Transcript("again"), Noon.AddMinutes(-30), [opening, .. Turns("again", Yesterday.AddHours(3), 4)]);
        File.SetCreationTimeUtc(Transcript("again"), Noon.AddMinutes(-30).UtcDateTime);

        var log = await ReadAsync(source);

        Assert.DoesNotContain(new AgentActivityRun(Yesterday, Yesterday.AddMinutes(2)), Assert.Single(log.Sessions).Runs);
        Assert.Equal(await FreshAsync(), Describe(log));
    }

    /// <summary>
    /// A resumed read cut short by the file going away under it. The session is absent
    /// from that read, as a file that could not be read has always been, and the read
    /// after it, with the file readable again, answers what a whole read does: nothing
    /// the interrupted pass folded was kept.
    /// </summary>
    [Fact]
    public async Task A_resumed_read_that_fails_part_way_keeps_nothing_it_read()
    {
        GivenFile(Transcript("failing"), Noon.AddHours(-1), Assistant("failing", Yesterday), Assistant("failing", Yesterday.AddMinutes(1)));
        var stopped = LengthOf(Transcript("failing"));

        var opener = new CountingOpener();
        var source = Source(opener);
        _ = await ReadAsync(source);

        // More than one read's worth, failing past the first, so the pass has folded
        // a hundred events by the time it fails.
        Append(Transcript("failing"), Noon.AddMinutes(-30), Turns("failing", Yesterday.AddHours(2), 200));

        opener.FailPast = stopped + 70_000;
        var failed = await ReadAsync(source);

        opener.FailPast = null;
        var read = await ReadAsync(source);

        Assert.Empty(failed.Sessions);
        Assert.Equal(await FreshAsync(), Describe(read));
        Assert.Equal(2, Assert.Single(read.Sessions).Runs.Count);
    }

    // --- The disk cache --------------------------------------------------------

    /// <summary>The first read after a launch: a reader that has never seen the file
    /// answers it from the disk entry another reader wrote, without opening it.</summary>
    [Fact]
    public async Task A_file_read_by_an_earlier_reader_is_answered_from_the_disk_without_being_opened()
    {
        GivenFile(Transcript("known"), Noon.AddHours(-1), Assistant("known", Yesterday), Assistant("known", Yesterday.AddMinutes(2)));
        var cache = new CountingCache();
        var before = Describe(await ReadAsync(Source(new CountingOpener(), cache)));

        var opener = new CountingOpener();
        var after = Describe(await ReadAsync(Source(opener, cache)));

        Assert.Equal(0, opener.Opens);
        Assert.Equal(before, after);
    }

    /// <summary>A resumed read that reached the end is a fact about the file as it was
    /// last written, and is written through for the next launch.</summary>
    [Fact]
    public async Task A_grown_file_read_to_its_end_is_remembered_on_disk_at_its_new_write_time()
    {
        GivenFile(Transcript("through"), Noon.AddHours(-1), Assistant("through", Yesterday), Assistant("through", Yesterday.AddMinutes(1)));
        var cache = new CountingCache();
        var source = Source(new CountingOpener(), cache);
        _ = await ReadAsync(source);

        Append(Transcript("through"), Noon.AddMinutes(-30), Assistant("through", Yesterday.AddHours(1)), Assistant("through", Yesterday.AddHours(1).AddMinutes(1)));
        _ = await ReadAsync(source);

        Assert.Equal(2, cache.Peek(Transcript("through"))?.Runs.Count);
    }

    /// <summary>
    /// A file that stops in the middle of a line was read short of what it holds. That
    /// is never written down against the write time — the key has no length to tell the
    /// short reading from the whole one — and the half line is still there to be read
    /// when the rest of it arrives.
    /// </summary>
    [Fact]
    public async Task A_file_ending_in_half_a_line_is_not_remembered_on_disk_and_reads_the_line_once_it_is_whole()
    {
        GivenFile(Transcript("open"), Noon.AddHours(-1), Assistant("open", Yesterday), Assistant("open", Yesterday.AddMinutes(1)));
        var cache = new CountingCache();
        var source = Source(new CountingOpener(), cache);
        _ = await ReadAsync(source);
        var writes = cache.Writes;

        var line = Assistant("open", Yesterday.AddMinutes(2));
        File.AppendAllText(Transcript("open"), line[..40]);
        File.SetLastWriteTimeUtc(Transcript("open"), Noon.AddMinutes(-30).UtcDateTime);
        var cut = await ReadAsync(source);

        Assert.Equal(writes, cache.Writes);
        Assert.Equal(Yesterday.AddMinutes(1), Assert.Single(Assert.Single(cut.Sessions).Runs).EndedAt);

        Append(Transcript("open"), Noon.AddMinutes(-20), line[40..], Assistant("open", Yesterday.AddMinutes(3)));
        var read = await ReadAsync(source);

        Assert.Equal(new AgentActivityRun(Yesterday, Yesterday.AddMinutes(3)), Assert.Single(Assert.Single(read.Sessions).Runs));
        Assert.Equal(await FreshAsync(), Describe(read));
    }

    // --- What is kept ----------------------------------------------------------

    /// <summary>Where a read stopped is kept only for the files written in the last day,
    /// decided when the read is kept: one older than that keeps no fold, is answered from
    /// memory while it stands still, and is read whole when it moves.</summary>
    [Fact]
    public async Task A_file_last_written_over_a_day_ago_keeps_no_fold_and_is_read_whole_when_it_grows()
    {
        GivenFile(Transcript("old"), Noon.AddHours(-30), Turns("old", Yesterday.AddHours(-8), 20));
        var opener = new CountingOpener();
        var source = Source(opener);
        _ = await ReadAsync(source);

        var stopped = LengthOf(Transcript("old"));
        Append(Transcript("old"), Noon.AddHours(-26), Assistant("old", Yesterday.AddHours(-7)));
        opener.Reset();

        var read = await ReadAsync(source);

        Assert.Equal(stopped, opener.BytesBefore(stopped));
        Assert.Equal(await FreshAsync(), Describe(read));
    }

    /// <summary>And for no more than 32 of them, the most recently written: the 33rd is
    /// read whole when it moves, the 32nd from where it stopped. One limit across the
    /// sessions, the agents they spawned and Copilot's sessions, because it bounds one
    /// memory.</summary>
    [Fact]
    public async Task Only_the_32_most_recently_written_files_keep_where_their_read_stopped()
    {
        for (var index = 0; index < 33; index++)
        {
            GivenFile(Transcript($"t{index:00}"), Noon.AddHours(-1).AddMinutes(index), Turns($"t{index:00}", Yesterday, 20));
        }

        var opener = new CountingOpener();
        var source = Source(opener);
        _ = await ReadAsync(source);

        var stopped = LengthOf(Transcript("t00"));
        Append(Transcript("t00"), Noon.AddMinutes(-20), Assistant("t00", Yesterday.AddHours(1)));
        Append(Transcript("t01"), Noon.AddMinutes(-19), Assistant("t01", Yesterday.AddHours(1)));
        opener.Reset();

        _ = await ReadAsync(source);

        Assert.Equal(stopped, opener.BytesBefore(stopped, Transcript("t00")));
        Assert.True(opener.BytesBefore(stopped, Transcript("t01")) < stopped);
    }

    /// <summary>
    /// A file the walk no longer returns is forgotten. Proved by writing it again, with
    /// other content at the same length and the same write time: a reader that still
    /// held the old fold would answer it from memory.
    /// </summary>
    [Fact]
    public async Task A_file_the_walk_no_longer_finds_is_forgotten()
    {
        GivenFile(Transcript("gone"), Noon.AddHours(-1), Assistant("gone", Yesterday), Assistant("gone", Yesterday.AddMinutes(2)));
        GivenFile(Transcript("stays"), Noon.AddHours(-1), Assistant("stays", Yesterday), Assistant("stays", Yesterday.AddMinutes(1)));
        var source = Source(new CountingOpener());
        _ = await ReadAsync(source);

        File.Delete(Transcript("gone"));
        _ = await ReadAsync(source);

        GivenFile(Transcript("gone"), Noon.AddHours(-1), Assistant("gone", Yesterday), Assistant("gone", Yesterday.AddMinutes(3)));

        var log = await ReadAsync(source);

        Assert.Equal(Yesterday.AddMinutes(3), Assert.Single(log.Sessions.Single(session => session.Id == "gone").Runs).EndedAt);
    }

    /// <summary>One reader is shared by every consumer of the port, so reads overlap.
    /// Each answers what one read alone answers.</summary>
    [Fact]
    public async Task Reads_that_overlap_answer_what_one_read_answers()
    {
        for (var index = 0; index < 4; index++)
        {
            GivenFile(Transcript($"s{index}"), Noon.AddHours(-1).AddMinutes(index), Turns($"s{index}", Yesterday.AddHours(index), 40));
        }

        GivenFile(Copilot("pilot"), Noon.AddHours(-1), CopilotEvent(Yesterday), CopilotEvent(Yesterday.AddMinutes(4)));

        var source = Source(new CountingOpener());
        var reads = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => Task.Run(() => ReadAsync(source))));

        var fresh = await FreshAsync();

        Assert.All(reads, log => Assert.Equal(fresh, Describe(log)));
    }

    // --- Helpers -----------------------------------------------------------------

    private LocalAgentActivitySource Source(CountingOpener opener, IAgentActivityCache? cache = null, TimeProvider? clock = null) =>
        new(ClaudeHome, CopilotHome, "machine", "DEV-TOWER", cache, opener.Open, clock ?? new FakeTimeProvider(Noon));

    private static Task<AgentActivityLog> ReadAsync(LocalAgentActivitySource source) =>
        source.GetActivityAsync(Horizon, TestContext.Current.CancellationToken);

    /// <summary>What a reader that had never seen the profile answers.</summary>
    private async Task<string> FreshAsync() =>
        Describe(await ReadAsync(Source(new CountingOpener())));

    /// <summary>Everything a read answers, as one comparable string.</summary>
    private static string Describe(AgentActivityLog log) => string.Join(
        Environment.NewLine,
        [
            .. log.Sessions
                .OrderBy(session => session.Kind)
                .ThenBy(session => session.Id, StringComparer.Ordinal)
                .Select(session => string.Join(
                    " | ",
                    session.Kind,
                    session.Id,
                    string.Join(", ", session.Runs),
                    string.Join(", ", session.Waits),
                    string.Join(", ", session.LimitHits),
                    string.Join(", ", session.HumanTurns))),
            .. log.Subagents
                .OrderBy(agent => agent.Id, StringComparer.Ordinal)
                .Select(agent => string.Join(
                    " | ",
                    "agent",
                    agent.Id,
                    agent.SessionId,
                    string.Join(", ", agent.Runs),
                    string.Join(", ", agent.LimitHits))),
            .. log.Unreadable
        ]);

    private string Transcript(string id) => Path.Combine(ClaudeHome, "projects", Slug, $"{id}.jsonl");

    private string Spawned(string sessionId, string agentId) =>
        Path.Combine(ClaudeHome, "projects", Slug, sessionId, "subagents", $"agent-{agentId}.jsonl");

    private string Copilot(string id) => Path.Combine(CopilotHome, "session-state", id, "events.jsonl");

    /// <summary>A run of assistant turns a minute apart, prompted once at the start —
    /// lines in Claude's real shape, to give a file some length.</summary>
    private static string[] Turns(string id, DateTimeOffset from, int count) =>
        [Prompt(id, from), .. Enumerable.Range(1, count - 1).Select(index => Assistant(id, from.AddMinutes(index)))];

    private static long LengthOf(string path) => new FileInfo(path).Length;

    private static void GivenFile(string path, DateTimeOffset lastWrite, params IEnumerable<string> lines)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllLines(path, lines);
        File.SetLastWriteTimeUtc(path, lastWrite.UtcDateTime);
    }

    private static void Append(string path, DateTimeOffset lastWrite, params IEnumerable<string> lines)
    {
        File.AppendAllLines(path, lines);
        File.SetLastWriteTimeUtc(path, lastWrite.UtcDateTime);
    }

    /// <summary>New content in place, the creation time kept.</summary>
    private static void Rewrite(string path, DateTimeOffset lastWrite, params IEnumerable<string> lines)
    {
        var created = new FileInfo(path).CreationTimeUtc;
        File.WriteAllLines(path, lines);
        File.SetCreationTimeUtc(path, created);
        File.SetLastWriteTimeUtc(path, lastWrite.UtcDateTime);
    }

    /// <summary>Other content of the same length, with the write time and creation time
    /// put back: a change neither the length nor the time can see.</summary>
    private static void Swap(string path, params IEnumerable<string> lines)
    {
        var before = new FileInfo(path);
        var (length, written, created) = (before.Length, before.LastWriteTimeUtc, before.CreationTimeUtc);

        File.WriteAllLines(path, lines);
        File.SetCreationTimeUtc(path, created);
        File.SetLastWriteTimeUtc(path, written);

        Assert.Equal(length, new FileInfo(path).Length);
    }

    private static string Prompt(string id, DateTimeOffset at) =>
        $$"""
        {"parentUuid":null,"isSidechain":false,"type":"user","message":{"role":"user","content":"go on"},"promptSource":"typed","uuid":"7c1b0f2a-4d2e-4a91-9f2c-1d8a0b3e6c22","timestamp":"{{Stamp(at)}}","cwd":"D:\\Repos\\Backlog","sessionId":"{{id}}","version":"2.1.229","gitBranch":"main"}
        """;

    private static string HumanPrompt(string id, DateTimeOffset at) =>
        $$"""
        {"parentUuid":null,"isSidechain":false,"type":"user","message":{"role":"user","content":"look at the roadmap"},"uuid":"7c1b0f2a-4d2e-4a91-9f2c-1d8a0b3e6c23","timestamp":"{{Stamp(at)}}","promptSource":"sdk","origin":{"kind":"human"},"entrypoint":"claude-desktop","cwd":"D:\\Repos\\Backlog","sessionId":"{{id}}","version":"2.1.286","gitBranch":"main"}
        """;

    private static string ToolCall(string id, DateTimeOffset at, string toolUseId, string name) =>
        $$$"""
        {"parentUuid":"7c1b0f2a-4d2e-4a91-9f2c-1d8a0b3e6c23","isSidechain":false,"type":"assistant","message":{"id":"msg_01Hs","type":"message","role":"assistant","model":"claude-opus-5-5","content":[{"type":"tool_use","id":"{{{toolUseId}}}","name":"{{{name}}}","input":{}}],"usage":{"input_tokens":4,"output_tokens":9}},"uuid":"1a2b3c4d-5e6f-4a7b-8c9d-0e1f2a3b4c5e","timestamp":"{{{Stamp(at)}}}","cwd":"D:\\Repos\\Backlog","sessionId":"{{{id}}}","version":"2.1.286","gitBranch":"main"}
        """;

    private static string ToolAnswer(string id, DateTimeOffset at, string toolUseId, string content) =>
        $$$"""
        {"parentUuid":"1a2b3c4d-5e6f-4a7b-8c9d-0e1f2a3b4c5e","isSidechain":false,"type":"user","message":{"role":"user","content":[{"tool_use_id":"{{{toolUseId}}}","type":"tool_result","content":{{{System.Text.Json.JsonSerializer.Serialize(content)}}}}]},"uuid":"2b3c4d5e-6f7a-4b8c-9d0e-1f2a3b4c5d6f","timestamp":"{{{Stamp(at)}}}","cwd":"D:\\Repos\\Backlog","sessionId":"{{{id}}}","version":"2.1.286","gitBranch":"main"}
        """;

    private static string Assistant(string id, DateTimeOffset at, string text = "carrying on") =>
        $$$"""
        {"parentUuid":"7c1b0f2a-4d2e-4a91-9f2c-1d8a0b3e6c22","isSidechain":false,"type":"assistant","message":{"id":"msg_01Hs","type":"message","role":"assistant","model":"claude-opus-4-5-20260101","content":[{"type":"text","text":"{{{text}}}"}],"usage":{"input_tokens":4,"output_tokens":9}},"uuid":"1a2b3c4d-5e6f-4a7b-8c9d-0e1f2a3b4c5d","timestamp":"{{{Stamp(at)}}}","cwd":"D:\\Repos\\Backlog","sessionId":"{{{id}}}","version":"2.1.229","gitBranch":"main"}
        """;

    private static string Sidechain(string sessionId, string agentId, DateTimeOffset at) =>
        $$$"""
        {"parentUuid":null,"isSidechain":true,"agentId":"{{{agentId}}}","type":"assistant","message":{"id":"msg_01Hs","type":"message","role":"assistant","model":"claude-opus-4-5-20260101","content":[{"type":"text","text":"working"}],"usage":{"input_tokens":4,"output_tokens":9}},"uuid":"5d6e7f8a-9b0c-4d1e-8f2a-3b4c5d6e7f80","timestamp":"{{{Stamp(at)}}}","cwd":"D:\\Repos\\Backlog","sessionId":"{{{sessionId}}}","version":"2.1.229","gitBranch":"main"}
        """;

    private static string CopilotEvent(DateTimeOffset at) =>
        $$$"""
        {"type":"assistant.message","timestamp":"{{{Stamp(at)}}}","data":{"turn_id":"f4a1c7d2-9b3e-4c15-8a6d-2e7f0b9c4a13"}}
        """;

    private static string Stamp(DateTimeOffset at) =>
        at.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ss.fffZ", CultureInfo.InvariantCulture);

    /// <summary>The file opener, counting: how many opens, and which bytes each read
    /// covered. It can be told to fail a read that reaches past an offset, which is the
    /// agent truncating or locking the file under a read. Thread-safe, because one test
    /// reads through one source from several threads.</summary>
    private sealed class CountingOpener
    {
        private readonly Lock _gate = new();
        private readonly List<(string Path, long Position, int Count)> _reads = [];
        private int _opens;

        public int Opens => Volatile.Read(ref _opens);

        public long? FailPast { get; set; }

        public Stream Open(string path)
        {
            Interlocked.Increment(ref _opens);

            return new CountingStream(TranscriptTail.OpenShared(path), path, this);
        }

        public void Reset()
        {
            lock (_gate)
            {
                _opens = 0;
                _reads.Clear();
            }
        }

        /// <summary>The bytes read at or after <paramref name="offset"/>, of one file or
        /// of all of them.</summary>
        public long BytesFrom(long offset, string? path = null)
        {
            lock (_gate)
            {
                return Of(path).Sum(read => Math.Max(0, read.Position + read.Count - Math.Max(read.Position, offset)));
            }
        }

        /// <summary>The bytes read before <paramref name="offset"/>, of one file or of
        /// all of them.</summary>
        public long BytesBefore(long offset, string? path = null)
        {
            lock (_gate)
            {
                return Of(path).Sum(read => Math.Max(0, Math.Min(read.Position + read.Count, offset) - read.Position));
            }
        }

        private IEnumerable<(string Path, long Position, int Count)> Of(string? path) =>
            _reads.Where(read => path is null || string.Equals(read.Path, path, StringComparison.OrdinalIgnoreCase));

        private void Record(string path, long position, int count)
        {
            if (FailPast is { } limit && position + count > limit) throw new IOException("The file went away mid-read.");

            lock (_gate)
            {
                _reads.Add((path, position, count));
            }
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

    /// <summary>The disk cache, in memory and counting the reads and writes it is asked
    /// for, keyed as the disk half keys it: the path, the write time and the
    /// threshold.</summary>
    private sealed class CountingCache : IAgentActivityCache
    {
        private readonly Lock _gate = new();
        private readonly Dictionary<string, AgentActivityEntry> _entries = new(StringComparer.OrdinalIgnoreCase);

        public int Reads { get; private set; }

        public int Writes { get; private set; }

        public AgentActivityEntry? TryRead(string path, DateTimeOffset writtenAt, TimeSpan idleAfter)
        {
            lock (_gate)
            {
                Reads++;

                return _entries.GetValueOrDefault(Key(path, writtenAt, idleAfter));
            }
        }

        public void Write(string path, DateTimeOffset writtenAt, AgentActivityEntry entry)
        {
            lock (_gate)
            {
                Writes++;
                _entries[Key(path, writtenAt, entry.IdleAfter)] = entry;
            }
        }

        public void Forget()
        {
            lock (_gate)
            {
                _entries.Clear();
            }
        }

        /// <summary>What is stored for the file as it is written now, without counting
        /// as a read.</summary>
        public AgentActivityEntry? Peek(string path)
        {
            var written = new DateTimeOffset(new FileInfo(path).LastWriteTimeUtc, TimeSpan.Zero);

            lock (_gate)
            {
                return _entries.GetValueOrDefault(Key(path, written, AgentActivityRuns.IdleAfter));
            }
        }

        private static string Key(string path, DateTimeOffset writtenAt, TimeSpan idleAfter) =>
            $"{path}|{writtenAt.UtcTicks}|{idleAfter}";
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
        }
    }
}
