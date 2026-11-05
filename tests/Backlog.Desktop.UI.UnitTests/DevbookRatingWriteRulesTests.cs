using Backlog.SharedKernel.Devbook;

namespace Backlog.Desktop.UI.UnitTests;

/// <summary>
/// The Markdown rating writer: the one write into a chapter's <c>meta</c> fence
/// that outlived the status writer.
///
/// <para>Under devbook's branch-true decision a chapter's workflow state is kept
/// by the tool rather than in git, so the product writes no <c>status</c> into
/// any folder. What <c>tech/</c> and <c>ai/</c> rate is not workflow state but
/// where a technology stands on the adoption ladder, how a team uses an AI
/// practice — and a rating stays in the Markdown, under the field name
/// <c>rating</c> since devbook contract 38 renamed it and rejects <c>status</c>
/// there. A legacy <c>status</c> line is renamed in place as it is written. So the
/// writer takes those two folders and refuses every other one before the file is
/// opened.</para>
/// </summary>
public sealed class DevbookRatingWriteRulesTests : IDisposable
{
    private readonly List<string> _roots = [];

    [Theory]
    [InlineData(".tech/", "tech", "shared.md")]
    [InlineData(".devbook/tech/", "tech", "shared.md")]
    [InlineData(".ai/", "ai", "adoption-map.md")]
    public void A_rating_folder_takes_a_rating(string prefix, string area, string file)
    {
        var (root, path) = Write(area, file, "# Title\n\n```meta\nrating: trial\n```\n");

        DevbookMarkdownRatingWriter.UpdateRating(root, prefix + file, prefix, "adopt");

        Assert.Equal(["rating: adopt"], Fence(File.ReadAllText(path), "# Title"));
    }

    /// <summary>A fence written before contract 38 states its rating as
    /// <c>status</c>, which the contract rejects in both folders: the write renames
    /// that line in place, keeping its position and indent.</summary>
    [Theory]
    [InlineData(".tech/", "tech", "shared.md")]
    [InlineData(".devbook/ai/", "ai", "adoption-map.md")]
    public void A_legacy_status_line_is_renamed_to_rating_in_place(string prefix, string area, string file)
    {
        var (root, path) = Write(area, file, "# Title\n\n```meta\nstatus: trial\ntype: library\n```\n");

        DevbookMarkdownRatingWriter.UpdateRating(root, prefix + file, prefix, "adopt");

        var text = File.ReadAllText(path);
        Assert.Equal(["rating: adopt", "type: library"], Fence(text, "# Title"));
        Assert.DoesNotContain("status:", text, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(".tech/", "tech", "shared.md")]
    [InlineData(".ai/", "ai", "adoption-map.md")]
    public void A_rating_folder_has_no_resting_value_so_active_is_written_like_any_word(string prefix, string area, string file)
    {
        // Not a word either folder defines — the writer is not the vocabulary's
        // judge — but it is not a resting value there, so it is not a removal.
        var (root, path) = Write(area, file, "# Title\n\n```meta\nrating: trial\n```\n");

        DevbookMarkdownRatingWriter.UpdateRating(root, prefix + file, prefix, "active");

        Assert.Contains("rating: active", File.ReadAllText(path), StringComparison.Ordinal);
    }

    /// <summary>A repository that has migrated under contract 38 writes
    /// <c>rating</c>; the writer updates the field the fence already carries
    /// rather than adding a second one.</summary>
    [Theory]
    [InlineData(".tech/", "tech", "shared.md")]
    [InlineData(".ai/", "ai", "adoption-map.md")]
    public void A_fence_that_carries_rating_has_its_rating_updated(string prefix, string area, string file)
    {
        var (root, path) = Write(area, file, "# Title\n\n```meta\ntype: library\nrating: trial\n```\n");

        DevbookMarkdownRatingWriter.UpdateRating(root, prefix + file, prefix, "adopt");

        Assert.Equal(["type: library", "rating: adopt"], Fence(File.ReadAllText(path), "# Title"));
    }

    [Theory]
    [InlineData(".tech/", "tech", "shared.md")]
    [InlineData(".devbook/ai/", "ai", "adoption-map.md")]
    public void A_fence_with_neither_field_gets_rating(string prefix, string area, string file)
    {
        var (root, path) = Write(area, file, "# Title\n\n```meta\ntype: library\n```\n");

        DevbookMarkdownRatingWriter.UpdateRating(root, prefix + file, prefix, "adopt");

        Assert.Equal(["rating: adopt", "type: library"], Fence(File.ReadAllText(path), "# Title"));
    }

    [Fact]
    public void A_heading_with_no_fence_gets_one_stating_rating()
    {
        var (root, path) = Write("tech", "shared.md", "# Shared\n\nText.\n\n## Blazor\n\nNode text.\n");

        DevbookMarkdownRatingWriter.UpdateRating(root, ".tech/shared.md#blazor", ".tech/", "adopt");

        Assert.Equal(["rating: adopt"], Fence(File.ReadAllText(path), "## Blazor"));
    }

    /// <summary>A fence carrying both spellings — one a migration left half done
    /// — has its <c>rating</c> updated and its legacy <c>status</c> removed, which
    /// contract 38 rejects in both folders.</summary>
    [Fact]
    public void A_fence_with_both_fields_has_its_rating_updated_and_its_status_removed()
    {
        var (root, path) = Write("tech", "shared.md", "# Title\n\n```meta\nstatus: trial\ntype: library\nrating: trial\n```\n\nText.\n");

        DevbookMarkdownRatingWriter.UpdateRating(root, ".tech/shared.md", ".tech/", "adopt");

        var text = File.ReadAllText(path);
        Assert.Equal(["type: library", "rating: adopt"], Fence(text, "# Title"));
        Assert.DoesNotContain("status:", text, StringComparison.Ordinal);
    }

    /// <summary>Only a top-level field is the fence's: a <c>status</c> key nested
    /// under another field is that field's value, and survives a rating change.</summary>
    [Fact]
    public void A_nested_status_key_is_left_alone()
    {
        var (root, path) = Write("tech", "shared.md", "# Title\n\n```meta\nrating: trial\next.tool:\n  status: green\n  - status: item\n```\n\nText.\n");

        DevbookMarkdownRatingWriter.UpdateRating(root, ".tech/shared.md", ".tech/", "adopt");

        var text = File.ReadAllText(path);
        Assert.Equal(["rating: adopt", "ext.tool:", "status: green", "- status: item"], Fence(text, "# Title"));
        Assert.Contains("\n  status: green\n", text.Replace("\r\n", "\n", StringComparison.Ordinal), StringComparison.Ordinal);
    }

    /// <summary>A CRLF file stays CRLF through the rename, every line of it.</summary>
    [Fact]
    public void A_crlf_file_keeps_its_line_endings()
    {
        var (root, path) = WriteVerbatim("tech", "shared.md", "# Title\r\n\r\n```meta\r\nstatus: trial\r\ntype: library\r\n```\r\n\r\nText.\r\n");

        DevbookMarkdownRatingWriter.UpdateRating(root, ".tech/shared.md", ".tech/", "adopt");

        Assert.Equal(
            "# Title\r\n\r\n```meta\r\nrating: adopt\r\ntype: library\r\n```\r\n\r\nText.\r\n",
            File.ReadAllText(path));
    }

    [Theory]
    [InlineData(".tech/", "tech", "Accepted")]
    [InlineData(".ai/", "ai", "approved")]
    public void A_decision_rung_is_refused_and_the_file_left_alone(string prefix, string area, string rung)
    {
        const string original = "# Title\n\n```meta\nstatus: trial\n```\n";
        var (root, path) = Write(area, "page.md", original);

        Assert.Throws<ArgumentException>(() => DevbookMarkdownRatingWriter.UpdateRating(root, prefix + "page.md", prefix, rung));

        Assert.Equal(original, File.ReadAllText(path));
    }

    /// <summary>The retirement itself: a folder whose <c>status</c> is workflow
    /// state is no longer the writer's to touch, whichever spelling the prefix
    /// takes and whatever word is asked for.</summary>
    [Theory]
    [InlineData(".arc42/", "arc42", "proposed")]
    [InlineData(".devbook/arc42/", "arc42", "superseded")]
    [InlineData(".domain/", "domain", "draft")]
    [InlineData(".devbook/domain/", "domain", "approved")]
    [InlineData(".design/", "design", "deprecated")]
    public void A_folder_that_keeps_no_rating_is_refused_and_the_file_left_alone(string prefix, string area, string status)
    {
        const string original = "# Title\n\n```meta\nstatus: draft\n```\n";
        var (root, path) = Write(area, "page.md", original);

        Assert.Throws<ArgumentException>(() => DevbookMarkdownRatingWriter.UpdateRating(root, prefix + "page.md", prefix, status));

        Assert.Equal(original, File.ReadAllText(path));
    }

    [Fact]
    public void Only_the_addressed_headings_fence_is_touched()
    {
        var (root, path) = Write("tech", "shared.md", """
            # Shared

            ```meta
            type: layer
            ```

            ## Blazor

            ```meta
            rating: trial
            related: [.devbook/tech/runtime.md]
            ```

            ## Bunit

            ```meta
            rating: trial
            ```
            """);

        DevbookMarkdownRatingWriter.UpdateRating(root, ".tech/shared.md#blazor", ".tech/", "adopt");

        var text = File.ReadAllText(path);
        Assert.Equal(["rating: adopt", "related: [.devbook/tech/runtime.md]"], Fence(text, "## Blazor"));
        Assert.Equal(["rating: trial"], Fence(text, "## Bunit"));
        Assert.Equal(["type: layer"], Fence(text, "# Shared"));
    }

    /// <summary>The decision-record rule the chapter editor's merge still asks,
    /// moved with the shared fence helpers when the status writer went.</summary>
    [Theory]
    [InlineData("accepted", new string[0])]
    [InlineData("approved", new[] { "accepted-by", "accepted-at", "accepted-hash" })]
    [InlineData("draft", new[] { "approved-by", "approved-at", "approved-hash", "accepted-by", "accepted-at", "accepted-hash" })]
    [InlineData(null, new[] { "approved-by", "approved-at", "approved-hash", "accepted-by", "accepted-at", "accepted-hash" })]
    public void The_record_that_no_longer_stands_is_decided_by_the_status_it_would_stand_beside(string? status, string[] expected)
    {
        Assert.Equal(expected, DevbookHeadingDocument.RecordFieldsThatNoLongerStand(status));
        Assert.Equal(DevbookSchema.DecisionRecordFields, DevbookHeadingDocument.RecordFieldsThatNoLongerStand("proposed"));
    }

    public void Dispose()
    {
        foreach (var root in _roots.Where(Directory.Exists))
        {
            try { Directory.Delete(root, recursive: true); } catch (IOException) { }
        }
    }

    /// <summary>The lines inside the <c>meta</c> fence directly under a heading,
    /// trimmed.</summary>
    private static string[] Fence(string text, string heading)
    {
        var lines = text.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        var at = Array.IndexOf(lines, heading);
        Assert.True(at >= 0, $"Heading '{heading}' is not in the file.");

        var open = Array.FindIndex(lines, at + 1, line => line.Trim() == "```meta");
        var close = Array.FindIndex(lines, open + 1, line => line.TrimStart().StartsWith("```", StringComparison.Ordinal));

        return [.. lines[(open + 1)..close].Select(line => line.Trim())];
    }

    /// <summary>A file in a folder root, which is what the stores hand the writer:
    /// the item path's prefix is stripped against it.</summary>
    private (string Root, string Path) Write(string area, string file, string content) =>
        WriteVerbatim(area, file, content.Replace("\r\n", "\n", StringComparison.Ordinal));

    /// <summary>A file written byte for byte, line endings included.</summary>
    private (string Root, string Path) WriteVerbatim(string area, string file, string content)
    {
        var root = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "devbook-rating-rules-tests", Guid.NewGuid().ToString("N"), area);
        Directory.CreateDirectory(root);
        _roots.Add(System.IO.Path.GetDirectoryName(root)!);

        var path = System.IO.Path.Combine(root, file);
        File.WriteAllText(path, content);
        return (root, path);
    }
}
