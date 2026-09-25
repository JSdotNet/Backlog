using Backlog.UI.Components.Devbook;

namespace Backlog.Desktop.UI.UnitTests;

/// <summary>
/// The three contract-16 rules the status writer applies on its way to the file,
/// each of which depends on the folder it reads off the prefix it is handed.
///
/// <para>The resting value in <c>arc42/</c>, <c>domain/</c> and <c>design/</c> is
/// spelled by omitting the field, so writing it deletes the line. A decision rung
/// outside <c>domain/</c> is not in that folder's vocabulary, so it is refused.
/// And in <c>domain/</c>, a write that moves a chapter off a rung deletes the
/// record that no longer stands in the same write — the rule reports a record
/// left behind as loudly as a status nobody recognises. The chapter shapes are
/// the rule text's: a <c>features.md</c> chapter of <c>type: feature</c>, with
/// the record fields spelled as <c>devbook-chapter-metadata.md</c> defines
/// them.</para>
/// </summary>
public sealed class DevbookStatusWriteRulesTests : IDisposable
{
    /// <summary>An approved-then-accepted domain chapter, carrying both records
    /// and a sibling fence with fields of the same names, which no write here may
    /// touch.</summary>
    private const string Accepted = """
        # Inbox Features

        ```meta
        type: features
        ```

        ## Inbox Capture

        ```meta
        type: feature
        status: accepted
        approved-by: jobsc
        approved-at: 2026-09-01
        approved-hash: sha256:0a1b2c3d
        accepted-by: jobsc
        accepted-at: 2026-09-20
        accepted-hash: sha256:0a1b2c3d
        related: [.devbook/domain/inbox/context.md]
        ```

        Capture text.

        ## Inbox Triage

        ```meta
        type: feature
        status: accepted
        approved-by: someone
        approved-at: 2026-08-01
        accepted-by: someone
        accepted-at: 2026-08-02
        ```

        Triage text.
        """;

    private readonly List<string> _roots = [];

    [Theory]
    [InlineData(".arc42/", "arc42", "04-solution-strategy.md")]
    [InlineData(".devbook/arc42/", "arc42", "04-solution-strategy.md")]
    [InlineData(".domain/", "domain", "features.md")]
    [InlineData(".design/", "design", "color-scheme.md")]
    public void Writing_the_resting_value_in_a_resting_folder_deletes_the_line_and_keeps_the_fence(string prefix, string area, string file)
    {
        var (root, path) = Write(area, file, "# Title\n\n```meta\nstatus: draft\n```\n\nText.\n");

        DevbookMarkdownStatusWriter.UpdateStatus(root, prefix + file, prefix, "Active");

        Assert.Equal("# Title\n\n```meta\n```\n\nText.\n", File.ReadAllText(path));
    }

    [Fact]
    public void Writing_the_resting_value_where_none_is_stated_leaves_the_file_untouched()
    {
        const string original = "# Title\n\n```meta\n```\n\nText.\n";
        var (root, path) = Write("arc42", "01-introduction.md", original);
        var before = File.GetLastWriteTimeUtc(path);

        DevbookMarkdownStatusWriter.UpdateStatus(root, ".arc42/01-introduction.md", ".arc42/", "active");

        Assert.Equal(original, File.ReadAllText(path));
        Assert.Equal(before, File.GetLastWriteTimeUtc(path));
    }

    [Theory]
    [InlineData(".tech/", "tech", "shared.md")]
    [InlineData(".ai/", "ai", "adoption-map.md")]
    public void A_rating_folder_has_no_resting_value_so_active_is_written_like_any_word(string prefix, string area, string file)
    {
        // Not a word either folder defines — the writer is not the vocabulary's
        // judge — but it is not the resting value there, so it is not a removal.
        var (root, path) = Write(area, file, "# Title\n\n```meta\nstatus: trial\n```\n");

        DevbookMarkdownStatusWriter.UpdateStatus(root, prefix + file, prefix, "active");

        Assert.Contains("status: active", File.ReadAllText(path), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(".arc42/", "arc42", "approved")]
    [InlineData(".devbook/arc42/", "arc42", "accepted")]
    [InlineData(".design/", "design", "approved")]
    [InlineData(".tech/", "tech", "Accepted")]
    [InlineData(".ai/", "ai", "approved")]
    public void A_decision_rung_outside_domain_is_refused_and_the_file_left_alone(string prefix, string area, string rung)
    {
        const string original = "# Title\n\n```meta\nstatus: draft\n```\n";
        var (root, path) = Write(area, "page.md", original);

        Assert.Throws<ArgumentException>(() => DevbookMarkdownStatusWriter.UpdateStatus(root, prefix + "page.md", prefix, rung));

        Assert.Equal(original, File.ReadAllText(path));
    }

    [Fact]
    public void Domain_takes_a_decision_rung()
    {
        var (root, path) = Write("domain", "features.md", "# Title\n\n```meta\nstatus: draft\n```\n");

        DevbookMarkdownStatusWriter.UpdateStatus(root, ".domain/features.md", ".domain/", "approved");

        Assert.Contains("status: approved", File.ReadAllText(path), StringComparison.Ordinal);
    }

    [Fact]
    public void Moving_from_accepted_to_approved_deletes_the_acceptance_record_and_keeps_the_approval()
    {
        var (root, path) = Write("domain", "features.md", Accepted);

        DevbookMarkdownStatusWriter.UpdateStatus(root, ".domain/features.md#inbox-capture", ".domain/", "approved");

        var capture = Fence(File.ReadAllText(path), "## Inbox Capture");
        Assert.Equal(
            ["type: feature", "status: approved", "approved-by: jobsc", "approved-at: 2026-09-01", "approved-hash: sha256:0a1b2c3d", "related: [.devbook/domain/inbox/context.md]"],
            capture);
    }

    [Theory]
    [InlineData("draft")]
    [InlineData("deprecated")]
    public void Leaving_the_rungs_for_an_ordinary_status_deletes_all_six_record_fields(string status)
    {
        var (root, path) = Write("domain", "features.md", Accepted);

        DevbookMarkdownStatusWriter.UpdateStatus(root, ".domain/features.md#inbox-capture", ".domain/", status);

        Assert.Equal(
            ["type: feature", $"status: {status}", "related: [.devbook/domain/inbox/context.md]"],
            Fence(File.ReadAllText(path), "## Inbox Capture"));
    }

    [Fact]
    public void Leaving_the_rungs_for_the_resting_value_deletes_the_status_and_all_six_record_fields()
    {
        var (root, path) = Write("domain", "features.md", Accepted);

        DevbookMarkdownStatusWriter.UpdateStatus(root, ".domain/features.md#inbox-capture", ".domain/", "active");

        Assert.Equal(
            ["type: feature", "related: [.devbook/domain/inbox/context.md]"],
            Fence(File.ReadAllText(path), "## Inbox Capture"));
    }

    [Fact]
    public void Removing_the_status_of_an_approved_chapter_deletes_the_approval_record_with_it()
    {
        var approved = Accepted
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace("status: accepted\napproved-by: jobsc", "status: approved\napproved-by: jobsc", StringComparison.Ordinal);
        var (root, path) = Write("domain", "features.md", approved);

        DevbookMarkdownStatusWriter.RemoveStatus(root, ".devbook/domain/features.md#inbox-capture", ".domain/");

        Assert.Equal(
            ["type: feature", "related: [.devbook/domain/inbox/context.md]"],
            Fence(File.ReadAllText(path), "## Inbox Capture"));
    }

    [Fact]
    public void Only_the_addressed_headings_fence_is_touched()
    {
        var (root, path) = Write("domain", "features.md", Accepted);

        DevbookMarkdownStatusWriter.UpdateStatus(root, ".domain/features.md#inbox-capture", ".domain/", "draft");

        var text = File.ReadAllText(path);
        Assert.Equal(
            ["type: feature", "status: accepted", "approved-by: someone", "approved-at: 2026-08-01", "accepted-by: someone", "accepted-at: 2026-08-02"],
            Fence(text, "## Inbox Triage"));
        Assert.Equal(["type: features"], Fence(text, "# Inbox Features"));
    }

    [Fact]
    public void Writing_a_rung_in_domain_ends_the_review_that_led_to_it()
    {
        // "Approval deletes all three in the same change that writes the rung."
        var (root, path) = Write("domain", "features.md", """
            # Title

            ```meta
            status: draft
            review: cleared
            reviewer: jobsc
            review-at: 2026-09-01
            ```
            """);

        DevbookMarkdownStatusWriter.UpdateStatus(root, ".domain/features.md", ".domain/", "approved");

        Assert.Equal(["status: approved"], Fence(File.ReadAllText(path), "# Title"));
    }

    [Fact]
    public void Outside_domain_the_record_fields_are_not_the_writers_to_touch()
    {
        // They are violations there, and reported as such; a status write is not
        // the place to tidy them away silently.
        var (root, path) = Write("arc42", "09-decisions.md", "# Title\n\n```meta\nstatus: draft\napproved-by: jobsc\n```\n");

        DevbookMarkdownStatusWriter.UpdateStatus(root, ".arc42/09-decisions.md", ".arc42/", "proposed");

        Assert.Equal(["status: proposed", "approved-by: jobsc"], Fence(File.ReadAllText(path), "# Title"));
    }

    [Theory]
    [InlineData("accepted", new string[0])]
    [InlineData("approved", new[] { "accepted-by", "accepted-at", "accepted-hash" })]
    [InlineData("draft", new[] { "approved-by", "approved-at", "approved-hash", "accepted-by", "accepted-at", "accepted-hash" })]
    [InlineData(null, new[] { "approved-by", "approved-at", "approved-hash", "accepted-by", "accepted-at", "accepted-hash" })]
    public void The_record_that_no_longer_stands_is_decided_by_the_status_it_would_stand_beside(string? status, string[] expected)
    {
        Assert.Equal(expected, DevbookMarkdownStatusWriter.RecordFieldsThatNoLongerStand(status));
        Assert.Equal(DevbookSchema.DecisionRecordFields, DevbookMarkdownStatusWriter.RecordFieldsThatNoLongerStand("proposed"));
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
    private (string Root, string Path) Write(string area, string file, string content)
    {
        var root = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "devbook-status-rules-tests", Guid.NewGuid().ToString("N"), area);
        Directory.CreateDirectory(root);
        _roots.Add(System.IO.Path.GetDirectoryName(root)!);

        var path = System.IO.Path.Combine(root, file);
        File.WriteAllText(path, content.Replace("\r\n", "\n", StringComparison.Ordinal));
        return (root, path);
    }
}
