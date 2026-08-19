using System.Text;

namespace Backlog.Desktop.UI.UnitTests;

/// <summary>
/// A knowledge chapter is written back over its own <c>.md</c> file, and what
/// nobody edited comes back exactly as it was.
/// <para>
/// Byte identity is the acceptance criterion that dies quietly: a line-ending
/// flip, a dropped trailing newline or a lost byte-order mark shows up as a
/// whole-file diff in the repository the chapter came from, long after the edit
/// that caused it, and nothing in the app looks wrong at the time. These tests
/// are the only place that notices. The status merge is here for the same reason
/// — it only ever misbehaves when two writes race, which is exactly when nobody
/// is watching.
/// </para>
/// </summary>
public sealed class KnowledgeChapterWriteTests : IDisposable
{
    /// <summary>A layer file as the technology panel deals with one: the chapter's
    /// own fence under the title, and a node's fence under a heading inside it.
    /// The second fence is the one a node's status selector writes to, and the one
    /// a first-fence-only merge would revert.</summary>
    private const string Layered =
        "# Frontend\n\n```meta\nstatus: active\n```\n\nLayer prose.\n\n## Blazor Hybrid\n\n```meta\nstatus: draft\n```\n\nNode prose.\n";

    private readonly List<string> _tempDirs = [];
    private readonly KnowledgeChapterWriter _writer = new();

    [Fact]
    public async Task A_crlf_chapter_stays_crlf()
    {
        var (root, chapter) = Chapter("notes.md", "# Notes\r\n\r\n```meta\r\nstatus: draft\r\n```\r\n\r\nOriginal prose.\r\n");

        await _writer.WriteAsync(chapter, "# Notes\n\n```meta\nstatus: draft\n```\n\nEdited prose.\n", Baseline("# Notes\n\n```meta\nstatus: draft\n```\n"));

        var written = File.ReadAllText(Path.Combine(root, "notes.md"));
        Assert.Contains("Edited prose.", written, StringComparison.Ordinal);
        Assert.DoesNotContain("\n", written.Replace("\r\n", string.Empty, StringComparison.Ordinal), StringComparison.Ordinal);
    }

    [Fact]
    public async Task An_lf_chapter_stays_lf()
    {
        var (root, chapter) = Chapter("notes.md", "# Notes\n\n```meta\nstatus: draft\n```\n\nOriginal prose.\n");

        await _writer.WriteAsync(chapter, "# Notes\r\n\r\n```meta\r\nstatus: draft\r\n```\r\n\r\nEdited prose.\r\n", Baseline("# Notes\n\n```meta\nstatus: draft\n```\n"));

        var written = File.ReadAllText(Path.Combine(root, "notes.md"));
        Assert.Contains("Edited prose.", written, StringComparison.Ordinal);
        Assert.DoesNotContain("\r", written, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_stray_carriage_return_does_not_turn_a_line_feed_chapter_into_a_crlf_one()
    {
        // One line somebody pasted from a Windows editor, in a file that is
        // otherwise line feeds. Sniffing for "any CRLF" would rewrite every line
        // in the file to spare that one, which is the whole-file diff this writer
        // exists to avoid.
        var (root, chapter) = Chapter("notes.md", "# Notes\n\nProse.\r\nMore prose.\n\nEnd.\n");
        var filePath = Path.Combine(root, "notes.md");
        var buffer = File.ReadAllText(filePath);

        await _writer.WriteAsync(chapter, buffer.Replace("End.", "Edited end.", StringComparison.Ordinal), null);

        var written = File.ReadAllText(filePath);
        Assert.Contains("Edited end.", written, StringComparison.Ordinal);
        Assert.DoesNotContain("\r", written, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_stray_line_feed_does_not_turn_a_crlf_chapter_into_an_lf_one()
    {
        var (root, chapter) = Chapter("notes.md", "# Notes\r\n\r\nProse.\nMore prose.\r\n\r\nEnd.\r\n");
        var filePath = Path.Combine(root, "notes.md");
        var buffer = File.ReadAllText(filePath);

        await _writer.WriteAsync(chapter, buffer.Replace("End.", "Edited end.", StringComparison.Ordinal), null);

        var written = File.ReadAllText(filePath);
        Assert.Contains("Edited end.", written, StringComparison.Ordinal);
        Assert.DoesNotContain("\n", written.Replace("\r\n", string.Empty, StringComparison.Ordinal), StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_chapter_with_as_many_of_each_newline_takes_the_one_it_opens_with()
    {
        // A file with no majority has no convention to preserve, so the tie
        // goes to the ending it opens with rather than to whichever the app
        // would have picked on its own.
        var (root, chapter) = Chapter("notes.md", "# Notes\r\nProse.\nEnd.");
        var filePath = Path.Combine(root, "notes.md");

        await _writer.WriteAsync(chapter, File.ReadAllText(filePath).Replace("End.", "Edited end.", StringComparison.Ordinal), null);

        var written = File.ReadAllText(filePath);
        Assert.Contains("Edited end.", written, StringComparison.Ordinal);
        Assert.DoesNotContain("\n", written.Replace("\r\n", string.Empty, StringComparison.Ordinal), StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_chapter_that_ended_with_a_newline_still_does()
    {
        var (root, chapter) = Chapter("notes.md", "# Notes\n\nOriginal prose.\n");

        await _writer.WriteAsync(chapter, "# Notes\n\nEdited prose.", baseline: null);

        Assert.EndsWith("Edited prose.\n", File.ReadAllText(Path.Combine(root, "notes.md")), StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_chapter_that_did_not_end_with_a_newline_does_not_gain_one()
    {
        var (root, chapter) = Chapter("notes.md", "# Notes\n\nOriginal prose.");

        await _writer.WriteAsync(chapter, "# Notes\n\nEdited prose.\n", baseline: null);

        Assert.EndsWith("Edited prose.", File.ReadAllText(Path.Combine(root, "notes.md")), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Trailing_blank_lines_survive_a_chapter_that_ends_with_a_newline()
    {
        var (root, chapter) = Chapter("notes.md", "# Notes\n\nProse.\n\n\n");
        var filePath = Path.Combine(root, "notes.md");
        var before = File.ReadAllBytes(filePath);

        await _writer.WriteAsync(chapter, File.ReadAllText(filePath), null);

        Assert.Equal(before, File.ReadAllBytes(filePath));
    }

    [Fact]
    public async Task Trailing_blank_lines_a_user_typed_survive_a_chapter_that_ends_without_a_newline()
    {
        // Two trailing newlines cannot be the editing surface's habit — somebody
        // pressed Enter twice — so they are kept even though the file's own
        // convention is to have no final newline at all.
        var (root, chapter) = Chapter("notes.md", "# Notes\n\nProse.");

        await _writer.WriteAsync(chapter, "# Notes\n\nEdited prose.\n\n\n", null);

        Assert.Equal("# Notes\n\nEdited prose.\n\n\n", File.ReadAllText(Path.Combine(root, "notes.md")));
    }

    [Fact]
    public async Task A_chapter_without_a_byte_order_mark_does_not_gain_one()
    {
        var (root, chapter) = Chapter("notes.md", "# Notes\n\nOriginal prose.\n");

        await _writer.WriteAsync(chapter, "# Notes\n\nEdited prose.\n", baseline: null);

        var bytes = File.ReadAllBytes(Path.Combine(root, "notes.md"));
        Assert.False(bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF);
    }

    [Fact]
    public async Task A_byte_order_mark_the_chapter_arrived_with_is_written_back()
    {
        var (root, chapter) = Chapter("notes.md", "# Notes\n\nOriginal prose.\n", byteOrderMark: true);

        await _writer.WriteAsync(chapter, "# Notes\n\nEdited prose.\n", null);

        var bytes = File.ReadAllBytes(Path.Combine(root, "notes.md"));
        Assert.Equal(new byte[] { 0xEF, 0xBB, 0xBF }, bytes[..3]);
        Assert.Equal("# Notes\n\nEdited prose.\n", Encoding.UTF8.GetString(bytes[3..]));
    }

    [Fact]
    public async Task Saving_an_untouched_chapter_with_a_byte_order_mark_leaves_the_file_byte_identical()
    {
        var (root, chapter) = Chapter("notes.md", "# Notes\r\n\r\n```meta\r\nstatus: accepted\r\n```\r\n\r\nProse.\r\n", byteOrderMark: true);
        var filePath = Path.Combine(root, "notes.md");
        var before = File.ReadAllBytes(filePath);

        // Reading strips the mark, so the buffer the editor holds has no idea the
        // file had one. Only the writer can know, and only from the bytes.
        var buffer = File.ReadAllText(filePath);
        await _writer.WriteAsync(chapter, buffer, Baseline(buffer));

        Assert.Equal(before, File.ReadAllBytes(filePath));
    }

    [Fact]
    public async Task Saving_an_untouched_chapter_leaves_the_file_byte_identical()
    {
        const string original = "# Notes\r\n\r\n```meta\r\nstatus: accepted\r\nrelated: [\".domain/context-map.md\"]\r\n```\r\n\r\nProse.\r\n";
        var (root, chapter) = Chapter("notes.md", original);
        var filePath = Path.Combine(root, "notes.md");
        var before = File.ReadAllBytes(filePath);

        // What the editing surface holds is what it read, so a save with nothing
        // typed has to be a no-op at the byte level — not merely at the "looks
        // the same" level.
        var buffer = File.ReadAllText(filePath);
        await _writer.WriteAsync(chapter, buffer, Baseline(buffer));

        Assert.Equal(before, File.ReadAllBytes(filePath));
    }

    [Fact]
    public async Task An_untouched_meta_fence_survives_a_body_edit_byte_for_byte()
    {
        const string fence = "```meta\nstatus: accepted\nowner: docs\nrelated: [\".arc42/08-crosscutting-concepts.md\"]\n```";
        var (root, chapter) = Chapter("notes.md", $"# Notes\n\n{fence}\n\nOriginal prose.\n");

        await _writer.WriteAsync(chapter, $"# Notes\n\n{fence}\n\nEdited prose.\n", Baseline($"# Notes\n\n{fence}\n"));

        var written = File.ReadAllText(Path.Combine(root, "notes.md"));
        Assert.Contains(fence, written, StringComparison.Ordinal);
        Assert.Contains("Edited prose.", written, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_relative_path_that_climbs_out_of_the_root_is_refused()
    {
        var root = TempDir();
        Directory.CreateDirectory(Path.Combine(root, "area"));
        File.WriteAllText(Path.Combine(root, "outside.md"), "# Outside\n");
        var escaping = new KnowledgeChapterRef("arc42", Path.Combine(root, "area"), "../outside.md");

        await Assert.ThrowsAsync<InvalidOperationException>(() => _writer.WriteAsync(escaping, "# Rewritten\n", null));

        Assert.Equal("# Outside\n", File.ReadAllText(Path.Combine(root, "outside.md")));
    }

    [Fact]
    public async Task An_absolute_path_dressed_as_a_relative_one_is_refused()
    {
        var root = TempDir();
        Directory.CreateDirectory(Path.Combine(root, "area"));
        var outside = Path.Combine(root, "outside.md");
        File.WriteAllText(outside, "# Outside\n");
        var escaping = new KnowledgeChapterRef("arc42", Path.Combine(root, "area"), outside);

        await Assert.ThrowsAsync<InvalidOperationException>(() => _writer.WriteAsync(escaping, "# Rewritten\n", null));

        Assert.Equal("# Outside\n", File.ReadAllText(outside));
    }

    [Fact]
    public async Task A_chapter_file_that_is_not_there_is_refused()
    {
        var root = TempDir();
        var missing = new KnowledgeChapterRef("arc42", root, "gone.md");

        await Assert.ThrowsAsync<FileNotFoundException>(() => _writer.WriteAsync(missing, "# Gone\n", null));
    }

    [Fact]
    public async Task A_status_changed_on_disk_wins_over_the_stale_one_in_the_buffer()
    {
        var (root, chapter) = Chapter("notes.md", "# Notes\n\n```meta\nstatus: draft\n```\n\nOriginal prose.\n");
        var filePath = Path.Combine(root, "notes.md");

        // The status selector wrote while the body debounce was pending, so the
        // buffer still carries the status it was loaded with.
        File.WriteAllText(filePath, "# Notes\n\n```meta\nstatus: active\n```\n\nOriginal prose.\n");

        var result = await _writer.WriteAsync(chapter, "# Notes\n\n```meta\nstatus: draft\n```\n\nEdited prose.\n", Baseline("# Notes\n\n```meta\nstatus: draft\n```\n"));

        var written = File.ReadAllText(filePath);
        Assert.Contains("status: active", written, StringComparison.Ordinal);
        Assert.DoesNotContain("status: draft", written, StringComparison.Ordinal);
        Assert.Contains("Edited prose.", written, StringComparison.Ordinal);
        Assert.Equal("active", result.Status.For("notes"));
    }

    [Fact]
    public async Task A_status_typed_into_the_raw_markdown_wins_over_the_one_on_disk()
    {
        var (root, chapter) = Chapter("notes.md", "# Notes\n\n```meta\nstatus: draft\n```\n\nOriginal prose.\n");
        var filePath = Path.Combine(root, "notes.md");
        File.WriteAllText(filePath, "# Notes\n\n```meta\nstatus: active\n```\n\nOriginal prose.\n");

        // Editing status: in the markdown is a deliberate edit, and the merge is
        // symmetric precisely so a blunt "disk always wins" cannot silently
        // discard it.
        var result = await _writer.WriteAsync(chapter, "# Notes\n\n```meta\nstatus: accepted\n```\n\nEdited prose.\n", Baseline("# Notes\n\n```meta\nstatus: draft\n```\n"));

        var written = File.ReadAllText(filePath);
        Assert.Contains("status: accepted", written, StringComparison.Ordinal);
        Assert.DoesNotContain("status: active", written, StringComparison.Ordinal);
        Assert.Equal("accepted", result.Status.For("notes"));
    }

    [Fact]
    public async Task A_status_changed_on_disk_in_a_later_fence_is_not_reverted_by_a_save()
    {
        // The technology panel's node selector writes under the node's heading,
        // not the chapter's, and the editor's buffer still holds that node's old
        // status. A merge that only reconciled the first fence in the file would
        // undo the node's change on the next keystroke.
        var (root, chapter) = Chapter("frontend.md", Layered);
        var filePath = Path.Combine(root, "frontend.md");
        var baseline = Baseline(Layered);

        File.WriteAllText(filePath, Layered.Replace("status: draft", "status: active", StringComparison.Ordinal));

        var result = await _writer.WriteAsync(chapter, Layered.Replace("Node prose.", "Edited node prose.", StringComparison.Ordinal), baseline);

        var written = File.ReadAllText(filePath);
        Assert.Contains("## Blazor Hybrid\n\n```meta\nstatus: active\n```", written, StringComparison.Ordinal);
        Assert.Contains("Edited node prose.", written, StringComparison.Ordinal);
        Assert.Equal("active", result.Status.For("blazor-hybrid"));

        // And the chapter's own fence, which nobody touched, is still what it was.
        Assert.Equal("active", result.Status.For("frontend"));
    }

    [Fact]
    public async Task A_status_typed_into_a_later_fence_in_the_raw_markdown_wins_over_the_one_on_disk()
    {
        var (root, chapter) = Chapter("frontend.md", Layered);
        var filePath = Path.Combine(root, "frontend.md");
        var baseline = Baseline(Layered);

        File.WriteAllText(filePath, Layered.Replace("status: draft", "status: active", StringComparison.Ordinal));

        var result = await _writer.WriteAsync(chapter, Layered.Replace("status: draft", "status: accepted", StringComparison.Ordinal), baseline);

        var written = File.ReadAllText(filePath);
        Assert.Contains("## Blazor Hybrid\n\n```meta\nstatus: accepted\n```", written, StringComparison.Ordinal);
        Assert.DoesNotContain("status: active\n```\n\nNode prose.", written, StringComparison.Ordinal);
        Assert.Equal("accepted", result.Status.For("blazor-hybrid"));
    }

    [Fact]
    public async Task A_status_written_under_a_heading_the_buffer_has_renamed_is_dropped_rather_than_moved_to_another_section()
    {
        // The status write landed on "## Blazor Hybrid" while the buffer was
        // renaming that heading. Its anchor is gone, and a status change is worth
        // less than the certainty that it belongs to the section it was made on,
        // so it is not carried over to the new name — nor to any other fence.
        var (root, chapter) = Chapter("frontend.md", Layered);
        var filePath = Path.Combine(root, "frontend.md");
        var baseline = Baseline(Layered);

        File.WriteAllText(filePath, Layered.Replace("status: draft", "status: active", StringComparison.Ordinal));

        var renamed = Layered.Replace("## Blazor Hybrid", "## Blazor Hybrid Shell", StringComparison.Ordinal);
        var result = await _writer.WriteAsync(chapter, renamed, baseline);

        var written = File.ReadAllText(filePath);
        Assert.Contains("## Blazor Hybrid Shell\n\n```meta\nstatus: draft\n```", written, StringComparison.Ordinal);
        Assert.DoesNotContain("status: active\n```\n\nNode prose.", written, StringComparison.Ordinal);
        Assert.Equal("draft", result.Status.For("blazor-hybrid-shell"));
        Assert.Null(result.Status.For("blazor-hybrid"));
    }

    [Fact]
    public async Task A_status_first_written_on_disk_is_merged_into_a_buffer_that_has_no_meta_fence()
    {
        var (root, chapter) = Chapter("notes.md", "# Notes\n\nOriginal prose.\n");
        var filePath = Path.Combine(root, "notes.md");
        File.WriteAllText(filePath, "# Notes\n\n```meta\nstatus: active\n```\n\nOriginal prose.\n");

        var result = await _writer.WriteAsync(chapter, "# Notes\n\nEdited prose.\n", baseline: null);

        var written = File.ReadAllText(filePath);
        Assert.Contains("```meta", written, StringComparison.Ordinal);
        Assert.Contains("status: active", written, StringComparison.Ordinal);
        Assert.Contains("Edited prose.", written, StringComparison.Ordinal);
        Assert.Equal("active", result.Status.For("notes"));
    }

    [Fact]
    public async Task A_meta_fence_the_user_deleted_is_not_put_back_by_the_merge()
    {
        // Deleting the fence in the markdown is an edit like any other: the text
        // no longer carries the baseline, so disk does not win.
        var (root, chapter) = Chapter("notes.md", "# Notes\n\n```meta\nstatus: draft\n```\n\nOriginal prose.\n");
        var filePath = Path.Combine(root, "notes.md");
        var baseline = Baseline("# Notes\n\n```meta\nstatus: draft\n```\n");

        File.WriteAllText(filePath, "# Notes\n\n```meta\nstatus: active\n```\n\nOriginal prose.\n");

        await _writer.WriteAsync(chapter, "# Notes\n\nEdited prose.\n", baseline);

        var written = File.ReadAllText(filePath);
        Assert.DoesNotContain("```meta", written, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_write_leaves_no_temporary_sibling_behind()
    {
        var (root, chapter) = Chapter("notes.md", "# Notes\n\nOriginal prose.\n");

        await _writer.WriteAsync(chapter, "# Notes\n\nEdited prose.\n", baseline: null);

        var files = Directory.EnumerateFiles(root).Select(path => Path.GetFileName(path)!).Order().ToArray();
        Assert.Equal(["notes.md"], files);
    }

    [Fact]
    public async Task The_written_text_is_handed_back_so_the_buffer_and_the_file_agree()
    {
        var (root, chapter) = Chapter("notes.md", "# Notes\r\n\r\n```meta\r\nstatus: draft\r\n```\r\n\r\nOriginal prose.\r\n");

        var result = await _writer.WriteAsync(chapter, "# Notes\n\n```meta\nstatus: draft\n```\n\nEdited prose.\n", Baseline("# Notes\n\n```meta\nstatus: draft\n```\n"));

        Assert.Equal(File.ReadAllText(Path.Combine(root, "notes.md")), result.Text);
    }

    [Fact]
    public void The_status_reader_keys_every_fence_by_the_heading_that_owns_it()
    {
        var statuses = KnowledgeChapterStatus.Read(Layered);

        Assert.Equal("active", statuses.For("frontend"));
        Assert.Equal("draft", statuses.For("blazor-hybrid"));

        // The chapter's own status is still a first-class question — it is simply
        // the first fence rather than the only one that is read.
        Assert.Equal("active", statuses.Chapter);
        Assert.Null(statuses.For("no-such-heading"));
        Assert.True(KnowledgeChapterStatus.Read("# Notes\n\nNo meta here.\n").IsEmpty);
    }

    [Fact]
    public async Task Utf8_content_survives_the_round_trip()
    {
        var (root, chapter) = Chapter("notes.md", "# Notes\n\nSchépers — ünïcode ✓\n");

        var buffer = File.ReadAllText(Path.Combine(root, "notes.md"));
        await _writer.WriteAsync(chapter, buffer + "More ✓\n", baseline: null);

        var written = File.ReadAllText(Path.Combine(root, "notes.md"), Encoding.UTF8);
        Assert.Contains("Schépers — ünïcode ✓", written, StringComparison.Ordinal);
        Assert.Contains("More ✓", written, StringComparison.Ordinal);
    }

    public void Dispose()
    {
        foreach (var dir in _tempDirs.Where(Directory.Exists))
        {
            try { Directory.Delete(dir, recursive: true); } catch (IOException) { }
        }
    }

    /// <summary>The baseline as the editing surface takes it: read out of the
    /// buffer at the moment the buffer was loaded.</summary>
    private static KnowledgeChapterStatus Baseline(string buffer) => KnowledgeChapterStatus.Read(buffer);

    /// <summary>A chapter file in its own knowledge root, plus the ref that names
    /// it — the pairing every one of these tests starts from. The text goes out
    /// through an explicit encoding rather than the default one, because whether
    /// the file carries a byte-order mark is part of what is under test.</summary>
    private (string Root, KnowledgeChapterRef Chapter) Chapter(string relativePath, string markdown, bool byteOrderMark = false)
    {
        var root = TempDir();
        var filePath = Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);
        File.WriteAllText(filePath, markdown, new UTF8Encoding(encoderShouldEmitUTF8Identifier: byteOrderMark));

        return (root, new KnowledgeChapterRef("arc42", root, relativePath));
    }

    private string TempDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), "knowledge-chapter-write-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        _tempDirs.Add(dir);
        return dir;
    }
}
