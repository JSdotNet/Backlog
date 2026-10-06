namespace Backlog.ArchitectureTests;

/// <summary>
/// The shape of <c>src/Infrastructure</c> as a whole: its project references
/// run one way, and no adapter becomes the place every module meets.
///
/// <para><see cref="ModuleBoundaryTests.Backlog_Infrastructure_GitHub_never_reaches_for_the_file_system_adapter"/>
/// pins one edge. <c>Backlog.Infrastructure.FileSystem</c> went on to reference
/// the Devbook and Sqlite adapters as well and eight module projects, and nothing
/// noticed, because nothing looked at the graph rather than at an edge
/// (issue #741). These rules look at the graph.</para>
/// </summary>
public class InfrastructureGraphTests
{
    /// <summary>
    /// How many projects under <c>src/Modules</c> one infrastructure project may
    /// reference. Four is today's widest adapter that is still one adapter:
    /// <c>Backlog.Infrastructure.Mcp</c> answers the agent tools over Tasks,
    /// Roadmap, Devbook and Sessions. Past that an adapter is usually several
    /// adapters sharing a project.
    /// </summary>
    internal const int MaxModuleReferences = 4;

    /// <summary>
    /// The infrastructure projects allowed past <see cref="MaxModuleReferences"/>,
    /// each with the number it is allowed and why. The number is a ceiling as
    /// well as a record: a project that takes another module reference has to
    /// change it here, in writing.
    /// </summary>
    internal static readonly (string Project, int ModuleReferences, string Reason)[] WideAdapters =
    [
        ("Backlog.Infrastructure.FileSystem", 8,
            "The workspace folder's stores — feature settings, device identity, transcript caches, devbook "
            + "snapshots, working hours, planning velocity — each answer a different module's port from the "
            + "one folder that knows the workspace root. It is several adapters in one project; splitting "
            + "Workspace/ by module is what brings it under the cap."),
        ("Backlog.Infrastructure.Sync", 6,
            "The sync client carries every synchronised context's records — Tasks, Inbox, Roadmap, Devbook "
            + "and Sessions — over the Sync module's own contract, so it sees each context's published "
            + "surface by design.")
    ];

    /// <summary>
    /// A cycle between two adapters fails the build, so the build is not what
    /// this stops. What it stops is the fix somebody reaches for to make a cycle
    /// legal — a shared project split out from under both, which is the cycle
    /// with an extra node — and it says which projects form the loop, which the
    /// compiler's error does not.
    /// </summary>
    [Fact]
    public void The_infrastructure_reference_graph_is_acyclic()
    {
        var graph = InfrastructureGraph();

        Assert.NotEmpty(graph);

        var cycle = FindCycle(graph);

        Assert.True(
            cycle is null,
            "The project references under src/Infrastructure form a cycle: "
            + (cycle is null ? string.Empty : string.Join(" -> ", cycle)));
    }

    /// <summary>
    /// An adapter that references every module is where every module meets, and
    /// a meeting point is the shared layer the module split took away. Over the
    /// cap is allowed only with a reason in <see cref="WideAdapters"/>.
    /// </summary>
    [Fact]
    public void No_infrastructure_project_references_more_module_projects_than_the_cap()
    {
        var counts = ModuleReferenceCounts();

        Assert.NotEmpty(counts);

        var offenders = OverTheCap(counts);

        Assert.True(
            offenders.Count == 0,
            $"These infrastructure projects reference more than {MaxModuleReferences} projects under "
            + "src/Modules without a reason in " + nameof(WideAdapters) + ", or more than the reason "
            + "allows. Split the adapter, or write down why it is one:\n" + string.Join('\n', offenders));
    }

    /// <summary>
    /// An exception that has stopped being one is worse than no exception list:
    /// it reads as a considered decision while quietly permitting anything. A
    /// wide adapter that narrowed to the cap, or is gone, leaves the table.
    /// </summary>
    [Fact]
    public void Every_wide_adapter_is_still_wider_than_the_cap()
    {
        var counts = ModuleReferenceCounts();

        var stale = WideAdapters
            .Where(wide => !counts.TryGetValue(wide.Project, out var count) || count <= MaxModuleReferences)
            .Select(wide => counts.TryGetValue(wide.Project, out var count)
                ? $"{wide.Project} references {count} module projects now"
                : $"{wide.Project} is not under src/Infrastructure any more")
            .ToList();

        Assert.True(
            stale.Count == 0,
            "These entries in " + nameof(WideAdapters) + " no longer describe an adapter past the cap "
            + "and should be deleted:\n" + string.Join('\n', stale));
    }

    /// <summary>
    /// The two rules above are only as good as the helpers they ask, and a
    /// helper that stopped finding anything would turn them green rather than
    /// red. These feed each one a graph with the defect in it.
    /// </summary>
    [Fact]
    public void FindCycle_names_every_project_in_the_loop()
    {
        var graph = new Dictionary<string, IReadOnlyCollection<string>>(StringComparer.OrdinalIgnoreCase)
        {
            ["A"] = ["B"],
            ["B"] = ["C"],
            ["C"] = ["A"],
            ["D"] = ["A"]
        };

        var cycle = FindCycle(graph);

        Assert.NotNull(cycle);
        Assert.Equal(cycle[0], cycle[^1]);
        Assert.Equal(new[] { "A", "B", "C" }, cycle.Skip(1).Order(StringComparer.Ordinal));
    }

    /// <inheritdoc cref="FindCycle_names_every_project_in_the_loop" />
    [Fact]
    public void FindCycle_finds_nothing_in_a_graph_that_only_fans_in()
    {
        var graph = new Dictionary<string, IReadOnlyCollection<string>>(StringComparer.OrdinalIgnoreCase)
        {
            ["A"] = ["B", "C"],
            ["B"] = ["C"],
            ["C"] = []
        };

        Assert.Null(FindCycle(graph));
    }

    /// <inheritdoc cref="FindCycle_names_every_project_in_the_loop" />
    [Fact]
    public void OverTheCap_reports_an_unlisted_adapter_and_one_past_its_ceiling()
    {
        var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            ["Backlog.Infrastructure.Narrow"] = MaxModuleReferences,
            ["Backlog.Infrastructure.Wide"] = MaxModuleReferences + 1,
            [WideAdapters[0].Project] = WideAdapters[0].ModuleReferences + 1,
            [WideAdapters[1].Project] = WideAdapters[1].ModuleReferences
        };

        var offenders = OverTheCap(counts);

        Assert.Equal(2, offenders.Count);
        Assert.Contains(offenders, line => line.StartsWith("Backlog.Infrastructure.Wide ", StringComparison.Ordinal));
        Assert.Contains(offenders, line => line.StartsWith(WideAdapters[0].Project + " ", StringComparison.Ordinal));
    }

    /// <summary>Each project under <c>src/Infrastructure</c>, with the projects
    /// under <c>src/Infrastructure</c> it references.</summary>
    private static Dictionary<string, IReadOnlyCollection<string>> InfrastructureGraph()
    {
        var projects = Repository.ProjectsUnder("src", "Infrastructure").ToList();
        var names = projects
            .Select(project => Path.GetFileNameWithoutExtension(project.Name))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        return projects.ToDictionary(
            project => Path.GetFileNameWithoutExtension(project.Name),
            project => (IReadOnlyCollection<string>)[.. Repository.ReferencedProjectNames(project).Where(names.Contains)],
            StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>Each project under <c>src/Infrastructure</c>, with how many
    /// distinct projects under <c>src/Modules</c> it references. The shared
    /// kernel and the component library are under <c>src/Core</c> and do not
    /// count.</summary>
    private static Dictionary<string, int> ModuleReferenceCounts()
    {
        var modules = Repository.ProjectsUnder("src", "Modules")
            .Select(project => Path.GetFileNameWithoutExtension(project.Name))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        return Repository.ProjectsUnder("src", "Infrastructure").ToDictionary(
            project => Path.GetFileNameWithoutExtension(project.Name),
            project => Repository.ReferencedProjectNames(project)
                .Where(modules.Contains)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Count(),
            StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>The projects in <paramref name="counts"/> past the cap with no
    /// entry in <see cref="WideAdapters"/>, or past the ceiling their entry
    /// gives.</summary>
    internal static List<string> OverTheCap(IReadOnlyDictionary<string, int> counts) =>
        [.. counts
            .Where(pair => pair.Value > MaxModuleReferences)
            .Where(pair => !WideAdapters.Any(wide =>
                wide.Project.Equals(pair.Key, StringComparison.OrdinalIgnoreCase)
                && pair.Value <= wide.ModuleReferences))
            .OrderBy(pair => pair.Key, StringComparer.Ordinal)
            .Select(pair => $"{pair.Key} references {pair.Value} module projects")];

    /// <summary>
    /// A cycle in <paramref name="graph"/> as the path that closes it — the
    /// first project repeated at the end — or <see langword="null"/> when the
    /// graph has none. A depth-first walk that remembers the path it is on: a
    /// reference back into that path is the cycle.
    /// </summary>
    internal static List<string>? FindCycle(IReadOnlyDictionary<string, IReadOnlyCollection<string>> graph)
    {
        var done = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var path = new List<string>();

        foreach (var start in graph.Keys.Order(StringComparer.Ordinal))
        {
            var cycle = Visit(start);
            if (cycle is not null) return cycle;
        }

        return null;

        List<string>? Visit(string node)
        {
            var onPath = path.FindIndex(step => step.Equals(node, StringComparison.OrdinalIgnoreCase));
            if (onPath >= 0) return [.. path[onPath..], node];
            if (!done.Add(node)) return null;

            path.Add(node);

            foreach (var next in graph.TryGetValue(node, out var references) ? references : [])
            {
                var cycle = Visit(next);
                if (cycle is not null) return cycle;
            }

            path.RemoveAt(path.Count - 1);
            return null;
        }
    }
}
