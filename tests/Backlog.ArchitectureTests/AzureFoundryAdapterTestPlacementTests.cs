using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace Backlog.ArchitectureTests;

/// <summary>
/// Where the Azure Foundry adapter's tests live.
///
/// <para>They used to sit in <c>Backlog.Desktop.UI.UnitTests</c>, which also holds
/// the shell, the module screens and their composition, so a change to the
/// adapter rebuilt and reran the whole of that suite, and the adapter granted its
/// internals to a UI test assembly. Like every other adapter under
/// <c>src/Infrastructure</c>, it has a test project of its own now: the files that
/// test only the adapter live there, and the internals go to that project.</para>
/// </summary>
public class AzureFoundryAdapterTestPlacementTests
{
    private const string Adapter = "Backlog.Infrastructure.AzureFoundry";
    private const string AdapterTests = "Backlog.Infrastructure.AzureFoundry.UnitTests";
    private const string DesktopUiTests = "Backlog.Desktop.UI.UnitTests";

    /// <summary>The files that test the adapter alone, and nothing that renders.</summary>
    public static TheoryData<string> AdapterTestFiles() =>
    [
        "AzureFoundryTests.cs",
        "AzureFoundryCostClientTests.cs",
        "AzureFoundryRegistrationTests.cs",
        "AzureFoundryDashboardAdapterRegistrationTests.cs",
        "AzureFoundryPlanFixtureTests.cs",
        "FoundryMessagesTests.cs"
    ];

    [Theory]
    [MemberData(nameof(AdapterTestFiles))]
    public void The_adapter_test_lives_in_the_adapter_test_project(string file)
    {
        Assert.False(
            File.Exists(PathOf("tests", DesktopUiTests, file)),
            $"{file} tests only {Adapter}, so it belongs in tests/{AdapterTests} rather than tests/{DesktopUiTests}.");

        var moved = PathOf("tests", AdapterTests, file);
        Assert.True(File.Exists(moved), $"{file} is not in tests/{AdapterTests}.");

        var namespaces = Namespace.Matches(File.ReadAllText(moved)).Select(match => match.Groups[1].Value).ToList();
        Assert.Equal([AdapterTests], namespaces);
    }

    [Fact]
    public void The_adapter_test_project_is_in_the_solution_and_its_filter()
    {
        var solution = File.ReadAllText(PathOf("Backlog.sln"));
        var filter = File.ReadAllText(PathOf("Backlog.WithoutAppHeads.slnf"));
        var path = $@"tests\{AdapterTests}\{AdapterTests}.csproj";

        Assert.Contains(path, solution, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(path.Replace(@"\", @"\\"), filter, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>The project takes its runner, output type and coverage from
    /// <c>tests/Directory.Build.props</c> like every other test project, and
    /// references the adapter it covers.</summary>
    [Fact]
    public void The_adapter_test_project_inherits_the_test_defaults_and_references_the_adapter()
    {
        var project = new FileInfo(PathOf("tests", AdapterTests, $"{AdapterTests}.csproj"));
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

        Assert.Contains(Adapter, Repository.ReferencedProjectNames(project), StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void The_adapter_grants_its_internals_to_its_own_tests_and_not_the_desktop_ui_tests()
    {
        var project = XDocument.Load(PathOf("src", "Infrastructure", Adapter, $"{Adapter}.csproj"));
        var granted = project.Descendants("InternalsVisibleTo")
            .Select(grant => (string?)grant.Attribute("Include") ?? string.Empty)
            .ToList();

        Assert.Contains(AdapterTests, granted);
        Assert.DoesNotContain(DesktopUiTests, granted);
    }

    /// <summary>A file-scoped or block namespace declaration.</summary>
    private static readonly Regex Namespace = new(@"^namespace\s+([\w.]+)", RegexOptions.Compiled | RegexOptions.Multiline);

    private static string PathOf(params string[] segments) => Path.Combine([Repository.Root.FullName, .. segments]);
}
