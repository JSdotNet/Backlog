namespace Backlog.ArchitectureTests;

/// <summary>
/// The README is where a new contributor learns the layout and how to build, test
/// and run it. It fell a whole module behind the tree once, with nothing to say so;
/// these tests are what says so now.
/// </summary>
public class ReadmeTests
{
    private const string ProjectTableHeader = "| Project | Channel / role |";

    /// <summary>
    /// Every project under <c>src/</c> and <c>tests/</c> has a row in the solution
    /// structure table. Extra rows are allowed: <c>src/App/Backlog.Ide.VsCode</c> is a
    /// TypeScript project with no <c>.csproj</c>.
    /// </summary>
    [Fact]
    public void The_project_table_lists_every_project_under_src_and_tests()
    {
        var rows = ProjectTableRows();

        Assert.NotEmpty(rows);

        var missing = Repository.ProjectsUnder("src")
            .Concat(Repository.ProjectsUnder("tests"))
            .Select(project => Path.GetRelativePath(Repository.Root.FullName, project.DirectoryName!).Replace('\\', '/'))
            .Where(folder => !rows.Contains(folder))
            .Order(StringComparer.Ordinal)
            .ToList();

        Assert.True(
            missing.Count == 0,
            "README.md's project table has no row for: " + string.Join(", ", missing)
            + ". Add a `| `<folder>` | <role> |` row to the table under '## Solution structure'.");
    }

    [Fact]
    public void The_run_command_is_the_isolated_aspire_start()
    {
        var readme = Readme();

        Assert.Contains(
            "aspire start --isolated --non-interactive --apphost src/Aspire/Backlog.Aspire.AppHost/Backlog.Aspire.AppHost.csproj",
            readme,
            StringComparison.Ordinal);
        Assert.DoesNotContain("dotnet run --project src/Aspire/Backlog.Aspire.AppHost", readme, StringComparison.Ordinal);
    }

    /// <summary>This repository's <c>dotnet test</c> can report "Zero tests ran"
    /// without running anything, so the README says how to tell.</summary>
    [Fact]
    public void The_build_and_test_section_warns_that_dotnet_test_can_run_nothing()
    {
        var section = Section("## Build and test");

        Assert.Contains("dotnet build Backlog.sln", section, StringComparison.Ordinal);
        Assert.Contains("Zero tests ran", section, StringComparison.Ordinal);
    }

    [Fact]
    public void The_current_state_no_longer_says_setup_mode()
    {
        Assert.DoesNotContain("setup mode", Readme(), StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>The first cell of each row in the solution structure table, without its backticks.</summary>
    private static HashSet<string> ProjectTableRows()
    {
        var lines = Readme().Split('\n').Select(line => line.TrimEnd('\r')).ToList();
        var header = lines.IndexOf(ProjectTableHeader);

        Assert.True(header >= 0, $"README.md has no project table headed '{ProjectTableHeader}'.");

        return lines
            .Skip(header + 2)
            .TakeWhile(line => line.StartsWith('|'))
            .Select(line => line.Split('|', StringSplitOptions.TrimEntries)[1].Trim('`'))
            .ToHashSet(StringComparer.Ordinal);
    }

    /// <summary>The text from a level-two heading up to the next one.</summary>
    private static string Section(string heading)
    {
        var readme = Readme();
        var start = readme.IndexOf(heading + "\n", StringComparison.Ordinal);
        if (start < 0) start = readme.IndexOf(heading + "\r\n", StringComparison.Ordinal);

        Assert.True(start >= 0, $"README.md has no '{heading}' section.");

        var end = readme.IndexOf("\n## ", start + heading.Length, StringComparison.Ordinal);
        return end < 0 ? readme[start..] : readme[start..end];
    }

    private static string Readme() => File.ReadAllText(RepositoryRoot.File("README.md"));
}
