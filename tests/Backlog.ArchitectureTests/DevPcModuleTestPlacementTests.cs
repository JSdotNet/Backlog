using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace Backlog.ArchitectureTests;

/// <summary>
/// Where Dev PC Management's module tests live.
///
/// <para>They used to sit in <c>Backlog.Desktop.UI.UnitTests</c>, which also holds
/// the shell, the other module screens and their composition, so a change to the
/// tools pane rebuilt and reran the whole of that suite. The files that test only
/// <c>Backlog.Modules.DevPc.Abstractions</c> and <c>Backlog.Modules.DevPc.UI</c>
/// (with the adapter in <c>Backlog.Infrastructure.DevPc</c> behind the pane) live in
/// a test project of their own now. The adapter's own tests are
/// <c>Backlog.Infrastructure.DevPc.UnitTests</c> (<see cref="DevPcAdapterTests"/>),
/// and the Dev PC tests that need the desktop shell or the harness composition stay
/// where they were.</para>
/// </summary>
public class DevPcModuleTestPlacementTests
{
    private const string ModuleTests = "Backlog.Modules.DevPc.UnitTests";
    private const string DesktopUiTests = "Backlog.Desktop.UI.UnitTests";

    /// <summary>The files that test the Dev PC module alone.</summary>
    public static TheoryData<string> ModuleTestFiles() =>
    [
        "ToolsPaneTests.cs",
        "ToolsPaneDesignAlignmentTests.cs",
        "ToolsPaneOverviewTests.cs",
        "ToolsPaneRailLayoutTests.cs",
        "ToolsPaneRunStatesTests.cs",
        "ToolsPaneTabsTests.cs",
        "ToolsAiContentSourceTests.cs",
        "DevToolOutputTests.cs",
        "DevToolApplicationTests.cs",
        "McpEndpointExpansionTests.cs",
        "ProcessEnvironmentCollection.cs"
    ];

    /// <summary>The Dev PC tests that depend on the desktop shell's MCP registration
    /// or on the harness composition, and so stay behind.</summary>
    public static TheoryData<string> ShellDependentTestFiles() =>
    [
        "DevToolMcpServerTests.cs",
        "HarnessDevToolServiceTests.cs"
    ];

    [Theory]
    [MemberData(nameof(ModuleTestFiles))]
    public void The_module_test_lives_in_the_module_test_project(string file)
    {
        Assert.False(
            File.Exists(PathOf("tests", DesktopUiTests, file)),
            $"{file} tests only the Dev PC module, so it belongs in tests/{ModuleTests} rather than tests/{DesktopUiTests}.");

        var moved = PathOf("tests", ModuleTests, file);
        Assert.True(File.Exists(moved), $"{file} is not in tests/{ModuleTests}.");

        var source = File.ReadAllText(moved);
        var namespaces = Namespace.Matches(source).Select(match => match.Groups[1].Value).ToList();
        Assert.Equal([ModuleTests], namespaces);
        Assert.DoesNotContain($"using static {DesktopUiTests}.", source, StringComparison.Ordinal);
    }

    [Theory]
    [MemberData(nameof(ShellDependentTestFiles))]
    public void The_shell_dependent_test_stays_in_the_desktop_ui_tests(string file)
    {
        Assert.True(File.Exists(PathOf("tests", DesktopUiTests, file)), $"{file} is not in tests/{DesktopUiTests}.");
        Assert.False(File.Exists(PathOf("tests", ModuleTests, file)), $"{file} depends on the desktop shell, so it does not belong in tests/{ModuleTests}.");
    }

    /// <summary>The helpers other test classes borrow — <c>ToolsPaneTests.FakeDevToolService</c>
    /// and the process-environment collection — moved with the files that declare them
    /// rather than being copied, so each is declared once across the two projects and
    /// tests/Shared.</summary>
    [Theory]
    [InlineData(@"record\s+FakeDevToolService\b")]
    [InlineData(@"class\s+ProcessEnvironmentCollection\b")]
    public void A_shared_helper_is_declared_once(string declaration)
    {
        var pattern = new Regex(declaration);
        var declaring = new[] { ModuleTests, DesktopUiTests, "Shared" }
            .Select(project => PathOf("tests", project))
            .Where(Directory.Exists)
            .SelectMany(directory => Directory.EnumerateFiles(directory, "*.cs", SearchOption.AllDirectories))
            .Where(file => !file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}") && !file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"))
            .Where(file => pattern.IsMatch(File.ReadAllText(file)))
            .Select(Path.GetFileName)
            .ToList();

        Assert.Single(declaring);
    }

    [Fact]
    public void The_module_test_project_is_in_the_solution_and_its_filter()
    {
        var solution = File.ReadAllText(PathOf("Backlog.sln"));
        var filter = File.ReadAllText(PathOf("Backlog.WithoutAppHeads.slnf"));
        var path = $@"tests\{ModuleTests}\{ModuleTests}.csproj";

        Assert.Contains(path, solution, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(path.Replace(@"\", @"\\"), filter, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>The project takes its runner, output type and coverage from
    /// <c>tests/Directory.Build.props</c> like every other test project, and
    /// references the Dev PC projects it covers but not the desktop app, its harness
    /// or the phone.</summary>
    [Fact]
    public void The_module_test_project_references_dev_pc_and_not_the_desktop_app()
    {
        var project = new FileInfo(PathOf("tests", ModuleTests, $"{ModuleTests}.csproj"));
        Assert.True(project.Exists, $"{project.FullName} does not exist.");

        var document = XDocument.Load(project.FullName);
        Assert.Empty(document.Descendants("OutputType"));

        var packages = document.Descendants("PackageReference")
            .Select(package => (string?)package.Attribute("Include") ?? string.Empty)
            .ToList();
        foreach (var inherited in new[] { "xunit.v3", "xunit.runner.visualstudio", "Microsoft.NET.Test.Sdk", "Microsoft.Testing.Extensions.CodeCoverage" })
        {
            Assert.DoesNotContain(inherited, packages, StringComparer.OrdinalIgnoreCase);
        }

        var referenced = Repository.ReferencedProjectNames(project);
        foreach (var covered in new[] { "Backlog.Modules.DevPc.Abstractions", "Backlog.Modules.DevPc.UI", "Backlog.Infrastructure.DevPc" })
        {
            Assert.Contains(covered, referenced, StringComparer.OrdinalIgnoreCase);
        }

        foreach (var desktop in new[] { "Backlog.Desktop.UI", "Backlog.Desktop.WebHarness", "Backlog.Mobile.UI" })
        {
            Assert.DoesNotContain(desktop, referenced, StringComparer.OrdinalIgnoreCase);
        }
    }

    [Theory]
    [InlineData("Backlog.Modules.DevPc.Abstractions")]
    [InlineData("Backlog.Modules.DevPc.UI")]
    public void The_dev_pc_project_grants_its_internals_to_the_module_tests(string name)
    {
        var project = XDocument.Load(PathOf("src", "Modules", "DevPc", name, $"{name}.csproj"));
        var granted = project.Descendants("InternalsVisibleTo")
            .Select(grant => (string?)grant.Attribute("Include") ?? string.Empty)
            .ToList();

        Assert.Contains(ModuleTests, granted);
    }

    /// <summary>A file-scoped or block namespace declaration.</summary>
    private static readonly Regex Namespace = new(@"^namespace\s+([\w.]+)", RegexOptions.Compiled | RegexOptions.Multiline);

    private static string PathOf(params string[] segments) => Path.Combine([Repository.Root.FullName, .. segments]);
}
