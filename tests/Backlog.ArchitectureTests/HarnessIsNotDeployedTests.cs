using System.Xml.Linq;

namespace Backlog.ArchitectureTests;

/// <summary>
/// The harness projects are development-time hosts. Comments and README files say
/// so, but a comment cannot fail a build — these tests can. If someone wires a
/// harness into shipped code, or makes one publishable, this is what stops it.
/// </summary>
public class HarnessIsNotDeployedTests
{
    [Fact]
    public void No_shipped_project_references_a_harness()
    {
        var harnesses = Repository.ProjectsUnder("harness")
            .Select(p => Path.GetFileNameWithoutExtension(p.Name))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        Assert.NotEmpty(harnesses);

        var offenders = Repository.ProjectsUnder("src")
            .Where(project => Repository.ReferencedProjectNames(project).Any(harnesses.Contains))
            .Select(project => project.Name)
            .ToList();

        Assert.True(
            offenders.Count == 0,
            "Projects under src/ must never reference a harness: " + string.Join(", ", offenders));
    }

    [Theory]
    [InlineData("IsPublishable")]
    [InlineData("IsPackable")]
    [InlineData("IsShippingAssembly")]
    public void Every_harness_project_is_marked_not_shippable(string property)
    {
        var props = XDocument.Load(Path.Combine(Repository.Root.FullName, "harness", "Directory.Build.props"));

        var value = props.Descendants(property).Select(e => e.Value).SingleOrDefault();

        Assert.Equal("false", value, ignoreCase: true);
    }

    [Fact]
    public void Harness_projects_live_outside_src_and_tests()
    {
        var harnessFolder = Path.Combine(Repository.Root.FullName, "harness");

        Assert.True(Directory.Exists(harnessFolder), "harness/ must sit next to src/ and tests/.");

        var strays = Repository.ProjectsUnder("src")
            .Concat(Repository.ProjectsUnder("tests"))
            .Where(p => p.Name.Contains("Harness", StringComparison.OrdinalIgnoreCase))
            .Select(p => p.Name)
            .ToList();

        Assert.True(
            strays.Count == 0,
            "Harness projects belong in harness/, not src/ or tests/: " + string.Join(", ", strays));
    }
}
