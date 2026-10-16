using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace Backlog.ArchitectureTests;

/// <summary>
/// Where the Sessions module's tests live.
///
/// <para>They used to sit in <c>Backlog.Desktop.UI.UnitTests</c>, which also holds
/// the shell, the other module screens and their composition, so a change to
/// Sessions rebuilt and reran the whole of that suite. The files that test only
/// <c>Backlog.Modules.Sessions.Abstractions</c> and <c>Backlog.Modules.Sessions.UI</c>
/// (with the readers in <c>Backlog.Infrastructure.Sessions</c> behind the pane) live in
/// a test project of their own now. The Sessions tests that need the desktop shell,
/// its MCP endpoints, the Dashboard or Devbook stay where they were.</para>
/// </summary>
public class SessionsModuleTestPlacementTests
{
    private const string ModuleTests = "Backlog.Modules.Sessions.UnitTests";
    private const string DesktopUiTests = "Backlog.Desktop.UI.UnitTests";

    /// <summary>The files that test the Sessions module alone.</summary>
    public static TheoryData<string> ModuleTestFiles() =>
    [
        "AgentSessionEnvironmentTests.cs",
        "AgentSessionGroupTests.cs",
        "AgentSessionViewTests.cs",
        "SessionListTests.cs",
        "SessionRowsTests.cs",
        "DeliveryRunMergingTests.cs",
        "DeliveryEconomicsPageTests.cs",
        "DeliveryRunStagesTests.cs",
        "SessionsAiContentSourceTests.cs",
        "SessionsPaneTests.cs",
        "SessionsPaneDetailTests.cs",
        "SessionsPaneLoadStatesTests.cs",
        "SessionsPaneRunTests.cs",
        "ClaudeCodeTelemetrySettingsTests.cs",
        "ModelPriceSettingsTests.cs"
    ];

    /// <summary>The Sessions tests that depend on the desktop shell, its MCP
    /// endpoints, Dashboard.UI or Devbook.UI, and so stay behind.</summary>
    public static TheoryData<string> ShellDependentTestFiles() =>
    [
        "SessionsSurfaceActivatorTests.cs",
        "SessionPullRequestStatusesTests.cs",
        "DeliveryRunTelemetryTests.cs",
        "ClaudeCodeOtlpLogsTests.cs",
        "DashboardSessionsTabTests.cs",
        "SessionDevbookAnnotationStoreTests.cs"
    ];

    [Theory]
    [MemberData(nameof(ModuleTestFiles))]
    public void The_module_test_lives_in_the_module_test_project(string file)
    {
        Assert.False(
            File.Exists(PathOf("tests", DesktopUiTests, file)),
            $"{file} tests only the Sessions module, so it belongs in tests/{ModuleTests} rather than tests/{DesktopUiTests}.");

        var moved = PathOf("tests", ModuleTests, file);
        Assert.True(File.Exists(moved), $"{file} is not in tests/{ModuleTests}.");

        var namespaces = Namespace.Matches(File.ReadAllText(moved)).Select(match => match.Groups[1].Value).ToList();
        Assert.Equal([ModuleTests], namespaces);
    }

    [Theory]
    [MemberData(nameof(ShellDependentTestFiles))]
    public void The_shell_dependent_test_stays_in_the_desktop_ui_tests(string file)
    {
        Assert.True(File.Exists(PathOf("tests", DesktopUiTests, file)), $"{file} is not in tests/{DesktopUiTests}.");
        Assert.False(File.Exists(PathOf("tests", ModuleTests, file)), $"{file} depends on the desktop shell, so it does not belong in tests/{ModuleTests}.");
    }

    /// <summary>The helpers other test classes borrow — <c>SessionRowsTests.Run</c>,
    /// <c>SessionsPaneTests.StubRunSource</c>, <c>StubPriceStore</c>
    /// and <c>StubRequestStore</c> — moved with the files that declare them rather than
    /// being copied, so each is declared once across the two projects and tests/Shared,
    /// where <c>StubPriceStore</c> lives because the Settings badge tests use it too.</summary>
    [Theory]
    [InlineData(@"class\s+StubRunSource\b")]
    [InlineData(@"class\s+StubPriceStore\b")]
    [InlineData(@"class\s+StubRequestStore\b")]
    [InlineData(@"internal\s+static\s+DeliveryRun\s+Run\(")]
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
    /// references the Sessions projects it covers but not the desktop app or its harness.</summary>
    [Fact]
    public void The_module_test_project_references_sessions_and_not_the_desktop_app()
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
        foreach (var covered in new[] { "Backlog.Modules.Sessions.Abstractions", "Backlog.Modules.Sessions.UI", "Backlog.Infrastructure.Sessions" })
        {
            Assert.Contains(covered, referenced, StringComparer.OrdinalIgnoreCase);
        }

        foreach (var desktop in new[] { "Backlog.Desktop.UI", "Backlog.Desktop.WebHarness" })
        {
            Assert.DoesNotContain(desktop, referenced, StringComparer.OrdinalIgnoreCase);
        }
    }

    [Theory]
    [InlineData("Modules", "Sessions", "Backlog.Modules.Sessions.Abstractions")]
    [InlineData("Modules", "Sessions", "Backlog.Modules.Sessions.UI")]
    public void The_sessions_project_grants_its_internals_to_the_module_tests(string area, string module, string name)
    {
        var project = XDocument.Load(PathOf("src", area, module, name, $"{name}.csproj"));
        var granted = project.Descendants("InternalsVisibleTo")
            .Select(grant => (string?)grant.Attribute("Include") ?? string.Empty)
            .ToList();

        Assert.Contains(ModuleTests, granted);
    }

    /// <summary>A file-scoped or block namespace declaration.</summary>
    private static readonly Regex Namespace = new(@"^namespace\s+([\w.]+)", RegexOptions.Compiled | RegexOptions.Multiline);

    private static string PathOf(params string[] segments) => Path.Combine([Repository.Root.FullName, .. segments]);
}
