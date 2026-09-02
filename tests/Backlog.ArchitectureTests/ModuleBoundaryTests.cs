namespace Backlog.ArchitectureTests;

/// <summary>
/// Boundary rules for the modular monolith. Modules own domain behaviour; the
/// adapters they need are cross-cutting and live in <c>src/Infrastructure</c>,
/// so a module must never be able to see one.
///
/// <para>A module folder holds two kinds of project, and only one of them is a
/// module. <see cref="DomainProjects"/> — <c>Backlog.Modules.X</c> and its
/// <c>.Abstractions</c> — own the decision, which is why they declare ports
/// instead of reaching for adapters and why they may not see a sibling context
/// at all. <see cref="PresentationProjects"/> — the <c>.UI</c> projects — own no
/// decision; each renders one context's screens and sits under the module folder
/// because that is the context it renders. Handing a screen an adapter is what a
/// host does, so the rules below say something different about them rather than
/// nothing: a screen may take an adapter, but it still may not reach past
/// another context's published surface into its implementation, and the
/// dependency between a module and its own UI runs one way.</para>
/// </summary>
public class ModuleBoundaryTests
{
    /// <summary>
    /// The cross-context references between module UI projects that
    /// <c>.domain/context-map.md</c> already carries as a relationship, each
    /// with the relationship that justifies it written beside it.
    ///
    /// <para>Adding an entry here is a claim that the map says these two
    /// contexts talk, and that the reference is the consumer conforming to what
    /// the other publishes rather than reading its internals. If the map does
    /// not say so, the map is the thing to change first — and if the answer is
    /// that the two panes merely want the same data, the data belongs in a
    /// place underneath both of them.</para>
    /// </summary>
    private static readonly (string From, string To, string Relationship)[] AllowedCrossContextUi =
    [
        ("Backlog.Modules.Backlog.UI", "Backlog.Modules.Inbox.UI",
            "Conformist: BacklogDrafts converts repository-authored rows into the Inbox's published "
            + "InboxItem contract. Backlog Management conforms to what the Inbox publishes; the Inbox "
            + "never reads back.")
    ];

    [Fact]
    public void A_module_never_references_infrastructure()
    {
        var infrastructure = InfrastructureNames();

        Assert.NotEmpty(infrastructure);

        var offenders = DomainProjects()
            .Where(module => Repository.ReferencedProjectNames(module).Any(infrastructure.Contains))
            .Select(module => module.Name)
            .ToList();

        Assert.True(
            offenders.Count == 0,
            "Modules declare ports; infrastructure implements them, never the other way round: "
            + string.Join(", ", offenders));
    }

    /// <summary>
    /// The counterpart for the presentation side. A <c>.UI</c> project is not
    /// declaring a port — it is a screen, and a screen legitimately talks to an
    /// adapter, exactly as the shell and the mobile app do. What it may not do
    /// is skip a sibling context's published surface: that is the same "ask the
    /// module rather than reach past it" line the rule above draws, at the one
    /// place it still applies once the adapter clause is gone.
    ///
    /// <para>Its own module's implementation is left out of this rule rather
    /// than permitted by it: that one is the subject of
    /// <see cref="ModuleSurfaceTests.The_desktop_side_sees_only_the_published_surface"/>,
    /// which forbids it across the whole presentation side at once.</para>
    /// </summary>
    [Fact]
    public void A_module_ui_may_take_an_adapter_but_never_another_modules_implementation()
    {
        var implementations = ImplementationNames();

        Assert.NotEmpty(implementations);

        var offenders = new List<string>();

        foreach (var project in PresentationProjects())
        {
            var self = Path.GetFileNameWithoutExtension(project.Name);

            offenders.AddRange(Repository.ReferencedProjectNames(project)
                .Where(implementations.Contains)
                .Where(reference => !reference.Equals(OwningModuleOf(self), StringComparison.OrdinalIgnoreCase))
                .Select(reference => $"{project.Name} -> {reference}"));
        }

        Assert.True(
            offenders.Count == 0,
            "A screen asks another context through its .Abstractions; referencing the implementation puts "
            + "that context's aggregate and handlers back within reach of a component: "
            + string.Join(", ", offenders));
    }

    /// <summary>
    /// A context's UI may render another context's screens only where the
    /// context map says the two contexts talk. Everything not in
    /// <see cref="AllowedCrossContextUi"/> is a pane reaching sideways, which is
    /// how two contexts quietly become one.
    /// </summary>
    [Fact]
    public void A_module_ui_reaches_into_another_context_only_where_the_context_map_says_so()
    {
        var presentation = PresentationProjects().ToList();
        var names = presentation
            .Select(project => Path.GetFileNameWithoutExtension(project.Name))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        Assert.NotEmpty(names);

        var offenders = new List<string>();

        foreach (var project in presentation)
        {
            var self = Path.GetFileNameWithoutExtension(project.Name);

            offenders.AddRange(Repository.ReferencedProjectNames(project)
                .Where(names.Contains)
                .Where(reference => !reference.Equals(self, StringComparison.OrdinalIgnoreCase))
                .Where(reference => !IsAllowedCrossContextEdge(self, reference))
                .Select(reference => $"{self} -> {reference}"));
        }

        Assert.True(
            offenders.Count == 0,
            "These context UIs reach into another context without a relationship in "
            + ".domain/context-map.md to stand on: " + string.Join(", ", offenders));
    }

    /// <summary>
    /// A screen asks its own module and takes whatever adapters it needs; what it
    /// does not do is consume another module's published surface.
    ///
    /// <para>This is the rule the workspace refactor was for. There used to be a
    /// <c>Backlog.Desktop.Workspace</c> project underneath all three contexts,
    /// and because everyone could read it, everyone did: Second Brain's panels
    /// took the backlog root store and Backlog Management's list took the
    /// knowledge-folder resolver. Splitting those four types into ports made the
    /// coupling visible, and the obvious next move — port each type into the
    /// module that owns it — would only have converted a shared project into two
    /// crossing references, <c>Knowledge.UI -&gt; Backlog.Abstractions</c> and
    /// <c>Backlog.UI -&gt; Knowledge.Abstractions</c>. Both are exactly what
    /// <see cref="A_module_ui_reaches_into_another_context_only_where_the_context_map_says_so"/>
    /// forbids between <c>.UI</c> projects, and neither would have tripped it,
    /// because an <c>.Abstractions</c> project is not a <c>.UI</c> project.</para>
    ///
    /// <para>So the answer was to push the join down: an infrastructure adapter
    /// may see both contexts and answer both ports, and each screen asks only its
    /// own module. That is a property nothing asserted until now — this asserts
    /// it. It deliberately says nothing about infrastructure or the shared kernel:
    /// a screen taking an adapter is the subject of the rule above, and the kernel
    /// is what everything may see.</para>
    /// </summary>
    [Fact]
    public void A_module_ui_asks_only_its_own_modules_published_surface()
    {
        var abstractions = Repository.ProjectsUnder("src", "Modules")
            .Select(project => Path.GetFileNameWithoutExtension(project.Name))
            .Where(name => name.EndsWith(".Abstractions", StringComparison.Ordinal))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        Assert.NotEmpty(abstractions);

        var offenders = new List<string>();

        foreach (var project in PresentationProjects())
        {
            var self = OwningModuleOf(Path.GetFileNameWithoutExtension(project.Name));

            offenders.AddRange(Repository.ReferencedProjectNames(project)
                .Where(abstractions.Contains)
                .Where(reference => !OwningModuleOf(reference).Equals(self, StringComparison.OrdinalIgnoreCase))
                .Select(reference => $"{project.Name} -> {reference}"));
        }

        Assert.True(
            offenders.Count == 0,
            "A screen renders one context and asks that context's module. Consuming another module's "
            + "published surface is the same sideways reach as consuming its UI, one layer down where "
            + "the cross-context rule cannot see it — put the join in an adapter that answers both "
            + "modules' ports instead: " + string.Join(", ", offenders));
    }

    /// <summary>
    /// An exception that has stopped being one is worse than no exception list:
    /// it reads as a considered decision while quietly permitting anything.
    /// </summary>
    [Fact]
    public void Every_allowed_cross_context_edge_is_still_a_reference()
    {
        var edges = PresentationProjects()
            .SelectMany(project => Repository.ReferencedProjectNames(project)
                .Select(reference => (From: Path.GetFileNameWithoutExtension(project.Name), To: reference)))
            .ToHashSet();

        Assert.NotEmpty(edges);

        var stale = AllowedCrossContextUi
            .Where(allowed => !edges.Contains((allowed.From, allowed.To)))
            .Select(allowed => $"{allowed.From} -> {allowed.To}")
            .ToList();

        Assert.True(
            stale.Count == 0,
            "These cross-context exceptions no longer describe a reference that exists and should be "
            + "deleted: " + string.Join(", ", stale));
    }

    [Fact]
    public void A_module_never_references_another_module()
    {
        var modules = Repository.ProjectsUnder("src", "Modules").ToList();
        var names = modules.Select(m => Path.GetFileNameWithoutExtension(m.Name)).ToList();

        foreach (var module in DomainProjects())
        {
            var self = Path.GetFileNameWithoutExtension(module.Name);

            var otherModules = Repository.ReferencedProjectNames(module)
                .Where(reference => names.Contains(reference, StringComparer.OrdinalIgnoreCase))
                .Where(reference => !reference.StartsWith(OwningModuleOf(self), StringComparison.OrdinalIgnoreCase))
                .ToList();

            Assert.True(
                otherModules.Count == 0,
                $"{module.Name} reaches into another module directly: {string.Join(", ", otherModules)}");
        }
    }

    /// <summary>
    /// The dependency between a module and the screens that show it runs one
    /// way. A module that can see its own <c>.UI</c> project is a module whose
    /// rules can start depending on how they are displayed — and the rule above
    /// would not catch it, because a module's own UI belongs to the same module.
    /// </summary>
    [Fact]
    public void A_modules_domain_projects_never_reference_a_ui_project()
    {
        var presentation = Repository.ProjectsUnder("src", "Modules")
            .Where(Repository.IsUserInterface)
            .Select(project => Path.GetFileNameWithoutExtension(project.Name))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        Assert.NotEmpty(presentation);

        var offenders = DomainProjects()
            .SelectMany(module => Repository.ReferencedProjectNames(module)
                .Where(presentation.Contains)
                .Select(reference => $"{module.Name} -> {reference}"))
            .ToList();

        Assert.True(
            offenders.Count == 0,
            "The UI renders the module; the module knows nothing about being rendered: "
            + string.Join(", ", offenders));
    }

    /// <summary>
    /// <c>src/Core</c> holds both the shared kernel and the shared component
    /// library. Neither may reference anything else in the solution — that is
    /// exactly what lets the storybook render the library on its own and lets
    /// every module depend on the kernel without pulling anything else along.
    /// </summary>
    [Fact]
    public void Nothing_in_core_depends_on_the_solution()
    {
        foreach (var project in Repository.ProjectsUnder("src", "Core"))
        {
            Assert.True(
                !Repository.ReferencedProjectNames(project).Any(),
                $"{project.Name} must stay dependency-free — src/Core is the one thing everything else may reference.");
        }
    }

    /// <summary>
    /// The dependency between the two adapters runs one way:
    /// <c>Backlog.Infrastructure.FileSystem</c> knows about
    /// <c>Backlog.Infrastructure.GitHub</c>, and never the reverse.
    ///
    /// <para>Nothing asserted this until the repository registry moved into the
    /// workspace folder. The GitHub adapter now has to know where that folder is,
    /// and the obvious way to tell it — hand it the <c>WorkspaceSettingsStore</c>
    /// that owns the root — would close the loop and fail the build with a
    /// circular reference. It takes a <c>Func&lt;string&gt;</c> root provider
    /// instead, the same shape both hosts already use for the task database and
    /// the roadmap plan.</para>
    ///
    /// <para>The build would catch the cycle, so this is not what stops it. What
    /// it stops is the fix somebody would reach for next: moving
    /// <c>WorkspaceSettingsStore</c>, or splitting a shared project out from
    /// under both, to make the reference legal. This says the direction is the
    /// decision, not an accident of who was written first.</para>
    /// </summary>
    [Fact]
    public void Backlog_Infrastructure_GitHub_never_reaches_for_the_file_system_adapter()
    {
        const string github = "Backlog.Infrastructure.GitHub";
        const string fileSystem = "Backlog.Infrastructure.FileSystem";

        var projects = Repository.ProjectsUnder("src", "Infrastructure")
            .ToDictionary(project => Path.GetFileNameWithoutExtension(project.Name), StringComparer.OrdinalIgnoreCase);

        Assert.True(projects.ContainsKey(github), $"{github} is not under src/Infrastructure any more.");
        Assert.True(projects.ContainsKey(fileSystem), $"{fileSystem} is not under src/Infrastructure any more.");

        Assert.DoesNotContain(
            fileSystem,
            Repository.ReferencedProjectNames(projects[github]),
            StringComparer.OrdinalIgnoreCase);

        // And the edge this rule is about still runs the other way. Without this
        // the rule would keep passing after somebody inverted the dependency,
        // green for exactly the wrong reason.
        Assert.Contains(
            github,
            Repository.ReferencedProjectNames(projects[fileSystem]),
            StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>The projects that own the decision: the module library and its
    /// published contract. Everything the module rules were written for.</summary>
    private static IEnumerable<FileInfo> DomainProjects() =>
        Repository.ProjectsUnder("src", "Modules").Where(project => !Repository.IsUserInterface(project));

    /// <summary>The projects that render one context's screens.</summary>
    private static IEnumerable<FileInfo> PresentationProjects() =>
        Repository.ProjectsUnder("src", "Modules").Where(Repository.IsUserInterface);

    private static HashSet<string> InfrastructureNames() =>
        Repository.ProjectsUnder("src", "Infrastructure")
            .Select(p => Path.GetFileNameWithoutExtension(p.Name))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

    /// <summary>A module's implementation project: the one whose name is the
    /// module's name and nothing else. <c>.Abstractions</c> is the contract and
    /// <c>.UI</c> is a screen; only this one holds the aggregate and the
    /// handlers.</summary>
    private static HashSet<string> ImplementationNames() =>
        Repository.ProjectsUnder("src", "Modules")
            .Select(p => Path.GetFileNameWithoutExtension(p.Name))
            .Where(name => name.Split('.').Length == 3)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

    private static bool IsAllowedCrossContextEdge(string from, string to) =>
        AllowedCrossContextUi.Any(allowed =>
            allowed.From.Equals(from, StringComparison.OrdinalIgnoreCase)
            && allowed.To.Equals(to, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// <c>Backlog.Modules.Backlog.Abstractions</c> and friends belong to the same
    /// module as <c>Backlog.Modules.Backlog</c>; only the first three segments identify it.
    /// </summary>
    private static string OwningModuleOf(string projectName)
    {
        var parts = projectName.Split('.');
        return parts.Length >= 3 ? string.Join('.', parts[..3]) : projectName;
    }
}
