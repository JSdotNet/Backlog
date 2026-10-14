using System.Text;
using Backlog.Infrastructure.Sessions;

namespace Backlog.Infrastructure.Sessions.UnitTests;

/// <summary>
/// The line reader both transcript passes resume through: whole lines only, an offset
/// counted in bytes, and a mark that is thrown away the moment the file stops being
/// the one it was taken on.
/// <para>
/// Tested on its own as well as through the readers, because two readers rest on it
/// and each asserts only the facts it folds; what a line is, where the next read
/// starts and when it starts over are this type's promises, and a second reader built
/// on it should not have to rediscover them.
/// </para>
/// </summary>
public sealed class TranscriptTailTests : IDisposable
{
    private readonly string _folder = Path.Combine(
        Path.GetTempPath(),
        "backlog-transcript-tail-tests",
        Guid.NewGuid().ToString("n"));

    private string PathOf(string name) => Path.Combine(_folder, name);

    public TranscriptTailTests() => Directory.CreateDirectory(_folder);

    [Fact]
    public async Task Whole_lines_are_read_with_their_line_ends_trimmed_and_the_offset_is_in_bytes()
    {
        var path = Given("whole.jsonl", "{\"a\":\"café\"}\r\n{\"b\":2}\n");

        var (lines, pass, resumed) = await ReadAsync(path, mark: null);

        Assert.Equal(["{\"a\":\"café\"}", "{\"b\":2}"], lines);
        Assert.False(resumed);
        Assert.Equal(new FileInfo(path).Length, pass.Mark.Offset);
        Assert.True(pass.Whole);
    }

    /// <summary>A line still being written is not a line yet. It is left where it is,
    /// the offset stops in front of it, and the next read picks it up whole.</summary>
    [Fact]
    public async Task An_unterminated_last_line_is_left_for_the_next_read()
    {
        var path = Given("fragment.jsonl", "{\"a\":1}\n{\"b\":");

        var (first, pass, _) = await ReadAsync(path, mark: null);

        Assert.Equal(["{\"a\":1}"], first);
        Assert.Equal(8, pass.Mark.Offset);
        Assert.False(pass.Whole);

        File.AppendAllText(path, "2}\n");

        var (second, next, resumed) = await ReadAsync(path, pass.Mark);

        Assert.True(resumed);
        Assert.Equal(["{\"b\":2}"], second);
        Assert.True(next.Whole);
    }

    /// <summary>A file its writer has finished with, ending in a line it never
    /// terminated. A last line that is whole JSON is not still being written: it is the
    /// file's last line, read as one, and the pass reaches the length.</summary>
    [Fact]
    public async Task A_settled_files_unterminated_last_line_that_is_whole_json_is_its_last_line()
    {
        var path = Given("finished.jsonl", "{\"a\":1}\n{\"b\":2}");

        var (lines, pass, _) = await ReadAsync(path, mark: null, settled: true);

        Assert.Equal(["{\"a\":1}", "{\"b\":2}"], lines);
        Assert.Equal(new FileInfo(path).Length, pass.Mark.Offset);
        Assert.True(pass.Whole);
    }

    /// <summary>Half a line is half a line however long ago it was written: it is left
    /// where it is, and the offset stops in front of it.</summary>
    [Fact]
    public async Task A_settled_files_unterminated_last_line_that_is_not_whole_json_is_left_where_it_is()
    {
        var path = Given("cut.jsonl", "{\"a\":1}\n{\"b\":");

        var (lines, pass, _) = await ReadAsync(path, mark: null, settled: true);

        Assert.Equal(["{\"a\":1}"], lines);
        Assert.Equal(8, pass.Mark.Offset);
        Assert.False(pass.Whole);
    }

    /// <summary>The writer closes the line it left open, later. That newline ends the
    /// line already read rather than being an empty line of its own, so the lines
    /// handed over are the ones a read of the whole file hands over.</summary>
    [Fact]
    public async Task A_newline_closing_a_last_line_already_read_is_not_an_empty_line()
    {
        var path = Given("closed.jsonl", "{\"a\":1}\n{\"b\":2}");
        var (_, pass, _) = await ReadAsync(path, mark: null, settled: true);

        File.AppendAllText(path, "\r\n{\"c\":3}\n");

        var (lines, next, resumed) = await ReadAsync(path, pass.Mark, settled: true);
        var (whole, _, _) = await ReadAsync(path, mark: null);

        Assert.True(resumed);
        Assert.Equal(["{\"c\":3}"], lines);
        Assert.Equal(["{\"a\":1}", "{\"b\":2}", "{\"c\":3}"], whole);
        Assert.Equal(new FileInfo(path).Length, next.Mark.Offset);
    }

    /// <summary>One that is still being written is left for the next read even when its
    /// last line happens to parse: an object can be closed and still have the newline
    /// to come.</summary>
    [Fact]
    public async Task An_unsettled_files_unterminated_last_line_is_left_for_the_next_read_even_when_it_parses()
    {
        var path = Given("writing.jsonl", "{\"a\":1}\n{\"b\":2}");

        var (lines, pass, _) = await ReadAsync(path, mark: null, settled: false);

        Assert.Equal(["{\"a\":1}"], lines);
        Assert.Equal(8, pass.Mark.Offset);
        Assert.False(pass.Whole);
    }

    [Fact]
    public async Task A_read_from_a_mark_reads_only_what_was_appended()
    {
        var path = Given("grown.jsonl", "{\"a\":1}\n{\"b\":2}\n");

        var (_, pass, _) = await ReadAsync(path, mark: null);

        File.AppendAllText(path, "{\"c\":3}\n");

        var (lines, next, resumed) = await ReadAsync(path, pass.Mark);

        Assert.True(resumed);
        Assert.Equal(["{\"c\":3}"], lines);
        Assert.Equal(new FileInfo(path).Length, next.Mark.Offset);
    }

    /// <summary>StreamReader dropped a UTF-8 byte order mark before the first line, and
    /// a first line that kept it would fail every reader's <c>'{'</c> test.</summary>
    [Fact]
    public async Task A_byte_order_mark_is_not_part_of_the_first_line()
    {
        var path = PathOf("bom.jsonl");
        File.WriteAllBytes(path, [0xEF, 0xBB, 0xBF, .. Encoding.UTF8.GetBytes("{\"a\":1}\n")]);

        var (lines, pass, _) = await ReadAsync(path, mark: null);

        Assert.Equal(["{\"a\":1}"], lines);
        Assert.Equal(11, pass.Mark.Offset);
    }

    [Fact]
    public async Task A_line_longer_than_one_read_is_still_one_line()
    {
        var wide = "{\"a\":\"" + new string('x', 300_000) + "\"}";
        var path = Given("long.jsonl", wide + "\n{\"b\":2}\n");

        var (lines, _, _) = await ReadAsync(path, mark: null);

        Assert.Equal([wide, "{\"b\":2}"], lines);
    }

    /// <summary>The read stops at the length the walk saw, so what is remembered
    /// against that length is exactly what that length held.</summary>
    [Fact]
    public async Task The_read_stops_at_the_length_the_file_had_when_it_was_walked()
    {
        var path = Given("racing.jsonl", "{\"a\":1}\n");
        var walked = new FileInfo(path);
        _ = walked.Length;

        File.AppendAllText(path, "{\"b\":2}\n");

        var lines = new List<string>();
        using var tail = await TranscriptTail.OpenAsync(TranscriptTail.OpenShared, walked, null, TestContext.Current.CancellationToken);
        var pass = await tail.ReadAsync(lines.Add, settled: false, TestContext.Current.CancellationToken);

        Assert.Equal(["{\"a\":1}"], lines);
        Assert.Equal(8, pass.Mark.Offset);
        Assert.True(pass.Whole);
    }

    // --- When the mark no longer holds -------------------------------------------

    [Fact]
    public async Task A_file_shorter_than_the_mark_is_read_from_the_start()
    {
        var path = Given("truncated.jsonl", "{\"a\":1}\n{\"b\":2}\n");
        var (_, pass, _) = await ReadAsync(path, mark: null);

        Rewrite(path, "{\"c\":3}\n");

        var (lines, _, resumed) = await ReadAsync(path, pass.Mark);

        Assert.False(resumed);
        Assert.Equal(["{\"c\":3}"], lines);
    }

    [Fact]
    public async Task A_file_whose_opening_bytes_changed_is_read_from_the_start()
    {
        var path = Given("rewritten.jsonl", "{\"a\":1}\n");
        var (_, pass, _) = await ReadAsync(path, mark: null);

        Rewrite(path, "{\"z\":1}\n{\"b\":2}\n");

        var (lines, _, resumed) = await ReadAsync(path, pass.Mark);

        Assert.False(resumed);
        Assert.Equal(["{\"z\":1}", "{\"b\":2}"], lines);
    }

    [Fact]
    public async Task A_file_created_again_is_read_from_the_start()
    {
        var path = Given("recreated.jsonl", "{\"a\":1}\n");
        var (_, pass, _) = await ReadAsync(path, mark: null);

        File.AppendAllText(path, "{\"b\":2}\n");
        File.SetCreationTimeUtc(path, new FileInfo(path).CreationTimeUtc.AddMinutes(5));

        var (lines, _, resumed) = await ReadAsync(path, pass.Mark);

        Assert.False(resumed);
        Assert.Equal(["{\"a\":1}", "{\"b\":2}"], lines);
    }

    /// <summary>A file first read when it was a few bytes long is fingerprinted over
    /// more of itself as it grows, rather than over those few bytes for ever.</summary>
    [Fact]
    public async Task The_fingerprint_grows_with_the_file_up_to_its_cap()
    {
        var path = Given("small.jsonl", "{\"a\":1}\n");
        var (_, first, _) = await ReadAsync(path, mark: null);

        File.AppendAllText(path, "{\"b\":2}\n");
        var (_, second, _) = await ReadAsync(path, first.Mark);

        Assert.Equal(16, second.Mark.Prefix.Length);

        // The second line changes, inside the grown fingerprint and behind the mark.
        Rewrite(path, "{\"a\":1}\n{\"b\":9}\n{\"c\":3}\n");

        var (lines, _, resumed) = await ReadAsync(path, second.Mark);

        Assert.False(resumed);
        Assert.Equal(3, lines.Count);
    }

    [Fact]
    public async Task The_fingerprint_stops_at_its_cap()
    {
        var path = Given("capped.jsonl", "{\"a\":\"" + new string('x', TranscriptTail.PrefixBytes * 2) + "\"}\n");

        var (_, pass, _) = await ReadAsync(path, mark: null);

        Assert.Equal(TranscriptTail.PrefixBytes, pass.Mark.Prefix.Length);
    }

    private static async Task<(List<string> Lines, TranscriptPass Pass, bool Resumed)> ReadAsync(string path, TranscriptMark? mark, bool settled = false)
    {
        var lines = new List<string>();

        using var tail = await TranscriptTail.OpenAsync(TranscriptTail.OpenShared, new FileInfo(path), mark, TestContext.Current.CancellationToken);
        var pass = await tail.ReadAsync(lines.Add, settled, TestContext.Current.CancellationToken);

        return (lines, pass, tail.Resumes);
    }

    private string Given(string name, string content)
    {
        var path = PathOf(name);
        File.WriteAllText(path, content);

        return path;
    }

    /// <summary>The content replaced in place, keeping the creation time, so the only
    /// thing that changed is what the file holds.</summary>
    private static void Rewrite(string path, string content)
    {
        var created = new FileInfo(path).CreationTimeUtc;
        File.WriteAllText(path, content);
        File.SetCreationTimeUtc(path, created);
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
}
