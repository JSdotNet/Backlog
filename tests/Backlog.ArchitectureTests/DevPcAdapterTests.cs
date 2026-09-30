using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace Backlog.ArchitectureTests;

/// <summary>
/// Where Dev PC Management's tools adapter lives, and what its contract project
/// is left holding.
///
/// <para>The production <c>IDevToolService</c> used to sit inside the MAUI head,
/// with a second implementation in the web harness beside it, and the command
/// lines and parsers it runs sat in <c>Backlog.Modules.DevPc.Abstractions</c>.
/// A module declares a port and <c>src/Infrastructure</c> implements it, so the
/// adapter is <c>Backlog.Infrastructure.DevPc</c> now, both hosts compose that one
/// class, and the contract project holds contracts: the catalog types, the port,
/// its null object and the records a caller receives.</para>
/// </summary>
public class DevPcAdapterTests
{
    private const string Adapter = "Backlog.Infrastructure.DevPc";
    private const string AdapterTests = "Backlog.Infrastructure.DevPc.UnitTests";
    private const string Abstractions = "Backlog.Modules.DevPc.Abstractions";

    /// <summary>The only projects the adapter may see: the port it answers, the
    /// task store whose root the per-PC config resolves from, and nothing that
    /// renders a screen or hosts one.</summary>
    private static readonly string[] AllowedReferences = [Abstractions, "Backlog.Modules.Tasks.Abstractions"];

    [Fact]
    public void The_tools_adapter_is_an_infrastructure_project_and_not_part_of_the_desktop_head()
    {
        Assert.True(
            File.Exists(PathOf("src", "Infrastructure", Adapter, "DevToolService.cs")),
            $"DevToolService.cs is not in src/Infrastructure/{Adapter}.");

        Assert.False(
            File.Exists(PathOf("src", "App", "Backlog.Desktop", "Services", "DevToolService.cs")),
            "The desktop head still carries its own DevToolService.cs.");
    }

    [Fact]
    public void The_tools_adapter_references_only_the_ports_it_answers()
    {
        var project = AdapterProject();

        var references = Repository.ReferencedProjectNames(project).ToList();
        var unexpected = references
            .Where(reference => !AllowedReferences.Contains(reference, StringComparer.OrdinalIgnoreCase))
            .ToList();

        Assert.Contains(Abstractions, references, StringComparer.OrdinalIgnoreCase);
        Assert.True(
            unexpected.Count == 0,
            $"{Adapter} references projects outside {string.Join(", ", AllowedReferences)}: {string.Join(", ", unexpected)}");

        var packages = XDocument.Load(project.FullName)
            .Descendants("PackageReference")
            .Select(package => (string?)package.Attribute("Include") ?? string.Empty)
            .ToList();

        Assert.DoesNotContain(packages, package => package.Contains("Maui", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(packages, package => package.Contains("AspNetCore", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>The exhaustive-switch settings the contract project carries, for
    /// the same switches over <c>DevToolKind</c> and <c>DevToolProvider</c> — the
    /// adapter has more of them than the contract does.</summary>
    [Fact]
    public void The_tools_adapter_fails_the_build_on_an_unhandled_enum_member()
    {
        var project = XDocument.Load(AdapterProject().FullName);

        var errors = string.Join(';', project.Descendants("WarningsAsErrors").Select(element => element.Value));
        var silenced = string.Join(';', project.Descendants("NoWarn").Select(element => element.Value));

        Assert.Contains("CS8509", errors, StringComparison.Ordinal);
        Assert.Contains("CS8524", silenced, StringComparison.Ordinal);
    }

    [Fact]
    public void The_web_harness_composes_the_shared_adapter_rather_than_its_own()
    {
        var harness = new DirectoryInfo(PathOf("src", "Harness", "Backlog.Desktop.WebHarness"));
        Assert.True(harness.Exists, $"{harness.FullName} is not where the desktop web harness lives any more.");

        var implementations = harness
            .EnumerateFiles("*.cs", SearchOption.AllDirectories)
            .Where(file => !IsBuildOutput(file))
            .Where(file => Implements.IsMatch(File.ReadAllText(file.FullName)))
            .Select(file => Path.GetRelativePath(Repository.Root.FullName, file.FullName))
            .ToList();

        Assert.True(
            implementations.Count == 0,
            "The web harness declares its own IDevToolService. Compose the shared DevToolService from "
            + $"{Adapter} in its catalog-only configuration instead:\n" + string.Join('\n', implementations));

        var program = File.ReadAllText(Path.Combine(harness.FullName, "Program.cs"));
        Assert.Contains("DevToolService.CatalogOnly(", program, StringComparison.Ordinal);

        var references = Repository.ReferencedProjectNames(new FileInfo(Path.Combine(harness.FullName, "Backlog.Desktop.WebHarness.csproj")));
        Assert.Contains(Adapter, references, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void The_contract_project_holds_no_command_lines_or_their_parsers()
    {
        var source = AbstractionsSource();

        foreach (var moved in new[]
                 {
                     "class DevToolCommands",
                     "class DevToolRefresh",
                     "class DevToolClaudeDesktopConfig",
                     "record DevToolClaudeDesktopServer"
                 })
        {
            Assert.DoesNotContain(moved, source, StringComparison.Ordinal);
        }

        // And it still holds what it is for, so the rule above cannot pass by the
        // folder having been emptied.
        foreach (var kept in new[]
                 {
                     "interface IDevToolService",
                     "class UnsupportedDevToolService",
                     "class DevToolConfiguration",
                     "enum DevToolKind",
                     "enum DevToolVersionAuthority",
                     "record DevToolInfo(",
                     "record DevToolCatalog(",
                     "record DevToolCommand(",
                     "record DevToolActionResult("
                 })
        {
            Assert.Contains(kept, source, StringComparison.Ordinal);
        }
    }

    /// <summary>The endpoint placeholders stay internal to the contract, and the
    /// one assembly that expands them is the adapter now, not the desktop head.</summary>
    [Fact]
    public void The_endpoint_placeholders_are_granted_to_the_adapter_and_not_the_desktop_head()
    {
        var project = XDocument.Load(PathOf("src", "Modules", "DevPc", Abstractions, $"{Abstractions}.csproj"));
        var granted = project.Descendants("InternalsVisibleTo")
            .Select(grant => (string?)grant.Attribute("Include") ?? string.Empty)
            .ToList();

        Assert.Contains(Adapter, granted);
        Assert.DoesNotContain("Backlog.Desktop", granted);
    }

    [Fact]
    public void Both_new_projects_are_in_the_solution_and_its_filter()
    {
        var solution = File.ReadAllText(PathOf("Backlog.sln"));
        var filter = File.ReadAllText(PathOf("Backlog.WithoutAppHeads.slnf"));

        foreach (var path in new[]
                 {
                     $@"src\Infrastructure\{Adapter}\{Adapter}.csproj",
                     $@"tests\{AdapterTests}\{AdapterTests}.csproj"
                 })
        {
            Assert.Contains(path, solution, StringComparison.OrdinalIgnoreCase);
            Assert.Contains(path.Replace(@"\", @"\\"), filter, StringComparison.OrdinalIgnoreCase);
        }

        Assert.False(
            File.Exists(PathOf("tests", "Backlog.Desktop.UI.UnitTests", "DevToolTests.cs")),
            $"DevToolTests.cs belongs in tests/{AdapterTests} beside the code it covers.");
        Assert.True(File.Exists(PathOf("tests", AdapterTests, "DevToolTests.cs")));
    }

    /// <summary>A type declaration whose base list names the port.</summary>
    private static readonly Regex Implements = new(
        @"\b(class|record)\s+\w+[^{;]*:\s*[^{;]*\bIDevToolService\b",
        RegexOptions.Compiled);

    private static FileInfo AdapterProject()
    {
        var project = new FileInfo(PathOf("src", "Infrastructure", Adapter, $"{Adapter}.csproj"));
        Assert.True(project.Exists, $"{project.FullName} does not exist.");
        return project;
    }

    private static string AbstractionsSource()
    {
        var folder = new DirectoryInfo(PathOf("src", "Modules", "DevPc", Abstractions));
        return string.Join(
            '\n',
            folder.EnumerateFiles("*.cs", SearchOption.AllDirectories)
                .Where(file => !IsBuildOutput(file))
                .Select(file => File.ReadAllText(file.FullName)));
    }

    private static bool IsBuildOutput(FileInfo file) =>
        file.FullName.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}")
        || file.FullName.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}");

    private static string PathOf(params string[] segments) => Path.Combine([Repository.Root.FullName, .. segments]);
}
