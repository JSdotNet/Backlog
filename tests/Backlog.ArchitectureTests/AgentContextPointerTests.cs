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
