using System.Text;

namespace Backlog.Infrastructure.Brain.UnitTests;

/// <summary>
/// The link writer (local ADR 0052) against real note files, each case in LF and
/// in CRLF: the Cross-Linking invariants
/// (<c>.devbook/domain/brain/domain.invariants.md#cross-linking</c>) — only the
/// two lists change, a present value is not added again, an unparsable block is
/// not written — and the archive move that keeps the file's bytes.
/// </summary>
public sealed class BrainNoteLinkWriterTests : IDisposable
{
    private const string TaskId = "7b908425-4208-497e-a978-d3c5b3faa597";

    private readonly string _brain = Path.Combine(Path.GetTempPath(), "brain-link-writer-tests-" + Guid.NewGuid().ToString("N"));

    public BrainNoteLinkWriterTests() => Directory.CreateDirectory(_brain);

    public void Dispose()
    {
        try
        {
            Directory.Delete(_brain, recursive: true);
        }
        catch (IOException)
        {
            // Left for the OS to clear.
        }
    }

    private BrainNoteLinkWriter Writer() => new(new BrainFolderSource(() => _brain));

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <summary>The text in <paramref name="eol"/> endings, whatever endings this
    /// source file was checked out with.</summary>
    private static string Eol(string text, string eol) =>
        text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace("\n", eol, StringComparison.Ordinal);

    private string Note(string relativePath, string text, string eol, bool bom = false)
    {
        var path = Path.Combine([_brain, .. relativePath.Split('/')]);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var bytes = Encoding.UTF8.GetBytes(Eol(text, eol));
        File.WriteAllBytes(path, bom ? [0xEF, 0xBB, 0xBF, .. bytes] : bytes);
        return path;
    }

    private const string Flow = """
        # Sync outbox

        ```meta
        type: note
        status: draft
        tasks: [a1, b2]
        roadmap: []
        # kept: a comment
        ```

        The body, which stays.

        ## Later

        ```meta
        tasks: [z9]
        ```

        ```text
        # Not a heading
        ```

        """;

    private const string Block = """
        # Sync outbox

        ```meta
        type: note
        tasks:
          - a1
          - b2

        roadmap:
        - +release-q4
        source: manual
        ```

        Body.
        """;

    // --- Only the two lists change -------------------------------------------

    [Theory]
    [InlineData("\n")]
    [InlineData("\r\n")]
    public async Task A_task_id_appended_to_a_flow_list_changes_only_that_line(string eol)
    {
        var path = Note("projects/sync.md", Flow, eol);

        var write = await Writer().AppendTaskAsync("projects/sync.md", TaskId, Ct);

        Assert.Equal(BrainLinkOutcome.Appended, write.Outcome);
        Assert.Equal("projects/sync.md", write.RelativePath);
        var expected = Eol(Flow, eol).Replace("tasks: [a1, b2]", $"tasks: [a1, b2, {TaskId}]", StringComparison.Ordinal);
        Assert.Equal(expected, File.ReadAllText(path));
    }

    [Theory]
    [InlineData("\n")]
    [InlineData("\r\n")]
    public async Task A_link_written_beside_a_review_date_never_touches_it(string eol)
    {
        // Local ADR 0054 §6: the app never writes review, not even through this writer.
        const string reviewed = "# Health\n\n```meta\nreview: 2026-11-5\n```\n\nCheck-ups.\n";
        var path = Note("areas/health.md", reviewed, eol);

        await Writer().AppendTaskAsync("areas/health.md", TaskId, Ct);
        await Writer().AppendRoadmapTagAsync("areas/health.md", "health", Ct);

        var expected = Eol(reviewed, eol).Replace(
            "review: 2026-11-5" + eol,
            "review: 2026-11-5" + eol + $"tasks: [{TaskId}]" + eol + "roadmap: [health]" + eol,
            StringComparison.Ordinal);
        Assert.Equal(expected, File.ReadAllText(path));
    }

    [Theory]
    [InlineData("\n")]
    [InlineData("\r\n")]
    public async Task A_roadmap_tag_appended_to_an_empty_flow_list_stays_a_flow_list(string eol)
    {
        var path = Note("projects/sync.md", Flow, eol);

        var write = await Writer().AppendRoadmapTagAsync("projects/sync.md", "+gtd-para", Ct);

        Assert.Equal(BrainLinkOutcome.Appended, write.Outcome);
        var expected = Eol(Flow, eol).Replace("roadmap: []", "roadmap: [+gtd-para]", StringComparison.Ordinal);
        Assert.Equal(expected, File.ReadAllText(path));
    }

    [Theory]
    [InlineData("\n")]
    [InlineData("\r\n")]
    public async Task A_block_list_stays_a_block_list_with_its_own_indentation(string eol)
    {
        var path = Note("projects/sync.md", Block, eol);

        await Writer().AppendTaskAsync("projects/sync.md", TaskId, Ct);
        await Writer().AppendRoadmapTagAsync("projects/sync.md", "+gtd-para", Ct);

        var expected = Eol(Block, eol)
            .Replace(Eol("  - b2\n", eol), Eol($"  - b2\n  - {TaskId}\n", eol), StringComparison.Ordinal)
            .Replace(Eol("- +release-q4\n", eol), Eol("- +release-q4\n- +gtd-para\n", eol), StringComparison.Ordinal);
        Assert.Equal(expected, File.ReadAllText(path));
    }

    [Theory]
    [InlineData("\n")]
    [InlineData("\r\n")]
    public async Task A_missing_field_lands_as_the_last_line_of_the_top_block(string eol)
    {
        const string text = "# Idea\n\n```meta\ntype: note\nstatus: draft\n```\n\nBody.\n";
        var path = Note("_inbox/idea.md", text, eol);

        var write = await Writer().AppendTaskAsync("_inbox/idea.md", TaskId, Ct);

        Assert.Equal(BrainLinkOutcome.Appended, write.Outcome);
        Assert.Equal(Eol($"# Idea\n\n```meta\ntype: note\nstatus: draft\ntasks: [{TaskId}]\n```\n\nBody.\n", eol), File.ReadAllText(path));
    }

    /// <summary>Every byte outside the list is the byte read: the body, the
    /// lower <c>meta</c> block with its own <c>tasks</c>, a fenced <c>#</c> line,
    /// a byte order mark, and a file that ends without a line ending.</summary>
    [Theory]
    [InlineData("\n")]
    [InlineData("\r\n")]
    public async Task Every_byte_outside_the_list_is_kept(string eol)
    {
        var text = Flow.TrimEnd('\n') + "\nNo ending at the end.";
        var path = Note("projects/sync.md", text, eol, bom: true);
        var before = File.ReadAllBytes(path);

        await Writer().AppendTaskAsync("projects/sync.md", "c3", Ct);

        var after = File.ReadAllBytes(path);
        var added = Encoding.UTF8.GetBytes(", c3");
        var at = Encoding.UTF8.GetByteCount(Eol(text, eol)[..Eol(text, eol).IndexOf("b2]", StringComparison.Ordinal)]) + 3 + 2;
        Assert.Equal([.. before[..at], .. added, .. before[at..]], after);
        Assert.Equal(new byte[] { 0xEF, 0xBB, 0xBF }, after[..3]);
        Assert.Contains("tasks: [z9]", Encoding.UTF8.GetString(after), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("\n")]
    [InlineData("\r\n")]
    public async Task A_block_list_append_keeps_every_other_byte_and_the_byte_order_mark(string eol)
    {
        var path = Note("projects/sync.md", Block, eol, bom: true);
        var before = File.ReadAllBytes(path);

        await Writer().AppendTaskAsync("projects/sync.md", "c3", Ct);

        var line = Encoding.UTF8.GetBytes(Eol("  - c3\n", eol));
        var at = 3 + Encoding.UTF8.GetByteCount(Eol(Block, eol)[..(Eol(Block, eol).IndexOf("  - b2", StringComparison.Ordinal) + Eol("  - b2\n", eol).Length)]);
        Assert.Equal([.. before[..at], .. line, .. before[at..]], File.ReadAllBytes(path));
    }

    [Theory]
    [InlineData("\n")]
    [InlineData("\r\n")]
    public async Task A_bare_field_with_no_items_becomes_a_flow_list(string eol)
    {
        var path = Note("_inbox/idea.md", "# Idea\n\n```meta\ntype: note\ntasks:\nstatus: draft\n```\n", eol);

        await Writer().AppendTaskAsync("_inbox/idea.md", "c3", Ct);

        Assert.Equal(Eol("# Idea\n\n```meta\ntype: note\ntasks: [c3]\nstatus: draft\n```\n", eol), File.ReadAllText(path));
    }

    [Theory]
    [InlineData("\n")]
    [InlineData("\r\n")]
    public async Task A_trailing_comma_in_a_flow_list_gets_no_second_comma(string eol)
    {
        var path = Note("_inbox/idea.md", "# Idea\n\n```meta\ntasks: [a1, ]\n```\n", eol);

        await Writer().AppendTaskAsync("_inbox/idea.md", "c3", Ct);

        // The space before the bracket is the person's and stays where it was.
        Assert.Equal(Eol("# Idea\n\n```meta\ntasks: [a1, c3 ]\n```\n", eol), File.ReadAllText(path));
    }

    /// <summary>The field a lower chapter's block carries is that chapter's, not
    /// the note's: the top block gets its own.</summary>
    [Theory]
    [InlineData("\n")]
    [InlineData("\r\n")]
    public async Task A_field_only_a_lower_block_carries_is_added_to_the_top_block(string eol)
    {
        const string text = "# Idea\n\n```meta\ntype: note\n```\n\n## Later\n\n```meta\nroadmap: [+old]\n```\n";
        var path = Note("_inbox/idea.md", text, eol);

        await Writer().AppendRoadmapTagAsync("_inbox/idea.md", "+gtd-para", Ct);

        Assert.Equal(Eol("# Idea\n\n```meta\ntype: note\nroadmap: [+gtd-para]\n```\n\n## Later\n\n```meta\nroadmap: [+old]\n```\n", eol), File.ReadAllText(path));
    }

    [Fact]
    public async Task A_file_with_mixed_line_endings_keeps_each_one()
    {
        const string text = "# Idea\r\n\n```meta\r\ntasks: [a1]\nstatus: draft\r\n```\nBody\r\n";
        var path = Note("_inbox/idea.md", string.Empty, "\n");
        File.WriteAllBytes(path, Encoding.UTF8.GetBytes(text));

        await Writer().AppendTaskAsync("_inbox/idea.md", "c3", Ct);

        Assert.Equal(text.Replace("[a1]", "[a1, c3]", StringComparison.Ordinal), Encoding.UTF8.GetString(File.ReadAllBytes(path)));
    }

    /// <summary>Two links into one note at once — a plan writing its task and its
    /// Roadmap Tag — both land: neither reads the file the other replaces.</summary>
    [Fact]
    public async Task Links_written_at_once_into_one_note_all_land()
    {
        var path = Note("projects/sync.md", Flow, "\n");
        var writer = Writer();

        var writes = await Task.WhenAll(Enumerable.Range(0, 20).Select(i => Task.Run(
            () => i % 2 == 0
                ? writer.AppendTaskAsync("projects/sync.md", $"t{i}", Ct)
                : new BrainNoteLinkWriter(new BrainFolderSource(() => _brain)).AppendRoadmapTagAsync("projects/sync.md", $"+r{i}", Ct),
            Ct)));

        Assert.All(writes, w => Assert.Equal(BrainLinkOutcome.Appended, w.Outcome));
        var text = File.ReadAllText(path);
        foreach (var i in Enumerable.Range(0, 20)) Assert.Contains(i % 2 == 0 ? $"t{i}" : $"+r{i}", text, StringComparison.Ordinal);
    }

    // --- A present value is not added again ----------------------------------

    [Theory]
    [InlineData("\n")]
    [InlineData("\r\n")]
    public async Task A_present_task_id_leaves_the_file_unchanged(string eol)
    {
        var path = Note("projects/sync.md", Flow.Replace("a1", TaskId.ToUpperInvariant(), StringComparison.Ordinal), eol);
        var before = File.ReadAllBytes(path);
        var stamp = File.GetLastWriteTimeUtc(path);

        var write = await Writer().AppendTaskAsync("projects/sync.md", TaskId, Ct);

        Assert.Equal(BrainLinkOutcome.AlreadyPresent, write.Outcome);
        Assert.True(write.Succeeded);
        Assert.Equal(before, File.ReadAllBytes(path));
        Assert.Equal(stamp, File.GetLastWriteTimeUtc(path));
    }

    [Theory]
    [InlineData("\n")]
    [InlineData("\r\n")]
    public async Task A_present_roadmap_tag_in_a_block_list_leaves_the_file_unchanged(string eol)
    {
        var path = Note("projects/sync.md", Block, eol);
        var before = File.ReadAllBytes(path);

        var write = await Writer().AppendRoadmapTagAsync("projects/sync.md", "+release-q4", Ct);

        Assert.Equal(BrainLinkOutcome.AlreadyPresent, write.Outcome);
        Assert.Equal(before, File.ReadAllBytes(path));
    }

    [Theory]
    [InlineData("\n")]
    [InlineData("\r\n")]
    public async Task A_repeated_link_writes_once(string eol)
    {
        var path = Note("projects/sync.md", Flow, eol);

        await Writer().AppendRoadmapTagAsync("projects/sync.md", "+gtd-para", Ct);
        var once = File.ReadAllBytes(path);
        var again = await Writer().AppendRoadmapTagAsync("projects/sync.md", "+gtd-para", Ct);

        Assert.Equal(BrainLinkOutcome.AlreadyPresent, again.Outcome);
        Assert.Equal(once, File.ReadAllBytes(path));
    }

    // --- An unparsable block is not written ----------------------------------

    public static TheoryData<string, string, string> Unparsable()
    {
        var cases = new (string Text, string Reason)[]
        {
            ("Just text.\n", "no title heading"),
            ("# Idea\n\nBody.\n", "no meta block under its title"),
            ("# Idea\n\n```meta\ntype: note\n", "never closed"),
            ("# Idea\n\n```meta\ntype: note\nnot a field\n```\n", "neither a field nor a list item"),
            ("# Idea\n\n```meta\n- stray\ntype: note\n```\n", "under no field"),
            ("# Idea\n\n```meta\ntasks: [a]\ntasks: [b]\n```\n", "twice"),
            ("# Idea\n\n```meta\ntasks: a1\n```\n", "not a list"),
            ("# Idea\n\n```meta\ntasks: [a1,\n  b2]\n```\n", "not closed on its line"),
            ("```text\n# Fenced\n```\n\n```meta\ntype: note\n```\n", "no title heading"),
            ("# First\n\nNo block.\n\n# Second\n\n```meta\ntasks: [a]\n```\n", "no meta block under its title"),
            ("# Idea\n\n```meta\ntasks: [a] # see [x]\n```\n", "holds a bracket"),
        };

        var data = new TheoryData<string, string, string>();
        foreach (var eol in (string[])["\n", "\r\n"])
        {
            foreach (var (text, reason) in cases) data.Add(text, reason, eol);
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(Unparsable))]
    public async Task A_missing_or_unparsable_top_block_is_refused_with_its_reason(string text, string reason, string eol)
    {
        var path = Note("_inbox/idea.md", text, eol);
        var before = File.ReadAllBytes(path);

        var write = await Writer().AppendTaskAsync("_inbox/idea.md", TaskId, Ct);

        Assert.Equal(BrainLinkOutcome.Refused, write.Outcome);
        Assert.False(write.Succeeded);
        Assert.Contains(reason, write.Error, StringComparison.Ordinal);
        Assert.Equal(before, File.ReadAllBytes(path));
        Assert.Empty(Directory.GetFiles(Path.GetDirectoryName(path)!, ".*"));
    }

    [Fact]
    public async Task A_file_that_is_not_utf8_is_refused()
    {
        var path = Path.Combine(_brain, "note.md");
        byte[] bytes = [.. Encoding.ASCII.GetBytes("# Caf"), 0xE9, .. Encoding.ASCII.GetBytes("\n\n```meta\ntype: note\n```\n")];
        File.WriteAllBytes(path, bytes);

        var write = await Writer().AppendTaskAsync("note.md", TaskId, Ct);

        Assert.Equal(BrainLinkOutcome.Refused, write.Outcome);
        Assert.Equal(bytes, File.ReadAllBytes(path));
    }

    [Theory]
    [InlineData("a b")]
    [InlineData("a,b")]
    [InlineData("[a]")]
    [InlineData("'a'")]
    [InlineData(" ")]
    public async Task A_value_that_cannot_sit_in_a_list_is_refused(string value)
    {
        var path = Note("projects/sync.md", Flow, "\n");
        var before = File.ReadAllBytes(path);

        var write = await Writer().AppendRoadmapTagAsync("projects/sync.md", value, Ct);

        Assert.Equal(BrainLinkOutcome.Refused, write.Outcome);
        Assert.Equal(before, File.ReadAllBytes(path));
    }

    [Theory]
    [InlineData("../outside.md")]
    [InlineData("projects/missing.md")]
    [InlineData("projects/sync.txt")]
    [InlineData(".hidden/sync.md")]
    [InlineData("projects/sync.md:stream.md")]
    [InlineData("projects/sy\0nc.md")]
    [InlineData("")]
    public async Task A_path_that_names_no_note_in_the_brain_is_refused(string notePath)
    {
        var note = Note("projects/sync.md", Flow, "\n");
        File.WriteAllText(Path.Combine(_brain, "projects", "sync.txt"), Flow);
        var before = File.ReadAllBytes(note);

        var write = await Writer().AppendTaskAsync(notePath, TaskId, Ct);
        var move = await Writer().ArchiveAsync(notePath, Ct);

        Assert.Equal(BrainLinkOutcome.Refused, write.Outcome);
        Assert.Equal(BrainLinkOutcome.Refused, move.Outcome);
        Assert.False(string.IsNullOrWhiteSpace(write.Error));
        Assert.Equal(before, File.ReadAllBytes(note));
        Assert.Equal(Flow, File.ReadAllText(Path.Combine(_brain, "projects", "sync.txt")));
    }

    [Fact]
    public async Task A_brain_that_is_not_set_up_is_refused_in_the_source_s_words()
    {
        var write = await new BrainNoteLinkWriter(new BrainFolderSource(() => null)).AppendTaskAsync("projects/sync.md", TaskId, Ct);

        Assert.Equal(BrainLinkOutcome.Refused, write.Outcome);
        Assert.Equal(BrainFolderSource.NoBrainFolder, write.Error);
    }

    // --- The archive move -----------------------------------------------------

    [Theory]
    [InlineData("\n")]
    [InlineData("\r\n")]
    public async Task The_archive_move_keeps_the_file_s_bytes(string eol)
    {
        var path = Note("projects/client/sync.md", Block, eol, bom: true);
        Directory.CreateDirectory(Path.Combine(_brain, "archive"));
        var before = File.ReadAllBytes(path);

        var write = await Writer().ArchiveAsync("projects/client/sync.md", Ct);

        Assert.Equal(BrainLinkOutcome.Moved, write.Outcome);
        Assert.Equal("archive/client/sync.md", write.RelativePath);
        Assert.False(File.Exists(path));
        Assert.Equal(before, File.ReadAllBytes(Path.Combine(_brain, "archive", "client", "sync.md")));
    }

    [Fact]
    public async Task The_archive_is_the_area_the_conventions_name_so()
    {
        File.WriteAllText(Path.Combine(_brain, "CONVENTIONS.md"), "## Areas\n\n| Area | Folder |\n|---|---|\n| Projects | work |\n| Archive | done |\n");
        Note("work/sync.md", Flow, "\n");

        var write = await Writer().ArchiveAsync("work/sync.md", Ct);

        Assert.Equal("done/sync.md", write.RelativePath);
        Assert.True(File.Exists(Path.Combine(_brain, "done", "sync.md")));
    }

    [Fact]
    public async Task A_brain_without_an_archive_area_is_refused()
    {
        File.WriteAllText(Path.Combine(_brain, "CONVENTIONS.md"), "## Areas\n\n| Area | Folder |\n|---|---|\n| Projects | projects |\n");
        var path = Note("projects/sync.md", Flow, "\n");

        var write = await Writer().ArchiveAsync("projects/sync.md", Ct);

        Assert.Equal(BrainLinkOutcome.Refused, write.Outcome);
        Assert.Equal(BrainNoteLinkWriter.NoArchive, write.Error);
        Assert.True(File.Exists(path));
    }

    [Fact]
    public async Task A_taken_name_in_the_archive_is_never_overwritten()
    {
        var path = Note("projects/sync.md", Flow, "\n");
        var taken = Note("archive/sync.md", "# Older\n", "\n");

        var write = await Writer().ArchiveAsync("projects/sync.md", Ct);

        Assert.Equal(BrainLinkOutcome.Refused, write.Outcome);
        Assert.True(File.Exists(path));
        Assert.Equal("# Older\n", File.ReadAllText(taken));
    }

    [Fact]
    public async Task A_note_in_the_archive_already_is_not_moved()
    {
        Note("archive/sync.md", Flow, "\n");

        var write = await Writer().ArchiveAsync("archive/sync.md", Ct);

        Assert.Equal(BrainLinkOutcome.Refused, write.Outcome);
        Assert.Contains("archive already", write.Error, StringComparison.Ordinal);
    }

    // --- The source hears about it --------------------------------------------

    [Fact]
    public async Task A_write_tells_the_source_its_content_changed_and_a_present_value_does_not()
    {
        Note("projects/sync.md", Flow, "\n");
        var source = new BrainFolderSource(() => _brain);
        var changes = 0;
        source.Changed += () => changes++;
        var writer = new BrainNoteLinkWriter(source);

        await writer.AppendTaskAsync("projects/sync.md", "c3", Ct);
        await writer.AppendTaskAsync("projects/sync.md", "c3", Ct);

        Assert.Equal(1, changes);
    }
}
