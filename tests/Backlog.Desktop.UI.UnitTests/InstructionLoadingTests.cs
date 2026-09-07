namespace Backlog.Desktop.UI.UnitTests;

/// <summary>
/// The rules in <see cref="InstructionLoading"/> are claims about two products
/// this repository does not ship, so every fact one of them encodes is asserted
/// here rather than left to the reading of whoever wrote it. A rule that is
/// wrong about a host is a wrong row on screen, and a wrong row looks exactly as
/// confident as a right one.
/// </summary>
public sealed class InstructionLoadingTests
{
    [Fact]
    public void Each_host_reads_its_own_root_file_and_not_the_others()
    {
        var rows = InstructionLoading.Compare(
        [
            Document("CLAUDE.md", "# Claude"),
            Document(".github/copilot-instructions.md", "# Copilot")
        ]);

        var claudeFile = Row(rows, "CLAUDE.md");
        Assert.Equal(InstructionReach.Always, claudeFile.Claude.Reach);
        Assert.Equal(InstructionReach.NotRead, claudeFile.Copilot.Reach);

        var copilotFile = Row(rows, ".github/copilot-instructions.md");
        Assert.Equal(InstructionReach.Always, copilotFile.Copilot.Reach);
        Assert.Equal(InstructionReach.NotRead, copilotFile.Claude.Reach);
    }

    [Fact]
    public void A_repository_wide_apply_to_is_always_and_a_narrower_one_is_on_match()
    {
        var rows = InstructionLoading.Compare(
        [
            Document(".github/instructions/mcp-usage.instructions.md", Frontmatter("applyTo: \"**\"")),
            Document(".github/instructions/ui-components.instructions.md", Frontmatter("applyTo: \"src/App/**,src/Modules/**\""))
        ]);

        Assert.Equal(InstructionReach.Always, Row(rows, ".github/instructions/mcp-usage.instructions.md").Copilot.Reach);

        var scoped = Row(rows, ".github/instructions/ui-components.instructions.md").Copilot;
        Assert.Equal(InstructionReach.OnMatch, scoped.Reach);
        Assert.Equal("src/App/**, src/Modules/**", scoped.Detail);
    }

    [Fact]
    public void An_instruction_file_named_in_prose_by_an_always_file_is_linked_not_loaded()
    {
        var rows = InstructionLoading.Compare(
        [
            Document("CLAUDE.md", "See `.github/instructions/ui-components.instructions.md` for the full rule."),
            Document(".github/instructions/ui-components.instructions.md", Frontmatter("applyTo: \"src/App/**\""))
        ]);

        Assert.Equal(InstructionReach.Linked, Row(rows, ".github/instructions/ui-components.instructions.md").Claude.Reach);
    }

    /// <summary>
    /// The case the view exists to find: auto-loaded for one host and unreachable
    /// for the other, because the file the other host does read never names it.
    /// This repository had exactly one of these when the view was written.
    /// </summary>
    [Fact]
    public void A_file_no_always_file_names_is_not_read_and_reads_as_one_sided()
    {
        var rows = InstructionLoading.Compare(
        [
            Document("CLAUDE.md", "Further guidance: `.github/instructions/mcp-usage.instructions.md`."),
            Document(".github/instructions/naming.instructions.md", Frontmatter("applyTo: \"**\""))
        ]);

        var naming = Row(rows, ".github/instructions/naming.instructions.md");
        Assert.Equal(InstructionReach.Always, naming.Copilot.Reach);
        Assert.Equal(InstructionReach.NotRead, naming.Claude.Reach);
        Assert.True(naming.IsOneSided);
    }

    [Fact]
    public void An_at_import_from_an_always_file_is_loaded_and_a_prose_mention_is_not()
    {
        var rows = InstructionLoading.Compare(
        [
            Document("CLAUDE.md", "@docs/imported.md\n\nSee docs/mentioned.md for the rest."),
            Document("docs/imported.md", "# Imported"),
            Document("docs/mentioned.md", "# Mentioned")
        ]);

        Assert.Equal(InstructionReach.Always, Row(rows, "docs/imported.md").Claude.Reach);
        Assert.Equal(InstructionReach.Linked, Row(rows, "docs/mentioned.md").Claude.Reach);
    }

    [Fact]
    public void An_at_import_inside_backticks_is_prose_and_not_an_import()
    {
        var rows = InstructionLoading.Compare(
        [
            Document("CLAUDE.md", "Write `@docs/quoted.md` to import it."),
            Document("docs/quoted.md", "# Quoted")
        ]);

        Assert.NotEqual(InstructionReach.Always, Row(rows, "docs/quoted.md").Claude.Reach);
    }

    [Fact]
    public void At_imports_stop_after_four_hops()
    {
        var rows = InstructionLoading.Compare(
        [
            Document("CLAUDE.md", "@docs/one.md"),
            Document("docs/one.md", "@docs/two.md"),
            Document("docs/two.md", "@docs/three.md"),
            Document("docs/three.md", "@docs/four.md"),
            Document("docs/four.md", "@docs/five.md"),
            Document("docs/five.md", "# Too far")
        ]);

        Assert.Equal(InstructionReach.Always, Row(rows, "docs/four.md").Claude.Reach);
        Assert.NotEqual(InstructionReach.Always, Row(rows, "docs/five.md").Claude.Reach);
    }

    [Fact]
    public void A_rule_is_always_without_paths_and_on_match_with_them()
    {
        var rows = InstructionLoading.Compare(
        [
            Document(".claude/rules/style.md", "# Style"),
            Document(".claude/rules/typescript.md", Frontmatter("paths: \"src/**/*.ts\""))
        ]);

        Assert.Equal(InstructionReach.Always, Row(rows, ".claude/rules/style.md").Claude.Reach);

        var scoped = Row(rows, ".claude/rules/typescript.md").Claude;
        Assert.Equal(InstructionReach.OnMatch, scoped.Reach);
        Assert.Equal("src/**/*.ts", scoped.Detail);
    }

    [Fact]
    public void Agents_files_are_copilots_by_directory_and_claude_does_not_read_them()
    {
        var rows = InstructionLoading.Compare(
        [
            Document("AGENTS.md", "# Agents"),
            Document("src/AGENTS.md", "# Nested")
        ]);

        var nested = Row(rows, "src/AGENTS.md");
        Assert.Equal(InstructionReach.OnMatch, nested.Copilot.Reach);
        Assert.Equal("src/**", nested.Copilot.Detail);
        Assert.Equal(InstructionReach.NotRead, nested.Claude.Reach);

        Assert.Equal("**", Row(rows, "AGENTS.md").Copilot.Detail);
    }

    [Fact]
    public void A_nested_claude_file_is_on_match_and_the_root_one_is_always()
    {
        var rows = InstructionLoading.Compare(
        [
            Document("CLAUDE.md", "# Root"),
            Document("src/Modules/CLAUDE.md", "# Nested")
        ]);

        Assert.Equal(InstructionReach.Always, Row(rows, "CLAUDE.md").Claude.Reach);

        var nested = Row(rows, "src/Modules/CLAUDE.md").Claude;
        Assert.Equal(InstructionReach.OnMatch, nested.Reach);
        Assert.Equal("src/Modules/**", nested.Detail);
    }

    [Fact]
    public void The_claude_folders_own_project_file_is_read_every_session()
    {
        var rows = InstructionLoading.Compare([Document(".claude/CLAUDE.md", "# Project")]);

        Assert.Equal(InstructionReach.Always, Row(rows, ".claude/CLAUDE.md").Claude.Reach);
    }

    [Fact]
    public void A_claude_workspace_file_that_is_not_a_rule_is_not_instructions()
    {
        var rows = InstructionLoading.Compare(
        [
            Document("CLAUDE.md", "# Root"),
            Document(".claude/commands/release.md", "# Release")
        ]);

        Assert.Equal(InstructionReach.NotRead, Row(rows, ".claude/commands/release.md").Claude.Reach);
    }

    [Fact]
    public void Rows_keep_the_order_they_were_discovered_in()
    {
        var rows = InstructionLoading.Compare(
        [
            Document(".github/copilot-instructions.md", "# Copilot"),
            Document("CLAUDE.md", "# Claude"),
            Document("AGENTS.md", "# Agents")
        ]);

        Assert.Equal(
            [".github/copilot-instructions.md", "CLAUDE.md", "AGENTS.md"],
            rows.Select(row => Normalize(row.Document.RelativePath)));
    }

    [Fact]
    public void An_empty_set_compares_to_nothing_rather_than_throwing() =>
        Assert.Empty(InstructionLoading.Compare([]));

    /// <summary>A document as discovery hands one over: the path in the separator
    /// the file system used, which on Windows is not the one the rules are
    /// written in.</summary>
    private static InstructionDocument Document(string relativePath, string content) =>
        new(
            Path.GetFileName(relativePath),
            relativePath.Replace('/', Path.DirectorySeparatorChar),
            relativePath.StartsWith(".github", StringComparison.Ordinal) ? "GitHub Copilot" : "Claude Code",
            "Test document",
            content,
            content.Length,
            DateTime.UtcNow);

    private static string Frontmatter(string field) =>
        $"---\n{field}\ndescription: A test file.\n---\n\n# Heading\n";

    private static string Normalize(string path) => path.Replace(Path.DirectorySeparatorChar, '/');

    private static InstructionLoadingRow Row(IReadOnlyList<InstructionLoadingRow> rows, string relativePath) =>
        Assert.Single(
            rows,
            row => string.Equals(Normalize(row.Document.RelativePath), relativePath, StringComparison.Ordinal));
}
