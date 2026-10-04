namespace Backlog.Desktop.UI.UnitTests;

/// <summary>
/// The one path comparison every Devbook panel selects a chapter by.
/// <para>
/// There used to be five private copies, and the instructions panel's had diverged
/// on purpose: an area panel reads <c>.arc42/05.md</c> and <c>arc42/05.md</c> as one
/// chapter, because the root layout spells its folders with a dot and the menu
/// names a chapter without one, while an instruction source lives in a dot folder —
/// <c>.github</c>, <c>.claude</c> — that is a different folder from the same name
/// without it. Both rules are kept, as two methods, and both are pinned here.
/// </para>
/// </summary>
public sealed class DevbookPathTests
{
    [Theory]
    [InlineData(@".arc42\adr\0001-decision.md", "arc42/adr/0001-decision.md")]
    [InlineData("./arc42/05.md", "arc42/05.md")]
    [InlineData("/arc42/05.md", "arc42/05.md")]
    [InlineData(".arc42/05.md", "arc42/05.md")]
    [InlineData("arc42/05.md", "arc42/05.md")]
    [InlineData("", "")]
    public void Normalize_uses_forward_slashes_and_drops_any_leading_dot_or_slash(string path, string expected)
    {
        Assert.Equal(expected, DevbookPath.Normalize(path));
    }

    [Fact]
    public void Normalize_reads_null_as_no_path()
    {
        Assert.Equal(string.Empty, DevbookPath.Normalize(null));
        Assert.Equal(string.Empty, DevbookPath.NormalizeKeepingDotFolder(null));
    }

    [Theory]
    [InlineData(@".github\copilot-instructions.md", ".github/copilot-instructions.md")]
    [InlineData("./.github/copilot-instructions.md", ".github/copilot-instructions.md")]
    [InlineData("/.claude/CLAUDE.md", ".claude/CLAUDE.md")]
    [InlineData("././AGENTS.md", "AGENTS.md")]
    [InlineData("AGENTS.md", "AGENTS.md")]
    public void NormalizeKeepingDotFolder_trims_a_leading_dot_slash_and_slash_but_keeps_a_dot_folder(string path, string expected)
    {
        Assert.Equal(expected, DevbookPath.NormalizeKeepingDotFolder(path));
    }

    [Theory]
    [InlineData(".arc42/05-building-blocks.md", ".arc42/05-building-blocks.md")]
    [InlineData(".arc42/05-building-blocks.md", "arc42/05-building-blocks.md")]
    [InlineData(@".arc42\adr\0001-decision.md", ".arc42/adr/0001-decision.md")]
    [InlineData(".devbook/arc42/adr/0001-decision.md", "adr/0001-decision.md")]
    [InlineData(".devbook/arc42/adr/0001-decision.md", "./adr/0001-decision.md")]
    [InlineData(".domain/inbox/domain.md", "INBOX/Domain.md")]
    public void A_chapter_matches_itself_its_folder_less_spelling_and_any_suffix_on_a_segment(string candidate, string selected)
    {
        Assert.True(DevbookPath.Matches(candidate, selected));
    }

    [Theory]
    [InlineData(".arc42/adr/0001-decision.md", "0001-decision")]
    [InlineData(".arc42/adr/0001-decision.md", "r/0001-decision.md")]
    [InlineData(".arc42/adr/0001-decision.md", ".arc42/adr/0002-other.md")]
    [InlineData(".arc42/adr/0001-decision.md", "")]
    public void A_chapter_does_not_match_a_partial_segment_or_another_chapter(string candidate, string selected)
    {
        Assert.False(DevbookPath.Matches(candidate, selected));
    }

    /// <summary>
    /// The case the instructions panel kept its own copy for. With the dot dropped,
    /// <c>.github/copilot-instructions.md</c> and <c>github/copilot-instructions.md</c>
    /// would be one selection; keeping it, the selection picks its own document out of
    /// a repository that has both, and nothing else.
    /// </summary>
    [Fact]
    public void A_dot_folder_instruction_source_matches_only_its_own_document()
    {
        string[] documents =
        [
            @".github\copilot-instructions.md",
            "github/copilot-instructions.md",
            ".claude/copilot-instructions.md",
            "docs/github/copilot-instructions.md"
        ];

        var selected = documents.Where(document =>
            DevbookPath.MatchesKeepingDotFolder(document, ".github/copilot-instructions.md")).ToList();

        Assert.Equal([@".github\copilot-instructions.md"], selected);

        // The area rule would not have told the first two apart, which is why it is
        // not the rule for instruction sources.
        Assert.True(DevbookPath.Matches("github/copilot-instructions.md", ".github/copilot-instructions.md"));
    }

    [Fact]
    public void An_instruction_source_still_matches_by_suffix_and_by_its_trimmed_spelling()
    {
        Assert.True(DevbookPath.MatchesKeepingDotFolder(".github/instructions/naming.instructions.md", "instructions/naming.instructions.md"));
        Assert.True(DevbookPath.MatchesKeepingDotFolder(".github/copilot-instructions.md", "./.github/copilot-instructions.md"));
        Assert.True(DevbookPath.MatchesKeepingDotFolder(@".claude\rules\naming.md", "/.claude/rules/naming.md"));
        Assert.False(DevbookPath.MatchesKeepingDotFolder(".claude/rules/naming.md", "aming.md"));
    }
}
