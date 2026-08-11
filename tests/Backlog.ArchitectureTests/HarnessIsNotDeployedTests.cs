using System.Xml.Linq;

namespace Backlog.ArchitectureTests;

/// <summary>
/// The harness projects are development-time hosts. Comments and README files say
/// so, but a comment cannot fail a build — these tests can. If someone wires a
/// harness into shipped code, or makes one publishable, this is what stops it.
/// </summary>
public class HarnessIsNotDeployedTests
{
    private static readonly string[] HarnessPath = ["src", "harness"];

    [Fact]
    public void No_shipped_project_references_a_harness()
    {
        var harnesses = Repository.ProjectsUnder(HarnessPath)
            .Select(p => Path.GetFileNameWithoutExtension(p.Name))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        Assert.NotEmpty(harnesses);

        var harnessRoot = Path.Combine([Repository.Root.FullName, .. HarnessPath]);
        var offenders = Repository.ProjectsUnder("src")
            .Where(project => !project.FullName.StartsWith(harnessRoot, StringComparison.OrdinalIgnoreCase))
            .Where(project => Repository.ReferencedProjectNames(project).Any(harnesses.Contains))
            .Select(project => project.Name)
            .ToList();

        Assert.True(
            offenders.Count == 0,
            "Shipping projects under src/ must never reference a harness: " + string.Join(", ", offenders));
    }

    [Theory]
    [InlineData("IsPublishable")]
    [InlineData("IsPackable")]
    [InlineData("IsShippingAssembly")]
    public void Every_harness_project_is_marked_not_shippable(string property)
    {
        var props = XDocument.Load(Path.Combine([Repository.Root.FullName, .. HarnessPath, "Directory.Build.props"]));

        var value = props.Descendants(property).Select(e => e.Value).SingleOrDefault();

        Assert.Equal("false", value, ignoreCase: true);
    }

    [Fact]
    public void Harness_projects_live_under_src_harness_and_outside_tests()
    {
        var harnessFolder = Path.Combine([Repository.Root.FullName, .. HarnessPath]);

        Assert.True(Directory.Exists(harnessFolder), "Harness projects must live under src/harness/.");
        Assert.DoesNotContain(
            Repository.ProjectsUnder("tests"),
            p => p.Name.Contains("Harness", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(
            Repository.ProjectsUnder("src"),
            p => p.Name.Contains("Harness", StringComparison.OrdinalIgnoreCase)
                && !p.FullName.StartsWith(harnessFolder, StringComparison.OrdinalIgnoreCase));
    }
}
