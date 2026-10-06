using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Xml.Linq;

namespace Backlog.ArchitectureTests;

/// <summary>
/// <see cref="ModuleBoundaryTests.A_module_never_references_infrastructure"/>
/// read off the compiled assemblies instead of the csproj files.
///
/// <para>The csproj rules read one project's <c>ProjectReference</c> lines, so a
/// module that reaches an adapter through another project — a package, or a
/// project that is not itself a module — passes every one of them. The
/// compiler records what an assembly actually references, and the closure of
/// those references is what a module can really see. This walks it
/// (issue #741).</para>
///
/// <para>This project references nothing on purpose (see its csproj), so the
/// assemblies are found where their own builds put them —
/// <c>bin/&lt;Configuration&gt;/&lt;TargetFramework&gt;</c> beside each
/// project, the same configuration and framework this test was built with — and
/// read with <see cref="System.Reflection.Metadata"/>, which ships in the box,
/// rather than loaded. A missing assembly is a failure that says to build first,
/// never a quiet pass.</para>
/// </summary>
public class ModuleAssemblyReferenceTests
{
    private const string InfrastructurePrefix = "Backlog.Infrastructure.";

    [Fact]
    public void No_module_reaches_infrastructure_through_its_compiled_references()
    {
        var domain = DomainProjects().ToList();

        Assert.NotEmpty(domain);

        var missing = new List<string>();
        var offenders = new List<string>();

        foreach (var project in domain)
        {
            var name = Path.GetFileNameWithoutExtension(project.Name);
            var output = BuildOutputOf(project);

            if (!File.Exists(Path.Combine(output.FullName, name + ".dll")))
            {
                missing.Add($"{name}: {output.FullName}");
                continue;
            }

            offenders.AddRange(InfrastructureReachedFrom(name, assembly =>
            {
                // Referenced projects are copied beside the one that references
                // them, so the module's own output folder holds its whole closure.
                var path = Path.Combine(output.FullName, assembly + ".dll");

                if (File.Exists(path)) return ReferencedAssemblyNames(path);

                missing.Add($"{assembly} (referenced from the closure of {name}): {path}");
                return [];
            }));
        }

        Assert.True(
            missing.Count == 0,
            "These assemblies are not built, so their references cannot be read. Build the solution "
            + $"({Configuration}, {TargetFramework}) before running this test:\n" + string.Join('\n', missing));

        Assert.True(
            offenders.Count == 0,
            "Modules declare ports; infrastructure implements them. These modules reach an adapter "
            + "through what they reference, even where no csproj names it:\n" + string.Join('\n', offenders));
    }

    /// <summary>
    /// The walk is only as good as its reach, and a walk that stopped one step
    /// short would turn the rule green rather than red. This hands it an adapter
    /// two references away, behind a framework assembly it must not follow.
    /// </summary>
    [Fact]
    public void The_closure_finds_an_adapter_behind_another_project()
    {
        var references = new Dictionary<string, string[]>(StringComparer.Ordinal)
        {
            ["Backlog.Modules.Example"] = ["System.Runtime", "Backlog.Modules.Example.Abstractions"],
            ["Backlog.Modules.Example.Abstractions"] = ["Backlog.SharedKernel"],
            ["Backlog.SharedKernel"] = ["Backlog.Infrastructure.Example"],
            ["Backlog.Infrastructure.Example"] = []
        };

        var reached = InfrastructureReachedFrom("Backlog.Modules.Example", name => references[name]);

        Assert.Equal(
            "Backlog.Modules.Example -> Backlog.Modules.Example.Abstractions -> Backlog.SharedKernel -> Backlog.Infrastructure.Example",
            Assert.Single(reached));
    }

    /// <summary>
    /// The compiled-reference rule depends on this project referencing nothing:
    /// a reference here would put the modules' assemblies in this project's own
    /// output, and a package would be a second way of reading them. Both are
    /// what the csproj comment rules out; this holds it to that.
    /// </summary>
    [Fact]
    public void The_architecture_project_references_no_project_and_no_package()
    {
        var csproj = Path.Combine(Repository.Root.FullName, "tests", "Backlog.ArchitectureTests", "Backlog.ArchitectureTests.csproj");

        Assert.True(File.Exists(csproj), $"{csproj} is not where the architecture tests live any more.");

        var references = XDocument.Load(csproj).Descendants()
            .Where(element => element.Name.LocalName is "ProjectReference" or "PackageReference")
            .Select(element => (string?)element.Attribute("Include"))
            .ToList();

        Assert.True(
            references.Count == 0,
            "Backlog.ArchitectureTests references " + string.Join(", ", references) + "; it reads the "
            + "repository and the built assemblies by path instead.");
    }

    /// <summary>
    /// Every <c>Backlog.Infrastructure.*</c> assembly reachable from
    /// <paramref name="start"/> through <c>Backlog.*</c> references, each as the
    /// path that reaches it. Framework and package assemblies are not followed:
    /// they cannot reference ours.
    /// </summary>
    internal static List<string> InfrastructureReachedFrom(string start, Func<string, IEnumerable<string>> referencesOf)
    {
        var cameFrom = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase) { [start] = null };
        var queue = new Queue<string>([start]);
        var reached = new List<string>();

        while (queue.TryDequeue(out var current))
        {
            foreach (var reference in referencesOf(current).Where(name => name.StartsWith("Backlog.", StringComparison.Ordinal)))
            {
                if (!cameFrom.TryAdd(reference, current)) continue;

                if (reference.StartsWith(InfrastructurePrefix, StringComparison.Ordinal))
                {
                    reached.Add(PathTo(reference));
                    continue;
                }

                queue.Enqueue(reference);
            }
        }

        return reached;

        string PathTo(string name)
        {
            var steps = new List<string>();
            for (string? step = name; step is not null; step = cameFrom[step]) steps.Add(step);
            steps.Reverse();
            return string.Join(" -> ", steps);
        }
    }

    /// <summary>The names of the assemblies the one at <paramref name="path"/> was
    /// compiled against, read from its metadata without loading it.</summary>
    private static List<string> ReferencedAssemblyNames(string path)
    {
        using var stream = File.OpenRead(path);
        using var image = new PEReader(stream);

        var metadata = image.GetMetadataReader();

        return [.. metadata.AssemblyReferences
            .Select(handle => metadata.GetString(metadata.GetAssemblyReference(handle).Name))];
    }

    /// <summary>The projects that own a decision: everything under
    /// <c>src/Modules</c> but the <c>.UI</c> screens and the <c>.Api</c> host — the
    /// same set <see cref="ModuleBoundaryTests"/> holds to its csproj rule.</summary>
    private static IEnumerable<FileInfo> DomainProjects() =>
        Repository.ProjectsUnder("src", "Modules")
            .Where(project => !Repository.IsUserInterface(project)
                           && !project.Name.EndsWith(".Api.csproj", StringComparison.OrdinalIgnoreCase));

    private static DirectoryInfo BuildOutputOf(FileInfo project) =>
        new(Path.Combine(project.DirectoryName!, "bin", Configuration, TargetFramework));

    /// <summary>The folder this test runs from is
    /// <c>bin/&lt;Configuration&gt;/&lt;TargetFramework&gt;</c>; the modules were
    /// built the same way in the same build.</summary>
    private static DirectoryInfo TestOutput { get; } =
        new(AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));

    private static string TargetFramework => TestOutput.Name;

    private static string Configuration => TestOutput.Parent!.Name;
}
