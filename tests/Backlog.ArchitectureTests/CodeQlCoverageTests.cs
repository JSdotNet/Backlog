using System.Text.RegularExpressions;

namespace Backlog.ArchitectureTests;

/// <summary>
/// CodeQL used to scan the <c>actions</c> language alone, so the C# solution and
/// the TypeScript VS Code extension were never analysed (issue #749). These rules
/// keep all three languages in the matrix, keep the C# scan free of a build — a
/// MAUI workload install on the scan runner is what <c>build-mode: none</c>
/// avoids — and keep the generated Archify renders out of the JavaScript results.
/// </summary>
/// <remarks>
/// The workflow is read as text: this project takes no package references, and
/// the shape asserted here is small enough that a line match says it plainly.
/// </remarks>
public class CodeQlCoverageTests
{
    private static readonly string[] Workflow = [".github", "workflows", "codeql.yml"];

    private static readonly string[] Config = [".github", "codeql", "codeql-config.yml"];

    private const string ConfigPath = ".github/codeql/codeql-config.yml";

    private static readonly string[] Languages = ["actions", "csharp", "javascript-typescript"];

    private static readonly string[] IgnoredRenders =
        [".devbook/**/_archify/*.html", "tools/archify/examples/*.html"];

    private static readonly Regex MatrixLanguage =
        new(@"^\s*-\s*language:\s*(?<language>\S+)\s*$", RegexOptions.Multiline);

    private static readonly Regex CSharpEntry =
        new(@"^\s*-\s*language:\s*csharp\s*\r?\n\s*build-mode:\s*none\s*$", RegexOptions.Multiline);

    [Fact]
    public void The_matrix_analyses_actions_csharp_and_typescript()
    {
        var workflow = File.ReadAllText(RepositoryRoot.File(Workflow));

        var languages = MatrixLanguage.Matches(workflow)
            .Select(m => m.Groups["language"].Value)
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(Languages, languages);
    }

    [Fact]
    public void The_csharp_scan_needs_no_build()
    {
        var workflow = File.ReadAllText(RepositoryRoot.File(Workflow));

        Assert.Matches(CSharpEntry, workflow);
        Assert.DoesNotContain("codeql-action/autobuild", workflow, StringComparison.Ordinal);
        Assert.DoesNotContain("dotnet build", workflow, StringComparison.Ordinal);
        Assert.DoesNotContain("workload install", workflow, StringComparison.Ordinal);
    }

    [Fact]
    public void Init_takes_the_language_build_mode_and_config_file()
    {
        var workflow = File.ReadAllText(RepositoryRoot.File(Workflow));

        Assert.Contains("languages: ${{ matrix.language }}", workflow, StringComparison.Ordinal);
        Assert.Contains("build-mode: ${{ matrix.build-mode }}", workflow, StringComparison.Ordinal);
        Assert.Contains($"config-file: ./{ConfigPath}", workflow, StringComparison.Ordinal);
    }

    [Fact]
    public void The_job_is_named_for_its_language()
    {
        var workflow = File.ReadAllText(RepositoryRoot.File(Workflow));

        Assert.Contains("name: Analyze (${{ matrix.language }})", workflow, StringComparison.Ordinal);
        Assert.DoesNotContain("name: Analyze (actions)", workflow, StringComparison.Ordinal);
    }

    [Fact]
    public void The_config_ignores_the_generated_archify_renders()
    {
        var config = File.ReadAllText(RepositoryRoot.File(Config));

        Assert.Contains("paths-ignore:", config, StringComparison.Ordinal);
        foreach (var glob in IgnoredRenders)
        {
            Assert.Contains($"- '{glob}'", config, StringComparison.Ordinal);
        }
    }
}
