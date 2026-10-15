using System.Text.RegularExpressions;

namespace Backlog.ArchitectureTests;

/// <summary>
/// The agent context files are what a flow reads before it touches anything, so a
/// pointer in them that names a folder, a file or a skill that is not there sends the
/// flow down a dead end. The 2026-09-27 repository review found several; these tests
/// are what says so the next time one goes stale.
/// </summary>
public partial class AgentContextPointerTests
{
    /// <summary>
    /// <c>CLAUDE.md</c> describes each of the backlog-tools plugin's skills and its
    /// relationship to the gate. A skill added to the plugin and left out of that
    /// paragraph leaves a flow guessing whether it routes through the gate.
    /// </summary>
    [Fact]
    public void Claude_md_names_every_backlog_tools_skill()
    {
        var claude = Read("CLAUDE.md");
        var skills = new DirectoryInfo(Path.Combine(Repository.Root.FullName, "plugins", "backlog-tools", "skills"))
            .EnumerateDirectories()
            .Where(folder => File.Exists(Path.Combine(folder.FullName, "SKILL.md")))
            .Select(folder => folder.Name)
            .ToList();

        Assert.NotEmpty(skills);

        var missing = skills
            .Where(skill => !claude.Contains($"plugins/backlog-tools/skills/{skill}/SKILL.md", StringComparison.Ordinal))
            .Order(StringComparer.Ordinal)
            .ToList();

        Assert.True(
            missing.Count == 0,
            "CLAUDE.md's Further guidance list has no entry for: " + string.Join(", ", missing)
            + ". Add the skill's SKILL.md path there and say how it relates to the gate.");
        Assert.DoesNotContain("Neither of its skills", claude, StringComparison.Ordinal);
    }

    /// <summary>
    /// The gate text names <c>src/</c> and <c>tests/</c>; the folders around them are
    /// routed by the gate table's tooling row. Both copies of the gate say so in the
    /// same sentence, since <c>context-loading.md</c> asks that the two be kept in step.
    /// </summary>
    [Fact]
    public void Both_gate_copies_route_the_folders_outside_src_and_tests_through_flow_code()
    {
        var claudeSentence = ScopeSentence(Read("CLAUDE.md"), "CLAUDE.md");
        var rulesSentence = ScopeSentence(Read(".agents/rules/context-loading.md"), ".agents/rules/context-loading.md");

        foreach (var folder in new[] { "`plugins/`", "`tools/`", "`build/`", "`.github/`", "`.claude/`", "`delivery:flow-code`" })
        {
            Assert.Contains(folder, claudeSentence, StringComparison.Ordinal);
        }

        Assert.Equal(claudeSentence, rulesSentence);
    }

    /// <summary>
    /// The harness README's table is where a reader learns what each host under
    /// <c>src/Harness</c> is for and which Aspire resource runs it.
    /// </summary>
    [Fact]
    public void The_harness_readme_table_lists_every_project_under_src_harness()
    {
        var readme = Read("src/Harness/README.md");

        var missing = Repository.ProjectsUnder("src", "Harness")
            .Select(project => Path.GetFileNameWithoutExtension(project.Name))
            .Where(name => !readme.Contains($"| `{name}` |", StringComparison.Ordinal))
            .Order(StringComparer.Ordinal)
            .ToList();

        Assert.True(
            missing.Count == 0,
            "src/Harness/README.md's table has no row for: " + string.Join(", ", missing) + ".");
        Assert.DoesNotContain("These are Blazor Server hosts", readme, StringComparison.Ordinal);
    }

    /// <summary>
    /// The design rules moved to <c>.devbook/design/</c> under local ADR 0016; a bare
    /// <c>.design</c> folder no longer exists.
    /// </summary>
    [Theory]
    [InlineData("src/Harness/README.md")]
    [InlineData("src/Harness/Backlog.UI.Storybook/Backlog.UI.Storybook.csproj")]
    public void No_harness_file_points_at_the_pre_devbook_design_folder(string path)
    {
        var stale = BareDesignFolder().Matches(Read(path)).Select(match => match.Value).ToList();

        Assert.True(stale.Count == 0, $"{path} still points at the `.design` folder; it is `.devbook/design/`.");
    }

    /// <summary>
    /// Pointers the review found that name nothing in the repository: a deleted
    /// context file, a skill nobody ships, an absent manifest and a renamed folder.
    /// </summary>
    [Theory]
    [InlineData("orch-context")]
    [InlineData("pr-jsdotnet")]
    [InlineData("github-app.yml")]
    [InlineData("src/Shared/")]
    public void No_agent_context_file_names_a_known_dead_pointer(string pointer)
    {
        var offenders = AgentContextFiles()
            .Where(file => File.ReadAllText(file.FullName).Contains(pointer, StringComparison.Ordinal))
            .Select(file => Path.GetRelativePath(Repository.Root.FullName, file.FullName).Replace('\\', '/'))
            .ToList();

        Assert.True(offenders.Count == 0, $"'{pointer}' names nothing in the repository, but appears in: " + string.Join(", ", offenders));
    }

    /// <summary>
    /// A procedure under <c>.agents/skills/</c> is reached through its
    /// <c>.claude/skills/&lt;name&gt;/SKILL.md</c> wrapper; one without a wrapper is
    /// never loaded and goes stale unread, as the five vendored Aspire skills did.
    /// A procedure is a <c>*.md</c> file or a folder holding a <c>SKILL.md</c>.
    /// </summary>
    [Fact]
    public void Every_agents_skill_has_a_claude_skills_wrapper_that_names_its_path()
    {
        var root = new DirectoryInfo(Path.Combine(Repository.Root.FullName, ".agents", "skills"));
        Assert.True(root.Exists, ".agents/skills/ is missing.");

        var procedures = root.EnumerateFiles("*.md")
            .Select(file => (Name: Path.GetFileNameWithoutExtension(file.Name), Path: $".agents/skills/{file.Name}"))
            .Concat(root.EnumerateDirectories()
                .Where(folder => File.Exists(Path.Combine(folder.FullName, "SKILL.md")))
                .Select(folder => (Name: folder.Name, Path: $".agents/skills/{folder.Name}/")))
            .ToList();

        Assert.NotEmpty(procedures);

        var unwrapped = procedures
            .Where(procedure =>
            {
                var wrapper = Path.Combine(Repository.Root.FullName, ".claude", "skills", procedure.Name, "SKILL.md");
                return !File.Exists(wrapper) || !File.ReadAllText(wrapper).Contains(procedure.Path, StringComparison.Ordinal);
            })
            .Select(procedure => procedure.Path)
            .Order(StringComparer.Ordinal)
            .ToList();

        Assert.True(
            unwrapped.Count == 0,
            "No .claude/skills/<name>/SKILL.md wrapper names: " + string.Join(", ", unwrapped)
            + ". Add a wrapper that reads it, or delete the procedure.");
    }

    /// <summary>
    /// The multi-account design document was retired into local ADR 0031, and its
    /// <c>.claude/design/</c> folder with it; a design kept beside the agent context
    /// files is read by nothing and drifts from what was built.
    /// </summary>
    [Fact]
    public void No_design_document_folder_sits_under_dot_claude()
    {
        Assert.False(
            Directory.Exists(Path.Combine(Repository.Root.FullName, ".claude", "design")),
            ".claude/design/ exists; a decision belongs in .devbook/arc42/adr/, not beside the agent context files.");
    }

    /// <summary>
    /// Pointers retired in the same change as what they named: the vendored Aspire
    /// skills, deleted for the Aspire CLI's own help and the Aspire MCP docs tools,
    /// and the multi-account design document, retired into local ADR 0031. Checked
    /// in the devbook chapters and the Copilot files too, since both named them.
    /// </summary>
    [Theory]
    [InlineData("skills/aspire")]
    [InlineData("Aspire skills")]
    [InlineData("multi-account-settings")]
    public void No_context_file_or_devbook_chapter_names_a_retired_pointer(string pointer)
    {
        var offenders = AgentContextFiles()
            .Concat(MarkdownUnder(".github"))
            .Concat(MarkdownUnder(".devbook").Where(file => !IsGenerated(file)))
            .Where(file => File.ReadAllText(file.FullName).Contains(pointer, StringComparison.Ordinal))
            .Select(file => Path.GetRelativePath(Repository.Root.FullName, file.FullName).Replace('\\', '/'))
            .Distinct(StringComparer.Ordinal)
            .ToList();

        Assert.True(offenders.Count == 0, $"'{pointer}' names something retired, but appears in: " + string.Join(", ", offenders));
    }

    /// <summary>
    /// The <c>run</c> procedure is where a flow learns the Aspire CLI; with the
    /// vendored skills gone it points at the CLI's own help and the Aspire MCP docs
    /// tools, which answer for the pinned version.
    /// </summary>
    [Fact]
    public void The_run_procedure_points_at_the_aspire_cli_help_and_the_mcp_docs_tools()
    {
        var run = Read(".claude/skills/run-backlog/SKILL.md");

        Assert.Contains("aspire <command> --help", run, StringComparison.Ordinal);
        Assert.Contains("search_docs", run, StringComparison.Ordinal);
        Assert.Contains("get_doc", run, StringComparison.Ordinal);
    }

    private static IEnumerable<FileInfo> MarkdownUnder(string folder)
    {
        var root = new DirectoryInfo(Path.Combine(Repository.Root.FullName, folder));
        return root.Exists ? root.EnumerateFiles("*.md", SearchOption.AllDirectories) : [];
    }

    /// <summary>The devbook's generated and installed parts, which no chapter edit reaches.</summary>
    private static bool IsGenerated(FileInfo file)
    {
        var path = file.FullName.Replace('\\', '/');
        return path.Contains("/_meta/", StringComparison.Ordinal) || path.Contains("/_tools/", StringComparison.Ordinal);
    }

    private static IEnumerable<FileInfo> AgentContextFiles()
    {
        yield return new FileInfo(RepositoryRoot.File("CLAUDE.md"));
        yield return new FileInfo(RepositoryRoot.File("AGENTS.md"));

        foreach (var folder in new[] { ".agents", ".claude" })
        {
            var root = new DirectoryInfo(Path.Combine(Repository.Root.FullName, folder));
            if (!root.Exists) continue;

            foreach (var file in root.EnumerateFiles("*.md", SearchOption.AllDirectories)
                .Where(file => !file.FullName.Contains($"{Path.DirectorySeparatorChar}worktrees{Path.DirectorySeparatorChar}")))
            {
                yield return file;
            }
        }
    }

    /// <summary>The one sentence that names <c>plugins/</c>, with its line breaks folded.</summary>
    private static string ScopeSentence(string text, string path)
    {
        var sentence = ParagraphBreak().Split(text)
            .Select(paragraph => Whitespace().Replace(paragraph, " ").Trim())
            .SelectMany(paragraph => SentenceBreak().Split(paragraph))
            .FirstOrDefault(candidate => candidate.Contains("`plugins/`", StringComparison.Ordinal));

        Assert.True(sentence is not null, $"{path} has no sentence naming `plugins/` and the flow it routes through.");
        return sentence!;
    }

    private static string Read(string relativePath) => File.ReadAllText(RepositoryRoot.File(relativePath));

    [GeneratedRegex(@"(?<![\w/])\.design(?![\w-])")]
    private static partial Regex BareDesignFolder();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();

    [GeneratedRegex(@"\r?\n\s*\r?\n")]
    private static partial Regex ParagraphBreak();

    [GeneratedRegex(@"(?<=[.!?])\s+")]
    private static partial Regex SentenceBreak();
}
