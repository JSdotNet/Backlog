using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace Backlog.ArchitectureTests;

/// <summary>
/// The build policy lives in a handful of root files — <c>.editorconfig</c>,
/// <c>Directory.Build.props</c>, <c>global.json</c> — and in the flags the pull
/// request workflow passes. None of them fails anything when a line goes
/// missing: a dropped analyzer setting or a floating SDK just lets the warning
/// count drift back up, which is how the solution reached 75 warnings with
/// nobody deciding it should. These tests are what notices the line going.
/// </summary>
public partial class BuildPolicyTests
{
    private static XDocument RootProps() => XDocument.Load(RepositoryRoot.File("Directory.Build.props"));

    private static string EditorConfig() => File.ReadAllText(RepositoryRoot.File(".editorconfig"));

    private static IEnumerable<string> Workflows() =>
        Directory.EnumerateFiles(RepositoryRoot.Directory(".github", "workflows"), "*.yml");

    /// <summary>The value of the first element named <paramref name="property"/>
    /// in the root props, whatever group holds it.</summary>
    private static XElement? RootProperty(string property) =>
        RootProps().Descendants(property).FirstOrDefault();

    [Fact]
    public void The_root_editorconfig_describes_the_csharp_style()
    {
        var config = EditorConfig();

        Assert.Matches(@"(?m)^root\s*=\s*true\s*$", config);
        Assert.Contains("csharp_style_namespace_declarations", config, StringComparison.Ordinal);
        Assert.Contains("csharp_style_var_", config, StringComparison.Ordinal);
        Assert.Contains("csharp_style_expression_bodied_", config, StringComparison.Ordinal);
        Assert.Contains("dotnet_naming_rule.", config, StringComparison.Ordinal);
        Assert.Contains("dotnet_analyzer_diagnostic.", config, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("AnalysisLevel", "latest")]
    [InlineData("EnforceCodeStyleInBuild", "true")]
    [InlineData("Deterministic", "true")]
    public void The_root_props_sets_the_analysis_defaults(string property, string expected)
    {
        var element = RootProperty(property);

        Assert.NotNull(element);
        Assert.Equal(expected, element.Value.Trim(), ignoreCase: true);
    }

    /// <summary>A continuous-integration build rewrites source paths in the
    /// PDBs, which is right on a runner and wrong on a developer's machine —
    /// a debugger there could no longer find the files.</summary>
    [Fact]
    public void Continuous_integration_build_is_set_only_on_a_ci_runner()
    {
        var element = RootProperty("ContinuousIntegrationBuild");

        Assert.NotNull(element);
        Assert.Equal("true", element.Value.Trim(), ignoreCase: true);

        var condition = element.Attribute("Condition")?.Value
            ?? element.Parent?.Attribute("Condition")?.Value;

        Assert.NotNull(condition);
        Assert.Contains("GITHUB_ACTIONS", condition, StringComparison.Ordinal);
    }

    /// <summary>
    /// The exhaustive-switch pair (CS8509 an error, CS8524 off) is one policy
    /// set once, not a thing each project that switches over an enum has to
    /// remember. A project that restates it is a project that can restate it
    /// differently.
    /// </summary>
    [Fact]
    public void The_exhaustive_switch_pair_is_set_once_at_the_root()
    {
        Assert.Contains("CS8509", RootProperty("WarningsAsErrors")?.Value ?? "", StringComparison.Ordinal);
        Assert.Contains("CS8524", RootProperty("NoWarn")?.Value ?? "", StringComparison.Ordinal);

        var restated = Repository.ProjectsUnder("src")
            .Concat(Repository.ProjectsUnder("tests"))
            .Where(project =>
            {
                var text = File.ReadAllText(project.FullName);
                return text.Contains("CS8509", StringComparison.Ordinal)
                    || text.Contains("CS8524", StringComparison.Ordinal);
            })
            .Select(project => project.Name)
            .ToList();

        Assert.True(
            restated.Count == 0,
            "The CS8509/CS8524 pair lives in the root Directory.Build.props; remove it from: " + string.Join(", ", restated));
    }

    /// <summary>
    /// Every await in the infrastructure adapters gives up the captured context,
    /// because they are called from UI threads they know nothing about. The
    /// analyzer that says so is scoped to that folder: the UI projects want the
    /// context back.
    /// </summary>
    [Fact]
    public void ConfigureAwait_is_a_rule_for_the_infrastructure_projects_only()
    {
        var sections = EditorConfigSections(EditorConfig());

        var scoped = sections
            .Where(section => section.Body.Any(line => Regex.IsMatch(line, @"^dotnet_diagnostic\.CA2007\.severity\s*=\s*(warning|error)\s*$")))
            .Select(section => section.Glob)
            .ToList();

        Assert.Equal(["src/Infrastructure/**.cs"], scoped);
    }

    [Fact]
    public void Global_json_pins_the_sdk_to_a_patch_band()
    {
        using var json = JsonDocument.Parse(File.ReadAllText(RepositoryRoot.File("global.json")));
        var root = json.RootElement;

        Assert.True(root.TryGetProperty("sdk", out var sdk), "global.json must pin the SDK.");
        Assert.Matches(@"^\d+\.\d+\.\d{3}$", sdk.GetProperty("version").GetString());
        Assert.Equal("latestPatch", sdk.GetProperty("rollForward").GetString());

        // The test runner opt-in `dotnet test` depends on stays beside the pin.
        Assert.Equal("Microsoft.Testing.Platform", root.GetProperty("test").GetProperty("runner").GetString());
    }

    /// <summary>A workflow that names its own SDK version is one that builds with
    /// something other than what global.json says developers build with.</summary>
    [Fact]
    public void Every_workflow_installs_the_sdk_global_json_names()
    {
        var offenders = Workflows()
            .SelectMany(file => SetupDotnetSteps(File.ReadAllLines(file))
                .Where(step => !step.Any(line => Regex.IsMatch(line, @"^\s*global-json-file:\s*global\.json\s*$"))
                            || step.Any(line => line.TrimStart().StartsWith("dotnet-version:", StringComparison.Ordinal)))
                .Select(_ => Path.GetFileName(file)))
            .ToList();

        Assert.NotEmpty(Workflows().SelectMany(file => SetupDotnetSteps(File.ReadAllLines(file))));
        Assert.True(
            offenders.Count == 0,
            "setup-dotnet must read global-json-file: global.json, not a dotnet-version, in: " + string.Join(", ", offenders));
    }

    /// <summary>The pull request is where a new warning has to stop, so every
    /// build that job runs treats one as an error.</summary>
    [Fact]
    public void The_pull_request_builds_treat_warnings_as_errors()
    {
        var lines = File.ReadAllLines(RepositoryRoot.File(".github", "workflows", "pull-request.yml"));

        var builds = RunBlocks(lines)
            .Where(block => block.Any(line => line.Contains("dotnet build", StringComparison.Ordinal)))
            .ToList();

        Assert.NotEmpty(builds);
        Assert.All(builds, block =>
            Assert.Contains(block, line => line.Contains("-p:TreatWarningsAsErrors=true", StringComparison.Ordinal)));
    }

    /// <summary>Transitive pinning is off, so a version nothing references pins
    /// nothing — it only reads as if it did.</summary>
    [Fact]
    public void Central_package_management_declares_no_unreferenced_yaml_version()
    {
        var packages = XDocument.Load(RepositoryRoot.File("Directory.Packages.props"));

        Assert.DoesNotContain(
            packages.Descendants("PackageVersion"),
            p => string.Equals((string?)p.Attribute("Include"), "YamlDotNet", StringComparison.OrdinalIgnoreCase));
    }

    private sealed record Section(string Glob, IReadOnlyList<string> Body);

    private static List<Section> EditorConfigSections(string config)
    {
        var sections = new List<Section>();
        string? glob = null;
        var body = new List<string>();

        foreach (var raw in config.Split('\n'))
        {
            var line = raw.Trim();
            var header = SectionHeader().Match(line);
            if (header.Success)
            {
                if (glob is not null) sections.Add(new Section(glob, body));
                glob = header.Groups[1].Value;
                body = [];
            }
            else if (glob is not null && line.Length > 0 && !line.StartsWith('#'))
            {
                body.Add(line);
            }
        }

        if (glob is not null) sections.Add(new Section(glob, body));
        return sections;
    }

    /// <summary>Each <c>uses: actions/setup-dotnet</c> step with the lines
    /// indented under it.</summary>
    private static IEnumerable<List<string>> SetupDotnetSteps(string[] lines) =>
        Steps(lines).Where(step => step.Any(line => line.Contains("actions/setup-dotnet", StringComparison.Ordinal)));

    /// <summary>Each step's <c>run:</c> block, folded or literal, as its lines.</summary>
    private static IEnumerable<List<string>> RunBlocks(string[] lines) =>
        Steps(lines).Select(step => step.SkipWhile(line => !line.TrimStart().StartsWith("run:", StringComparison.Ordinal)).ToList())
            .Where(block => block.Count > 0);

    /// <summary>A step starts at a <c>- name:</c> or <c>- uses:</c> item and runs
    /// to the next item at the same indentation or less.</summary>
    private static IEnumerable<List<string>> Steps(string[] lines)
    {
        List<string>? step = null;
        var indent = -1;

        foreach (var line in lines)
        {
            var item = StepItem().Match(line);
            var lineIndent = line.Length - line.TrimStart().Length;

            if (item.Success)
            {
                if (step is not null) yield return step;
                step = [line];
                indent = item.Groups[1].Length;
            }
            else if (step is not null && line.Trim().Length > 0 && lineIndent <= indent)
            {
                yield return step;
                step = null;
            }
            else
            {
                step?.Add(line);
            }
        }

        if (step is not null) yield return step;
    }

    [GeneratedRegex(@"^\[(.+)\]$")]
    private static partial Regex SectionHeader();

    [GeneratedRegex(@"^(\s*)- (name|uses):")]
    private static partial Regex StepItem();
}
