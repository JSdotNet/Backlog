using System.Text;
using Backlog.Infrastructure.Sessions;
using Microsoft.Extensions.Time.Testing;

namespace Backlog.Infrastructure.Sessions.UnitTests;

/// <summary>
/// What the Claude desktop app's session store says about a session, read the way the
/// store is laid out on disk: <c>&lt;account&gt;/&lt;org&gt;/local_&lt;guid&gt;.json</c>,
/// one record per desktop session, keyed here by the CLI session it drives.
/// <para>
/// Fixture folders shaped like the real store, keys and all — the real one is the
/// desktop app's own and undocumented, so a tidied-up fixture would prove the reader
/// against a format nobody writes.
/// </para>
/// </summary>
public sealed class ClaudeDesktopSessionReaderTests : IDisposable
{
    private static readonly DateTimeOffset Noon = new(2026, 10, 21, 12, 0, 0, TimeSpan.Zero);

    private const string Account = "76e40352-1b9a-468f-8ef7-caaf76d5103c";
    private const string Org = "6a29ea76-8694-4b36-aea5-a4d4b0f03805";

    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        "backlog-claude-desktop-tests",
        Guid.NewGuid().ToString("n"));

    private int _opens;

    [Fact]
    public async Task The_archived_flag_and_scheduled_task_are_read_per_cli_session_across_accounts()
    {
        GivenRecord(Account, Org, "local_a", """{"sessionId":"local_a","cliSessionId":"cli-a","isArchived":false,"lastActivityAt":1792598478687}""");
        GivenRecord(Account, Org, "local_b", """{"sessionId":"local_b","cliSessionId":"cli-b","isArchived":true,"scheduledTaskId":"morning-brief"}""");
        GivenRecord("other-account", "other-org", "local_c", """{"sessionId":"local_c","cliSessionId":"cli-c","isArchived":false,"scheduledTaskId":"nightly-sweep"}""");

        var sessions = await ReadAsync();

        Assert.Equal(false, sessions.For("cli-a")!.Archived);
        Assert.Null(sessions.For("cli-a")!.ScheduledTask);
        Assert.Equal(true, sessions.For("cli-b")!.Archived);
        Assert.Equal("morning-brief", sessions.For("cli-b")!.ScheduledTask);
        Assert.Equal(false, sessions.For("cli-c")!.Archived);
        Assert.Equal("nightly-sweep", sessions.For("cli-c")!.ScheduledTask);

        // Keyed case-blind, because a session id is a Guid however it was spelled.
        Assert.NotNull(sessions.For("CLI-A"));
        Assert.Null(sessions.For("cli-unknown"));
    }

    /// <summary>The store is the desktop app's own and undocumented: a record that
    /// leaves the flag out states nothing, and nothing is not "not archived".</summary>
    [Fact]
    public async Task A_record_without_the_flag_reads_null_not_false()
    {
        GivenRecord(Account, Org, "local_a", """{"sessionId":"local_a","cliSessionId":"cli-a"}""");

        var sessions = await ReadAsync();

        Assert.Null(sessions.For("cli-a")!.Archived);
        Assert.False(sessions.IsOpen("cli-a"));
    }

    [Fact]
    public async Task Only_local_records_are_read_and_never_the_scheduled_tasks_file()
    {
        var folder = Directory.CreateDirectory(Path.Combine(_root, Account, Org));

        File.WriteAllText(
            Path.Combine(folder.FullName, "scheduled-tasks.json"),
            """{"sessionId":"local_x","cliSessionId":"cli-x","isArchived":false}""");
        File.WriteAllText(
            Path.Combine(folder.FullName, "remote_y.json"),
            """{"sessionId":"remote_y","cliSessionId":"cli-y","isArchived":false}""");
        GivenRecord(Account, Org, "local_a", """{"sessionId":"local_a","cliSessionId":"cli-a","isArchived":false}""");

        var sessions = await ReadAsync();

        Assert.Null(sessions.For("cli-x"));
        Assert.Null(sessions.For("cli-y"));
        Assert.NotNull(sessions.For("cli-a"));
        Assert.Equal(1, _opens);
    }

    /// <summary>
    /// A real record carries blobs of a hundred kilobytes — tool surfaces, prompt
    /// snapshots — and those are skipped, never bound. One of them holds an
    /// <c>isArchived</c> of its own, nested, which must not be taken for the record's.
    /// </summary>
    [Fact]
    public async Task A_record_with_a_large_nested_blob_still_yields_its_own_flags()
    {
        var blob = new StringBuilder("""{"tools":[""");

        for (var index = 0; index < 1500; index++)
        {
            if (index > 0) blob.Append(',');

            blob.Append("{\"name\":\"tool-")
                .Append(index)
                .Append('"')
                .Append(""","inputSchema":{"type":"object","properties":{"isArchived":{"type":"boolean"}}},"isArchived":true,"scheduledTaskId":"not-this-one","cliSessionId":"nor-this"}""");
        }

        blob.Append("]}");
        Assert.True(blob.Length > 100_000);

        GivenRecord(Account, Org, "local_a", $$"""{"sessionId":"local_a","toolSurfaceSnapshot":{{blob}},"cliSessionId":"cli-a","promptAppendSnapshot":[{{blob}}],"isArchived":false,"title":"a title"}""");

        var record = (await ReadAsync()).For("cli-a");

        Assert.NotNull(record);
        Assert.Equal(false, record.Archived);
        Assert.Null(record.ScheduledTask);
        Assert.Null((await ReadAsync()).For("nor-this"));
    }

    /// <summary>A record that is half-written or held open by the desktop app loses
    /// only itself: the reader answers every other record, and does not throw.</summary>
    [Fact]
    public async Task A_malformed_or_locked_record_loses_only_its_own_record()
    {
        GivenRecord(Account, Org, "local_bad", """{"sessionId":"local_bad","cliSessionId":"cli-bad","isArch""");
        GivenRecord(Account, Org, "local_text", "not json at all");
        var locked = GivenRecord(Account, Org, "local_locked", """{"sessionId":"local_locked","cliSessionId":"cli-locked","isArchived":false}""");
        GivenRecord(Account, Org, "local_good", """{"sessionId":"local_good","cliSessionId":"cli-good","isArchived":false}""");

        ClaudeDesktopSessions sessions;

        await using (new FileStream(locked, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            sessions = await ReadAsync();
        }

        Assert.True(sessions.IsOpen("cli-good"));
        Assert.Null(sessions.For("cli-bad"));
        Assert.Null(sessions.For("cli-locked"));
    }

    /// <summary>A locked record is not remembered as absent: once the desktop app lets
    /// go of it, the next read has it.</summary>
    [Fact]
    public async Task A_record_that_was_locked_is_read_once_it_is_released()
    {
        var locked = GivenRecord(Account, Org, "local_locked", """{"sessionId":"local_locked","cliSessionId":"cli-locked","isArchived":false}""");
        var reader = Reader();

        await using (new FileStream(locked, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            Assert.Null((await reader.ReadAsync(TestContext.Current.CancellationToken)).For("cli-locked"));
        }

        Assert.True((await reader.ReadAsync(TestContext.Current.CancellationToken)).IsOpen("cli-locked"));
    }

    /// <summary>A machine without the desktop app has no store, and that is ordinary:
    /// an empty answer rather than an error.</summary>
    [Fact]
    public async Task A_missing_store_is_an_empty_answer()
    {
        var sessions = await ReadAsync();

        Assert.Null(sessions.For("cli-a"));
        Assert.Empty(sessions.OpenIds);
    }

    /// <summary>
    /// The store held 1,388 records and 41 MB on the machine this was built against,
    /// and a refresh re-reads it. A record at the same length and write time is
    /// answered from what the last read found, unopened; one that changed is read
    /// again.
    /// </summary>
    [Fact]
    public async Task An_unchanged_record_is_not_parsed_twice_and_a_changed_one_is()
    {
        GivenRecord(Account, Org, "local_a", """{"sessionId":"local_a","cliSessionId":"cli-a","isArchived":false}""");
        GivenRecord(Account, Org, "local_b", """{"sessionId":"local_b","cliSessionId":"cli-b","isArchived":false}""");
        var reader = Reader();

        _ = await reader.ReadAsync(TestContext.Current.CancellationToken);
        Assert.Equal(2, _opens);

        var second = await reader.ReadAsync(TestContext.Current.CancellationToken);
        Assert.Equal(2, _opens);
        Assert.True(second.IsOpen("cli-a"));

        GivenRecord(Account, Org, "local_b", """{"sessionId":"local_b","cliSessionId":"cli-b","isArchived":true}""");

        var third = await reader.ReadAsync(TestContext.Current.CancellationToken);
        Assert.Equal(3, _opens);
        Assert.Equal(true, third.For("cli-b")!.Archived);
    }

    /// <summary>A record the desktop app deleted is gone from the next answer, and from
    /// the memory: the reader does not keep answering for a file that is not there.</summary>
    [Fact]
    public async Task A_deleted_record_is_pruned()
    {
        var gone = GivenRecord(Account, Org, "local_a", """{"sessionId":"local_a","cliSessionId":"cli-a","isArchived":false}""");
        var reader = Reader();

        Assert.True((await reader.ReadAsync(TestContext.Current.CancellationToken)).IsOpen("cli-a"));

        File.Delete(gone);

        Assert.Null((await reader.ReadAsync(TestContext.Current.CancellationToken)).For("cli-a"));
    }

    /// <summary>Two desktop records can drive one CLI session — a session forked or
    /// moved in the desktop app. The one active more recently speaks for it.</summary>
    [Fact]
    public async Task Of_two_records_naming_one_cli_session_the_more_recently_active_wins()
    {
        GivenRecord(Account, Org, "local_old", """{"sessionId":"local_old","cliSessionId":"cli-a","isArchived":true,"lastActivityAt":1792500000000}""");
        GivenRecord(Account, Org, "local_new", """{"sessionId":"local_new","cliSessionId":"cli-a","isArchived":false,"lastActivityAt":1792600000000}""");

        var record = (await ReadAsync()).For("cli-a");

        Assert.Equal("local_new", record!.DesktopId);
        Assert.Equal(false, record.Archived);
    }

    /// <summary>A live marker names its desktop record by the desktop's own id. Where no
    /// record names the session's CLI id, that id is the fallback; the CLI id is asked
    /// first, because it is the record's own statement of the session it drives.</summary>
    [Fact]
    public async Task A_session_no_record_names_is_found_by_its_host_session()
    {
        GivenRecord(Account, Org, "local_host", """{"sessionId":"local_host","cliSessionId":"cli-earlier","isArchived":false}""");
        GivenRecord(Account, Org, "local_own", """{"sessionId":"local_own","cliSessionId":"cli-own","isArchived":true}""");

        var sessions = await ReadAsync();

        Assert.Equal("local_host", sessions.For("cli-resumed", hostSessionId: "local_host")!.DesktopId);
        Assert.Equal("local_own", sessions.For("cli-own", hostSessionId: "local_host")!.DesktopId);
        Assert.Null(sessions.For("cli-resumed", hostSessionId: "local_missing"));
    }

    private Task<ClaudeDesktopSessions> ReadAsync() => Reader().ReadAsync(TestContext.Current.CancellationToken);

    private ClaudeDesktopSessionReader Reader() =>
        new(_root, new FakeTimeProvider(Noon), path =>
        {
            _opens++;

            return TranscriptTail.OpenShared(path);
        });

    private string GivenRecord(string account, string org, string name, string json)
    {
        var folder = Directory.CreateDirectory(Path.Combine(_root, account, org));
        var path = Path.Combine(folder.FullName, $"{name}.json");
        var existed = File.Exists(path);

        File.WriteAllText(path, json);

        // Settled, as a record nobody is writing is: an hour old on the reader's clock,
        // a second later for a rewrite so the change is visible on the write time too.
        File.SetLastWriteTimeUtc(path, Noon.UtcDateTime.AddHours(-1).AddSeconds(existed ? 1 : 0));

        return path;
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
