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
    /// <summary>Not symmetrical: Claude Code reads only its own root file, while
    /// Copilot reads its own and, as agent instructions, the root
    /// <c>CLAUDE.md</c> too.</summary>
    [Fact]
    public void Claude_reads_only_its_root_file_and_copilot_reads_both()
    {
        var rows = Rows(
        [
            Document("CLAUDE.md", "# Claude"),
            Document(".github/copilot-instructions.md", "# Copilot")
        ]);

        var claudeFile = Row(rows, "CLAUDE.md");
        Assert.Equal(InstructionReach.Always, claudeFile.Claude.Reach);
        Assert.Equal(InstructionReach.Always, claudeFile.Copilot.Reach);

        var copilotFile = Row(rows, ".github/copilot-instructions.md");
        Assert.Equal(InstructionReach.Always, copilotFile.Copilot.Reach);
        Assert.Equal(InstructionReach.NotRead, copilotFile.Claude.Reach);
    }

    [Fact]
    public void A_repository_wide_apply_to_is_always_and_a_narrower_one_is_on_match()
    {
        var rows = Rows(
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
        var rows = Rows(
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
        var rows = Rows(
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
        var rows = Rows(
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
        var rows = Rows(
        [
            Document("CLAUDE.md", "Write `@docs/quoted.md` to import it."),
            Document("docs/quoted.md", "# Quoted")
        ]);

        Assert.NotEqual(InstructionReach.Always, Row(rows, "docs/quoted.md").Claude.Reach);
    }

    [Fact]
    public void At_imports_stop_after_four_hops()
    {
        var rows = Rows(
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
        var rows = Rows(
        [
            Document(".claude/rules/style.md", "# Style"),
            Document(".claude/rules/typescript.md", Frontmatter("paths: \"src/**/*.ts\""))
        ]);

        Assert.Equal(InstructionReach.Always, Row(rows, ".claude/rules/style.md").Claude.Reach);

        var scoped = Row(rows, ".claude/rules/typescript.md").Claude;
        Assert.Equal(InstructionReach.OnMatch, scoped.Reach);
        Assert.Equal("src/**/*.ts", scoped.Detail);
    }

    /// <summary>
    /// The root file is Copilot's every session; a nested one is read for the
    /// files under it. Claude Code reads neither — its documentation says so in
    /// as many words, and points at an <c>@AGENTS.md</c> import instead.
    /// <para>The root file used to read as "on match, <c>**</c>", which the totals
    /// count as nothing until a path is picked, and a repository whose whole
    /// Copilot setup is an <c>AGENTS.md</c> showed Copilot carrying 0 KB.</para>
    /// </summary>
    [Fact]
    public void The_root_agents_file_is_copilots_every_session_and_a_nested_one_by_directory()
    {
        var comparison = InstructionLoading.Compare(
        [
            Document("AGENTS.md", "# Agents\n"),
            Document("src/AGENTS.md", "# Nested")
        ]);
        var rows = comparison.Rows;

        var root = Row(rows, "AGENTS.md");
        Assert.Equal(InstructionReach.Always, root.Copilot.Reach);
        Assert.True(root.Copilot.Loaded);
        Assert.Equal(InstructionReach.NotRead, root.Claude.Reach);

        var nested = Row(rows, "src/AGENTS.md");
        Assert.Equal(InstructionReach.OnMatch, nested.Copilot.Reach);
        Assert.Equal("src/**", nested.Copilot.Detail);
        Assert.Equal(InstructionReach.NotRead, nested.Claude.Reach);

        Assert.Equal(1, comparison.Copilot.Files);
        Assert.Equal(9, comparison.Copilot.Bytes);
    }

    /// <summary>
    /// Copilot's documentation offers "a single CLAUDE.md or GEMINI.md file
    /// stored in the root of the repository" as the alternative to AGENTS.md.
    /// Read as an alternative: it stands in when there is no root AGENTS.md, and
    /// steps aside when there is one.
    /// </summary>
    [Fact]
    public void A_root_claude_file_stands_in_as_copilots_agent_instructions_when_there_is_no_agents_file()
    {
        var alone = Rows([Document("CLAUDE.md", "# Claude")]);
        Assert.Equal(InstructionReach.Always, Row(alone, "CLAUDE.md").Copilot.Reach);
        Assert.Equal("as agent instructions", Row(alone, "CLAUDE.md").Copilot.Detail);

        var beside = Rows(
        [
            Document("CLAUDE.md", "# Claude"),
            Document("AGENTS.md", "# Agents")
        ]);
        Assert.Equal(InstructionReach.NotRead, Row(beside, "CLAUDE.md").Copilot.Reach);

        // Only the root file; a nested CLAUDE.md is Claude's alone.
        var nested = Rows([Document("src/CLAUDE.md", "# Nested")]);
        Assert.Equal(InstructionReach.NotRead, Row(nested, "src/CLAUDE.md").Copilot.Reach);
    }

    /// <summary>
    /// The shape a repository takes when every folder's conventions live in an
    /// <c>AGENTS.md</c> and a one-line <c>CLAUDE.md</c> beside it imports them:
    /// the import is read when the importer is, so it inherits the importer's
    /// scope rather than reading as unreachable.
    /// </summary>
    [Fact]
    public void An_import_from_a_scoped_claude_file_is_loaded_with_that_files_scope()
    {
        var documents = new[]
        {
            Document("src/backend/CLAUDE.md", "@AGENTS.md"),
            Document("src/backend/AGENTS.md", "# Backend")
        };

        var imported = Row(InstructionLoading.Compare(documents).Rows, "src/backend/AGENTS.md").Claude;
        Assert.Equal(InstructionReach.OnMatch, imported.Reach);
        Assert.Equal("src/backend/**", imported.Detail);
        Assert.True(imported.Imported);
        Assert.False(imported.Loaded);

        var scoped = InstructionLoading.Compare(documents, ["src/backend/Common/Thing.cs"]);
        Assert.True(Row(scoped.Rows, "src/backend/AGENTS.md").Claude.Loaded);
        Assert.Equal(2, scoped.Claude.Files);
    }

    [Fact]
    public void An_import_from_a_path_scoped_rule_carries_the_rules_paths()
    {
        var rows = Rows(
        [
            Document(".claude/rules/api.md", Frontmatter("paths: \"src/api/**\"") + "\n@../../docs/api.md"),
            Document("docs/api.md", "# API")
        ]);

        var imported = Row(rows, "docs/api.md").Claude;
        Assert.Equal(InstructionReach.OnMatch, imported.Reach);
        Assert.Equal("src/api/**", imported.Detail);
    }

    /// <summary>A file the root imports is every session's, whatever else also
    /// imports it: an import from a scoped file never narrows a wider reach.</summary>
    [Fact]
    public void An_always_import_is_not_narrowed_by_a_scoped_one()
    {
        var rows = Rows(
        [
            Document("CLAUDE.md", "@AGENTS.md"),
            Document("src/CLAUDE.md", "@../AGENTS.md"),
            Document("AGENTS.md", "# Shared")
        ]);

        Assert.Equal(InstructionReach.Always, Row(rows, "AGENTS.md").Claude.Reach);
    }

    /// <summary>
    /// A skill is advertised every session and read when invoked, which is
    /// neither a load nor a link. Claude Code discovers skills under
    /// <c>.claude/skills</c>; Copilot under that folder, <c>.agents/skills</c>
    /// and <c>.github/skills</c> — so a skill under <c>.agents</c> is one Copilot
    /// can call and Claude cannot see, which is the drift this view exists to show.
    /// </summary>
    [Fact]
    public void A_skill_is_on_demand_for_each_host_that_discovers_it()
    {
        var comparison = InstructionLoading.Compare(
        [
            Document(".claude/skills/deploy/SKILL.md", "---\nname: deploy\ndescription: Deploys.\n---\n# Deploy\n"),
            Document(".agents/skills/tenants/SKILL.md", "---\nname: tenants\n---\n# Tenants\n"),
            Document(".github/skills/release/SKILL.md", "---\nname: release\n---\n# Release\n")
        ]);
        var rows = comparison.Rows;

        var shared = Row(rows, ".claude/skills/deploy/SKILL.md");
        Assert.Equal(InstructionReach.OnDemand, shared.Claude.Reach);
        Assert.Equal(InstructionReach.OnDemand, shared.Copilot.Reach);
        Assert.False(shared.IsOneSided);

        var agents = Row(rows, ".agents/skills/tenants/SKILL.md");
        Assert.Equal(InstructionReach.NotRead, agents.Claude.Reach);
        Assert.Equal(InstructionReach.OnDemand, agents.Copilot.Reach);
        Assert.True(agents.IsOneSided);

        Assert.Equal(InstructionReach.OnDemand, Row(rows, ".github/skills/release/SKILL.md").Copilot.Reach);
        Assert.Equal(InstructionReach.NotRead, Row(rows, ".github/skills/release/SKILL.md").Claude.Reach);

        // Advertised is not loaded: nothing here counts towards what a host carries.
        Assert.Equal(0, comparison.Claude.Files);
        Assert.Equal(0, comparison.Copilot.Files);
    }

    [Fact]
    public void A_nested_claude_file_is_on_match_and_the_root_one_is_always()
    {
        var rows = Rows(
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
        var rows = Rows([Document(".claude/CLAUDE.md", "# Project")]);

        Assert.Equal(InstructionReach.Always, Row(rows, ".claude/CLAUDE.md").Claude.Reach);
    }

    [Fact]
    public void A_claude_workspace_file_that_is_not_a_rule_is_not_instructions()
    {
        var rows = Rows(
        [
            Document("CLAUDE.md", "# Root"),
            Document(".claude/commands/release.md", "# Release")
        ]);

        Assert.Equal(InstructionReach.NotRead, Row(rows, ".claude/commands/release.md").Claude.Reach);
    }

    [Fact]
    public void Rows_keep_the_order_they_were_discovered_in()
    {
        var rows = Rows(
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
        Assert.Empty(InstructionLoading.Compare([]).Rows);

    [Fact]
    public void A_row_carries_the_size_of_the_file_it_is_about()
    {
        var rows = Rows([Document("CLAUDE.md", "one\ntwo\nthree\n")]);

        var size = Row(rows, "CLAUDE.md").Size;

        // Three lines, and the trailing newline ends the third rather than
        // starting a fourth.
        Assert.Equal(3, size.Lines);
        Assert.Equal(14, size.Bytes);
        Assert.Equal(4, size.EstimatedTokens);
    }

    [Fact]
    public void An_empty_file_is_no_lines_rather_than_one()
    {
        var rows = Rows([Document("CLAUDE.md", string.Empty)]);

        Assert.Equal(0, Row(rows, "CLAUDE.md").Size.Lines);
    }

    /// <summary>
    /// The baseline: with nothing picked, a host carries only what it reads every
    /// session. A conditional file is context it might spend, not context it has
    /// spent, and counting it in the resting total would overstate every host
    /// that scopes its rules well.
    /// </summary>
    [Fact]
    public void With_no_path_picked_the_totals_are_what_each_host_always_carries()
    {
        var comparison = InstructionLoading.Compare(
        [
            Document("CLAUDE.md", "root\n"),
            Document(".github/copilot-instructions.md", "copilot\n"),
            Document(".github/instructions/ui-components.instructions.md", Frontmatter("applyTo: \"src/App/**\""))
        ]);

        Assert.False(comparison.IsScoped);
        Assert.Equal(1, comparison.Claude.Files);
        // Its own root file and the root CLAUDE.md, which stands in for AGENTS.md.
        Assert.Equal(2, comparison.Copilot.Files);
        Assert.Equal(5, comparison.Claude.Bytes);
        Assert.Equal(13, comparison.Copilot.Bytes);
    }

    [Fact]
    public void A_picked_path_turns_on_the_files_whose_scope_covers_it()
    {
        var documents = new[]
        {
            Document(".github/copilot-instructions.md", "copilot\n"),
            Document(".github/instructions/ui-components.instructions.md", Frontmatter("applyTo: \"src/App/**\""))
        };

        var comparison = InstructionLoading.Compare(documents, ["src/App/Backlog.Desktop.UI/Home.razor"]);

        Assert.True(comparison.IsScoped);

        var scoped = Row(comparison.Rows, ".github/instructions/ui-components.instructions.md").Copilot;
        Assert.Equal(InstructionReach.OnMatch, scoped.Reach);
        Assert.True(scoped.Loaded);

        // The conditional file joins the baseline rather than replacing it.
        Assert.Equal(2, comparison.Copilot.Files);
        Assert.Equal(0, comparison.Claude.Files);
    }

    [Fact]
    public void A_picked_path_nothing_covers_leaves_the_baseline_where_it_was()
    {
        var documents = new[]
        {
            Document(".github/copilot-instructions.md", "copilot\n"),
            Document(".github/instructions/ui-components.instructions.md", Frontmatter("applyTo: \"src/App/**\""))
        };

        var baseline = InstructionLoading.Compare(documents);
        var scoped = InstructionLoading.Compare(documents, ["tests/Backlog.ArchitectureTests/Thing.cs"]);

        Assert.Equal(baseline.Copilot.Files, scoped.Copilot.Files);
        Assert.Equal(baseline.Copilot.Bytes, scoped.Copilot.Bytes);
        Assert.False(Row(scoped.Rows, ".github/instructions/ui-components.instructions.md").Copilot.Loaded);
    }

    [Fact]
    public void Several_picked_paths_are_read_together()
    {
        var documents = new[]
        {
            Document(".github/instructions/ui-components.instructions.md", Frontmatter("applyTo: \"src/App/**\"")),
            Document(".github/instructions/storybook.instructions.md", Frontmatter("applyTo: \"src/Harness/Backlog.UI.Storybook/**\""))
        };

        var one = InstructionLoading.Compare(documents, ["src/App/Home.razor"]);
        var both = InstructionLoading.Compare(documents, ["src/App/Home.razor", "src/Harness/Backlog.UI.Storybook/Index.razor"]);

        Assert.Equal(1, one.Copilot.Files);
        Assert.Equal(2, both.Copilot.Files);
    }

    [Fact]
    public void Clearing_the_selection_returns_the_unscoped_reading()
    {
        var documents = new[]
        {
            Document(".github/copilot-instructions.md", "copilot\n"),
            Document(".github/instructions/ui-components.instructions.md", Frontmatter("applyTo: \"src/App/**\""))
        };

        var baseline = InstructionLoading.Compare(documents);
        var cleared = InstructionLoading.Compare(documents, []);

        Assert.False(cleared.IsScoped);
        Assert.Equal(baseline.Copilot.Files, cleared.Copilot.Files);
    }

    /// <summary>An always-loaded file is loaded whatever is picked — a selection
    /// narrows what else comes with it, never what the host already carries.</summary>
    [Fact]
    public void A_picked_path_never_turns_an_always_file_off()
    {
        var comparison = InstructionLoading.Compare(
            [Document("CLAUDE.md", "root\n")],
            ["tests/Nothing.cs"]);

        Assert.True(Row(comparison.Rows, "CLAUDE.md").Claude.Loaded);
        Assert.Equal(1, comparison.Claude.Files);
    }

    [Fact]
    public void A_blank_path_is_not_a_selection()
    {
        var comparison = InstructionLoading.Compare([Document("CLAUDE.md", "root\n")], ["   "]);

        Assert.False(comparison.IsScoped);
    }

    private static IReadOnlyList<InstructionLoadingRow> Rows(IReadOnlyList<InstructionDocument> documents) =>
        InstructionLoading.Compare(documents).Rows;

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
