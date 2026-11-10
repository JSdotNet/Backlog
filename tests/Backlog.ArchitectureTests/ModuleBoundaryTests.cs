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
    /// <c>.devbook/domain/context-map.md</c> already carries as a relationship, each
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
        // One relationship, two references: the Brain's screens are the Brain's
        // feature on Devbook's pane, so Brain.UI conforms to the pane's mode
        // extension point and to the published language it is written in. The
        // edge runs one way; nothing in Devbook references the Brain. The earlier
        // entry — Tasks.UI conforming to the Inbox's published item contract —
        // went when the Inbox got a module of its own and its join moved into an
        // adapter under src/Infrastructure, which is where a cross-context edge
        // belongs unless a pane renders on another context's pane. The next entry
        // needs the context map's say-so in its Relationship, as these have.
        ("Backlog.Modules.Brain.UI", "Backlog.Modules.Devbook.UI",
            "Customer/Supplier: the Brain conforms to the Devbook pane's mode extension point (local ADR 0040 §2; .devbook/domain/context-map.md)"),
        ("Backlog.Modules.Brain.UI", "Backlog.Modules.Devbook.Abstractions",
            "Customer/Supplier: the Brain answers Devbook's personal-source port and opens repository chapters in its published language (local ADR 0040 §2, ADR 0027 §6; .devbook/domain/context-map.md)"),
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
    /// An adapter never takes a presentation project: not the shared component
    /// library and not any <c>.UI</c> project. Nothing asserted this while the MCP
    /// adapter referenced <c>Backlog.UI.Components</c> for the Markdown and metadata
    /// parsers, which pulled the whole Razor class library into the MCP server. The
    /// parsers live in <c>Backlog.SharedKernel</c> now, where an adapter may read them.
    /// </summary>
    [Fact]
    public void Infrastructure_never_references_a_ui_project()
    {
        var infrastructure = Repository.ProjectsUnder("src", "Infrastructure").ToList();

        Assert.NotEmpty(infrastructure);

        var offenders = infrastructure
            .SelectMany(project => Repository.ReferencedProjectNames(project)
                .Where(reference => reference.Equals("Backlog.UI.Components", StringComparison.OrdinalIgnoreCase)
                    || reference.EndsWith(".UI", StringComparison.OrdinalIgnoreCase))
                .Select(reference => $"{project.Name} -> {reference}"))
            .ToList();

        Assert.True(
            offenders.Count == 0,
            "An adapter implements a port; it never renders, so it never needs a presentation project: "
            + string.Join(", ", offenders));
    }

    /// <summary>
    /// The other half of <see cref="Infrastructure_never_references_a_ui_project"/>:
    /// the parsers the MCP adapter reads a chapter with are in the kernel, free of
    /// the component framework, and no adapter source still imports a UI namespace —
    /// which a transitive reference would otherwise let compile.
    /// </summary>
    [Fact]
    public void The_markdown_and_metadata_parsers_live_in_the_shared_kernel()
    {
        var kernel = Path.Combine(Repository.Root.FullName, "src", "Core", "Backlog.SharedKernel");

        string[] parsers =
        [
            Path.Combine("Markdown", "MarkdownPreview.cs"),
            Path.Combine("Markdown", "MarkdownFileMetadata.cs"),
            Path.Combine("Metadata", "MetadataReader.cs"),
            Path.Combine("Metadata", "MetadataRecord.cs"),
            Path.Combine("Devbook", "DevbookSchema.cs"),
            Path.Combine("Devbook", "DevbookReference.cs"),
            Path.Combine("Devbook", "DevbookSync.cs"),
            Path.Combine("Devbook", "DevbookFolder.cs"),
        ];

        var missing = parsers.Where(parser => !File.Exists(Path.Combine(kernel, parser))).ToList();

        Assert.True(missing.Count == 0, "Not in Backlog.SharedKernel: " + string.Join(", ", missing));

        var framework = Directory.EnumerateFiles(kernel, "*.cs", SearchOption.AllDirectories)
            .Where(file => File.ReadAllText(file).Contains("Microsoft.AspNetCore", StringComparison.Ordinal))
            .Select(file => Path.GetRelativePath(kernel, file))
            .ToList();

        Assert.True(
            framework.Count == 0,
            "The kernel is what everything may see, so it carries no component framework: "
            + string.Join(", ", framework));

        var infrastructure = Path.Combine(Repository.Root.FullName, "src", "Infrastructure");

        var importers = Directory.EnumerateFiles(infrastructure, "*.cs", SearchOption.AllDirectories)
            .Where(file => File.ReadLines(file).Any(line => line.StartsWith("using Backlog.UI.", StringComparison.Ordinal)))
            .Select(file => Path.GetRelativePath(infrastructure, file))
            .ToList();

        Assert.True(
            importers.Count == 0,
            "An adapter imports no UI namespace: " + string.Join(", ", importers));
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
            + ".devbook/domain/context-map.md to stand on: " + string.Join(", ", offenders));
    }

    /// <summary>
    /// A screen asks its own module and takes whatever adapters it needs; what it
    /// does not do is consume another module's published surface.
    ///
    /// <para>This is the rule the workspace refactor was for. There used to be a
    /// <c>Backlog.Desktop.Workspace</c> project underneath all three contexts,
    /// and because everyone could read it, everyone did: Devbook's panels
    /// took the backlog root store and Tasks' list took the
    /// devbook-folder resolver. Splitting those four types into ports made the
    /// coupling visible, and the obvious next move — port each type into the
    /// module that owns it — would only have converted a shared project into two
    /// crossing references, <c>Devbook.UI -&gt; Backlog.Abstractions</c> and
    /// <c>Backlog.UI -&gt; Devbook.Abstractions</c>. Both are exactly what
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
                .Where(reference => !IsAllowedCrossContextEdge(Path.GetFileNameWithoutExtension(project.Name), reference))
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
    /// library. Neither may reference anything outside <c>src/Core</c> — that is
    /// exactly what lets the storybook render the library on its own and lets
    /// every module depend on the kernel without pulling anything else along.
    /// Inside it the edge runs one way: the library renders the kernel's
    /// Markdown and metadata parsers, and the kernel references nothing at all.
    /// </summary>
    [Fact]
    public void Nothing_in_core_depends_on_the_solution()
    {
        var core = Repository.ProjectsUnder("src", "Core").ToList();

        Assert.NotEmpty(core);

        foreach (var project in core)
        {
            var allowed = project.Name.Equals("Backlog.UI.Components.csproj", StringComparison.OrdinalIgnoreCase)
                ? new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Backlog.SharedKernel" }
                : [];

            Assert.True(
                Repository.ReferencedProjectNames(project).All(allowed.Contains),
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

    /// <summary>
    /// The activity cache's two halves sit either side of the same line
    /// <see cref="Backlog_Infrastructure_GitHub_never_reaches_for_the_file_system_adapter"/>
    /// draws, and this says so for the same reason that one does.
    ///
    /// <para>The port — <c>IAgentActivityCache</c> — is declared in
    /// <c>Backlog.Modules.Sessions.Abstractions</c> because it is phrased in terms of
    /// <c>AgentActivityRun</c>, which lives there. The half that decides where bytes
    /// land on disk is in <c>Backlog.Infrastructure.FileSystem</c>, because that is
    /// where the workspace root is known. The build already catches the cycle, so
    /// what this stops is the fix somebody reaches for next: moving the port into
    /// infrastructure, or giving the Sessions module a reference to the file-system
    /// adapter, to make one call site legal.</para>
    /// </summary>
    [Fact]
    public void Backlog_Modules_Sessions_never_reaches_for_the_file_system_adapter()
    {
        const string abstractions = "Backlog.Modules.Sessions.Abstractions";

        var infrastructure = InfrastructureNames();

        Assert.NotEmpty(infrastructure);

        var sessions = Repository.ProjectsUnder("src", "Modules", "Sessions")
            .ToDictionary(project => Path.GetFileNameWithoutExtension(project.Name), StringComparer.OrdinalIgnoreCase);

        Assert.Contains("Backlog.Modules.Sessions.UI", sessions.Keys, StringComparer.OrdinalIgnoreCase);
        Assert.Contains(abstractions, sessions.Keys, StringComparer.OrdinalIgnoreCase);

        var offenders = sessions.Values
            .SelectMany(project => Repository.ReferencedProjectNames(project)
                .Where(infrastructure.Contains)
                .Select(reference => $"{project.Name} -> {reference}"))
            .ToList();

        Assert.True(
            offenders.Count == 0,
            "The Sessions module declares the cache port; the file-system adapter implements it, never the "
            + "other way round: " + string.Join(", ", offenders));

        // And the edge this rule is about still runs the other way. Without this the
        // rule would keep passing after somebody inverted the dependency and moved the
        // port, green for exactly the wrong reason.
        var fileSystem = Repository.ProjectsUnder("src", "Infrastructure")
            .Single(project => Path.GetFileNameWithoutExtension(project.Name)
                == "Backlog.Infrastructure.FileSystem");

        Assert.Contains(
            abstractions,
            Repository.ReferencedProjectNames(fileSystem),
            StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// The Sessions module's disk readers are an adapter under <c>src/Infrastructure</c>,
    /// and its <c>.UI</c> project is a screen and nothing else.
    ///
    /// <para>The rules above let a <c>.UI</c> project take an adapter; none of them asks
    /// whether it <em>is</em> one, and for a while this one was — the transcript
    /// readers, the delivery-run stores and the Archify runner sat in
    /// <c>Backlog.Modules.Sessions.UI/Adapters</c>. They are
    /// <c>Backlog.Infrastructure.Sessions</c> now, composed by the two hosts. This pins
    /// both halves: the folder is gone and no file in the screen project opens a file or
    /// starts a process, and the adapter exists, answers the module's published surface,
    /// is composed by both hosts and is in the solution and its filter.</para>
    /// </summary>
    [Fact]
    public void Backlog_Modules_Sessions_UI_is_a_screen_and_Backlog_Infrastructure_Sessions_is_its_adapter()
    {
        const string adapter = "Backlog.Infrastructure.Sessions";
        const string adapterTests = "Backlog.Infrastructure.Sessions.UnitTests";

        var screen = new DirectoryInfo(Path.Combine(
            Repository.Root.FullName, "src", "Modules", "Sessions", "Backlog.Modules.Sessions.UI"));

        Assert.True(screen.Exists, $"{screen.FullName} is not where the Sessions screens live any more.");
        Assert.False(
            Directory.Exists(Path.Combine(screen.FullName, "Adapters")),
            "Backlog.Modules.Sessions.UI/Adapters is back: the readers belong in " + adapter + ".");

        var diskOrProcess = new System.Text.RegularExpressions.Regex(
            @"\b(File|Directory|FileInfo|DirectoryInfo|FileStream|Process|ProcessStartInfo)\s*[.(]|using\s+System\.Diagnostics\s*;");

        var offenders = screen.EnumerateFiles("*.cs", SearchOption.AllDirectories)
            .Where(file => !file.FullName.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                        && !file.FullName.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"))
            .Where(file => diskOrProcess.IsMatch(File.ReadAllText(file.FullName)))
            .Select(file => file.Name)
            .ToList();

        Assert.True(
            offenders.Count == 0,
            "Backlog.Modules.Sessions.UI reads the disk or starts a process: " + string.Join(", ", offenders));

        var infrastructure = Repository.ProjectsUnder("src", "Infrastructure")
            .ToDictionary(project => Path.GetFileNameWithoutExtension(project.Name), StringComparer.OrdinalIgnoreCase);

        Assert.True(infrastructure.ContainsKey(adapter), $"{adapter} is not under src/Infrastructure.");

        var references = Repository.ReferencedProjectNames(infrastructure[adapter]).ToList();

        Assert.Contains("Backlog.Modules.Sessions.Abstractions", references, StringComparer.OrdinalIgnoreCase);
        Assert.DoesNotContain("Backlog.Modules.Sessions.UI", references, StringComparer.OrdinalIgnoreCase);

        foreach (var host in new[] { ("src", "App", "Backlog.Desktop"), ("src", "Harness", "Backlog.Desktop.WebHarness") })
        {
            var project = Repository.ProjectsUnder(host.Item1, host.Item2)
                .Single(candidate => Path.GetFileNameWithoutExtension(candidate.Name) == host.Item3);

            Assert.Contains(adapter, Repository.ReferencedProjectNames(project), StringComparer.OrdinalIgnoreCase);
        }

        var solution = File.ReadAllText(Path.Combine(Repository.Root.FullName, "Backlog.sln"));
        var filter = File.ReadAllText(Path.Combine(Repository.Root.FullName, "Backlog.WithoutAppHeads.slnf"));

        foreach (var name in new[] { adapter, adapterTests })
        {
            Assert.Contains($"\\{name}.csproj\"", solution, StringComparison.Ordinal);
            Assert.Contains($"\\\\{name}.csproj\"", filter, StringComparison.Ordinal);
        }
    }

    /// <summary>
    /// Events is one module trio (local ADR 0050 §7): <c>Backlog.Modules.Events</c>, its
    /// <c>.Abstractions</c> and its <c>.UI</c>. The implementation references nothing under
    /// <c>src/Modules</c> but its own Abstractions — another context's <c>.Abstractions</c> is the
    /// most ADR 0050 Verification 6 would allow it, and it needs none yet — and no other project
    /// under <c>src/Modules</c> references the implementation. The generic rules above hold the
    /// rest; this pins the trio, and its place in the solution and the filter CI builds.
    /// </summary>
    [Fact]
    public void Backlog_Modules_Events_is_one_trio_that_no_other_module_references()
    {
        const string module = "Backlog.Modules.Events";
        const string abstractions = "Backlog.Modules.Events.Abstractions";
        const string ui = "Backlog.Modules.Events.UI";
        const string tests = "Backlog.Modules.Events.UnitTests";

        var modules = Repository.ProjectsUnder("src", "Modules")
            .ToDictionary(project => Path.GetFileNameWithoutExtension(project.Name), StringComparer.OrdinalIgnoreCase);

        foreach (var name in new[] { module, abstractions, ui })
        {
            Assert.True(modules.ContainsKey(name), $"{name} is not under src/Modules/Events.");
            Assert.Equal("Events", modules[name].Directory!.Parent!.Name);
        }

        var reachedFromTheModule = Repository.ReferencedProjectNames(modules[module])
            .Where(modules.ContainsKey)
            .ToList();

        Assert.Equal([abstractions], reachedFromTheModule, StringComparer.OrdinalIgnoreCase);

        var referencingTheModule = modules.Values
            .Where(project => Repository.ReferencedProjectNames(project).Contains(module, StringComparer.OrdinalIgnoreCase))
            .Select(project => project.Name)
            .ToList();

        Assert.True(
            referencingTheModule.Count == 0,
            $"{module} is referenced from inside src/Modules; another context reads Events through its .Abstractions: "
            + string.Join(", ", referencingTheModule));

        var solution = File.ReadAllText(Path.Combine(Repository.Root.FullName, "Backlog.sln"));
        var filter = File.ReadAllText(Path.Combine(Repository.Root.FullName, "Backlog.WithoutAppHeads.slnf"));

        foreach (var name in new[] { module, abstractions, ui, tests })
        {
            Assert.Contains($"\\{name}.csproj\"", solution, StringComparison.Ordinal);
            Assert.Contains($"\\\\{name}.csproj\"", filter, StringComparison.Ordinal);
        }
    }

    /// <summary>
    /// The join between Capture and the Inbox runs through
    /// <c>Backlog.Infrastructure.Capture</c> and nowhere else: it answers
    /// Capture's ports — declared in the module implementation, next to the
    /// handler that owns the rules — and hands what they produce to the Inbox
    /// through the Inbox's <em>published</em> intake, never its implementation.
    ///
    /// <para>The generic rules above already forbid either module seeing the
    /// adapter or each other. What they cannot say is which surface of the Inbox
    /// the adapter is allowed to see, and that is the line this draws: the
    /// delivery is a translation between two contexts, and a translation that
    /// reached into the Inbox's aggregate would be a second place the Inbox's
    /// rules could be bypassed. The build would not catch that reference; this
    /// does.</para>
    /// </summary>
    [Fact]
    public void Backlog_Infrastructure_Capture_answers_captures_ports_through_the_inboxes_published_surface()
    {
        const string adapter = "Backlog.Infrastructure.Capture";
        const string captureModule = "Backlog.Modules.Capture";
        const string inboxAbstractions = "Backlog.Modules.Inbox.Abstractions";
        const string inboxModule = "Backlog.Modules.Inbox";

        var projects = Repository.ProjectsUnder("src", "Infrastructure")
            .ToDictionary(project => Path.GetFileNameWithoutExtension(project.Name), StringComparer.OrdinalIgnoreCase);

        Assert.True(projects.ContainsKey(adapter), $"{adapter} is not under src/Infrastructure any more.");

        var references = Repository.ReferencedProjectNames(projects[adapter]).ToList();

        Assert.Contains(captureModule, references, StringComparer.OrdinalIgnoreCase);
        Assert.Contains(inboxAbstractions, references, StringComparer.OrdinalIgnoreCase);
        Assert.DoesNotContain(inboxModule, references, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// The Calendar's events layer is answered in <c>Backlog.Infrastructure.FileSystem</c>,
    /// under <c>Events/</c>, through Events' <em>published</em> read contract and never its
    /// implementation (local ADR 0050 §7).
    ///
    /// <para>A project-reference rule cannot draw this line. FileSystem references
    /// <c>Backlog.Modules.Events.Abstractions</c> and nothing more of Events, but it also
    /// references <c>Backlog.Infrastructure.Sqlite</c>, which references the Events
    /// implementation, so <c>KnowledgeEvent</c> and <c>IKnowledgeEventRepository</c> compile
    /// inside <c>Events/</c>. So this reads the compiled FileSystem assembly: no type in
    /// that namespace names a type of the <c>Backlog.Modules.Events</c> assembly in its
    /// base type, interfaces, fields, properties, or method and constructor signatures.
    /// The assembly is read from where its own build put it, as
    /// <see cref="ModuleAssemblyReferenceTests"/> reads the modules; a missing one fails and
    /// says to build first.</para>
    /// </summary>
    [Fact]
    public void Backlog_Infrastructure_FileSystem_answers_the_calendars_events_through_the_events_published_surface()
    {
        var reached = TypesNamedFrom(
            "Backlog.Infrastructure.FileSystem",
            "Backlog.Infrastructure.FileSystem.Events",
            out var scanned);

        Assert.Contains("EventsCalendarEvents", scanned);
        Assert.Contains("EventsCrossContextAdapterRegistration", scanned);

        // The adapter does read Events: through the published reader.
        Assert.Contains(reached, entry =>
            entry.Assembly.Equals(EventsAbstractions, StringComparison.OrdinalIgnoreCase)
            && entry.Type.EndsWith(".IKnowledgeEventReader", StringComparison.Ordinal));

        var offenders = reached
            .Where(entry => entry.Assembly.Equals(EventsImplementation, StringComparison.OrdinalIgnoreCase))
            .Select(entry => $"{entry.Member} names {entry.Type}")
            .Distinct(StringComparer.Ordinal)
            .ToList();

        Assert.True(
            offenders.Count == 0,
            $"Backlog.Infrastructure.FileSystem.Events reads the Events implementation; it reads {EventsAbstractions} only:\n"
            + string.Join('\n', offenders));
    }

    /// <summary>
    /// The scan above is only as good as its reach: one that read no signature would pass on
    /// any adapter. The Sqlite repository behind Events is the one place that must name the
    /// Events implementation, so the same scan over it has to find it.
    /// </summary>
    [Fact]
    public void The_events_surface_scan_finds_the_implementation_where_it_is_named()
    {
        var reached = TypesNamedFrom(
            "Backlog.Infrastructure.Sqlite",
            "Backlog.Infrastructure.Sqlite.Events",
            out var scanned);

        Assert.Contains("SqliteKnowledgeEventRepository", scanned);
        Assert.Contains(reached, entry =>
            entry.Assembly.Equals(EventsImplementation, StringComparison.OrdinalIgnoreCase)
            && entry.Type.EndsWith(".IKnowledgeEventRepository", StringComparison.Ordinal));
    }

    private const string EventsImplementation = "Backlog.Modules.Events";

    private const string EventsAbstractions = "Backlog.Modules.Events.Abstractions";

    /// <summary>
    /// Every type the types of <paramref name="ns"/> (and the types nested in them) name in
    /// their base type, interfaces, fields, properties and method signatures, each with the
    /// assembly it comes from, read off the compiled <paramref name="assembly"/> without
    /// loading it. <paramref name="scanned"/> is the names of the top-level types read.
    /// </summary>
    private static List<(string Member, string Assembly, string Type)> TypesNamedFrom(
        string assembly,
        string ns,
        out List<string> scanned)
    {
        var project = Repository.ProjectsUnder("src", "Infrastructure")
            .Single(candidate => Path.GetFileNameWithoutExtension(candidate.Name).Equals(assembly, StringComparison.OrdinalIgnoreCase));
        var testOutput = new DirectoryInfo(AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        var path = Path.Combine(project.DirectoryName!, "bin", testOutput.Parent!.Name, testOutput.Name, assembly + ".dll");

        Assert.True(
            File.Exists(path),
            $"{assembly} is not built, so its signatures cannot be read. Build the solution "
            + $"({testOutput.Parent.Name}, {testOutput.Name}) before running this test: {path}");

        using var stream = File.OpenRead(path);
        using var image = new System.Reflection.PortableExecutable.PEReader(stream);
        var metadata = System.Reflection.Metadata.PEReaderExtensions.GetMetadataReader(image);
        var names = new SignatureNames(metadata);

        var reached = new List<(string Member, string Assembly, string Type)>();
        scanned = [];

        foreach (var handle in metadata.TypeDefinitions)
        {
            var type = metadata.GetTypeDefinition(handle);
            if (!string.Equals(OuterNamespace(metadata, type), ns, StringComparison.Ordinal)) continue;

            var typeName = metadata.GetString(type.Name);
            if (type.GetDeclaringType().IsNil) scanned.Add(typeName);

            void Add(string member, string signature)
            {
                foreach (var (from, name) in SignatureNames.Parse(signature)) reached.Add(($"{typeName}.{member}", from, name));
            }

            if (!type.BaseType.IsNil) Add("(base)", names.Of(type.BaseType));

            foreach (var implemented in type.GetInterfaceImplementations())
            {
                Add("(interface)", names.Of(metadata.GetInterfaceImplementation(implemented).Interface));
            }

            foreach (var fieldHandle in type.GetFields())
            {
                var field = metadata.GetFieldDefinition(fieldHandle);
                Add(metadata.GetString(field.Name), field.DecodeSignature(names, null));
            }

            foreach (var propertyHandle in type.GetProperties())
            {
                var property = metadata.GetPropertyDefinition(propertyHandle);
                var signature = property.DecodeSignature(names, null);
                Add(metadata.GetString(property.Name), string.Join(' ', [signature.ReturnType, .. signature.ParameterTypes]));
            }

            foreach (var methodHandle in type.GetMethods())
            {
                var method = metadata.GetMethodDefinition(methodHandle);
                var signature = method.DecodeSignature(names, null);
                Add(metadata.GetString(method.Name), string.Join(' ', [signature.ReturnType, .. signature.ParameterTypes]));
            }
        }

        return reached;
    }

    /// <summary>The namespace of <paramref name="type"/>, or of the type it is nested in —
    /// a nested type, such as a closure the compiler wrote, carries none of its own.</summary>
    private static string OuterNamespace(System.Reflection.Metadata.MetadataReader metadata, System.Reflection.Metadata.TypeDefinition type)
    {
        while (!type.GetDeclaringType().IsNil) type = metadata.GetTypeDefinition(type.GetDeclaringType());
        return metadata.GetString(type.Namespace);
    }

    /// <summary>
    /// Writes each type a signature names as <c>[assembly]Namespace.Name</c>, generic
    /// arguments and element types included, so <see cref="Parse"/> can list every type
    /// and the assembly it comes from. A type defined in the assembly being read is
    /// written with an empty assembly.
    /// </summary>
    private sealed class SignatureNames(System.Reflection.Metadata.MetadataReader metadata)
        : System.Reflection.Metadata.ISignatureTypeProvider<string, object?>
    {
        private static readonly System.Text.RegularExpressions.Regex Named =
            new(@"\[(?<assembly>[^\]]*)\](?<type>[A-Za-z0-9_.`+<>]+?)(?=[\[\]<>,&* ]|$)", System.Text.RegularExpressions.RegexOptions.CultureInvariant);

        public static IEnumerable<(string Assembly, string Type)> Parse(string signature) =>
            Named.Matches(signature).Select(match => (match.Groups["assembly"].Value, match.Groups["type"].Value));

        public string Of(System.Reflection.Metadata.EntityHandle handle) => handle.Kind switch
        {
            System.Reflection.Metadata.HandleKind.TypeDefinition => GetTypeFromDefinition(metadata, (System.Reflection.Metadata.TypeDefinitionHandle)handle, 0),
            System.Reflection.Metadata.HandleKind.TypeReference => GetTypeFromReference(metadata, (System.Reflection.Metadata.TypeReferenceHandle)handle, 0),
            System.Reflection.Metadata.HandleKind.TypeSpecification => GetTypeFromSpecification(metadata, null, (System.Reflection.Metadata.TypeSpecificationHandle)handle, 0),
            _ => string.Empty
        };

        public string GetTypeFromDefinition(System.Reflection.Metadata.MetadataReader reader, System.Reflection.Metadata.TypeDefinitionHandle handle, byte rawTypeKind)
        {
            var type = reader.GetTypeDefinition(handle);
            return $"[]{reader.GetString(type.Namespace)}.{reader.GetString(type.Name)}";
        }

        public string GetTypeFromReference(System.Reflection.Metadata.MetadataReader reader, System.Reflection.Metadata.TypeReferenceHandle handle, byte rawTypeKind)
        {
            var type = reader.GetTypeReference(handle);
            var scope = type.ResolutionScope;

            // A nested type is scoped by the type it is nested in: its assembly is that one's.
            var outer = type;
            while (scope.Kind == System.Reflection.Metadata.HandleKind.TypeReference)
            {
                outer = reader.GetTypeReference((System.Reflection.Metadata.TypeReferenceHandle)scope);
                scope = outer.ResolutionScope;
            }

            var assembly = scope.Kind == System.Reflection.Metadata.HandleKind.AssemblyReference
                ? reader.GetString(reader.GetAssemblyReference((System.Reflection.Metadata.AssemblyReferenceHandle)scope).Name)
                : string.Empty;

            return $"[{assembly}]{reader.GetString(outer.Namespace)}.{reader.GetString(type.Name)}";
        }

        public string GetTypeFromSpecification(System.Reflection.Metadata.MetadataReader reader, object? genericContext, System.Reflection.Metadata.TypeSpecificationHandle handle, byte rawTypeKind) =>
            reader.GetTypeSpecification(handle).DecodeSignature(this, genericContext);

        public string GetPrimitiveType(System.Reflection.Metadata.PrimitiveTypeCode typeCode) => typeCode.ToString();

        public string GetSZArrayType(string elementType) => elementType + "[]";

        public string GetArrayType(string elementType, System.Reflection.Metadata.ArrayShape shape) => elementType + "[,]";

        public string GetByReferenceType(string elementType) => elementType + "&";

        public string GetPointerType(string elementType) => elementType + "*";

        public string GetPinnedType(string elementType) => elementType;

        public string GetGenericInstantiation(string genericType, System.Collections.Immutable.ImmutableArray<string> typeArguments) =>
            $"{genericType}<{string.Join(',', typeArguments)}>";

        public string GetGenericMethodParameter(object? genericContext, int index) => "!!" + index;

        public string GetGenericTypeParameter(object? genericContext, int index) => "!" + index;

        public string GetFunctionPointerType(System.Reflection.Metadata.MethodSignature<string> signature) =>
            string.Join(' ', [signature.ReturnType, .. signature.ParameterTypes]);

        public string GetModifiedType(string modifier, string unmodifiedType, bool isRequired) => $"{unmodifiedType} {modifier}";
    }

    /// <summary>
    /// The sessions pane injects the cheap port and only the cheap port.
    ///
    /// <para><c>IAgentSessionSource</c> stats files; <c>IAgentActivitySource</c> reads
    /// their bodies — hundreds of megabytes for a week of transcripts on the machine
    /// this was measured on. The list of sessions must never pay that to draw a row,
    /// and the obvious tidy-up is to fold activity into the catalog so there is one
    /// read. This is what says no: two ports means the pane cannot accidentally ask,
    /// and a pane that grew a second injection would be asking.</para>
    ///
    /// <para>Both halves are asserted. Only checking that the expensive port is absent
    /// would pass on a pane that had stopped injecting anything at all, and only
    /// checking the cheap one is there would pass on a pane that injected both.</para>
    /// </summary>
    [Fact]
    public void The_sessions_pane_injects_only_the_port_that_costs_a_file_stat()
    {
        var pane = new FileInfo(Path.Combine(
            Repository.Root.FullName,
            "src", "Modules", "Sessions", "Backlog.Modules.Sessions.UI", "SessionsPane.razor"));

        Assert.True(pane.Exists, $"{pane.FullName} is not where the sessions pane lives any more.");

        var markup = File.ReadAllText(pane.FullName);

        Assert.Contains("@inject IAgentSessionSource", markup, StringComparison.Ordinal);

        Assert.DoesNotContain("IAgentActivitySource", markup, StringComparison.Ordinal);
    }

    /// <summary>
    /// The projects that own the decision: the module library and its published
    /// contract. Everything the module rules were written for.
    ///
    /// <para>A module folder holds a third kind of project as well as those two
    /// and the <c>.UI</c> one. <c>Backlog.Modules.Sync.Api</c> is a host — it
    /// owns a token signing key, a middleware pipeline, and the mapping from a
    /// Result to a status code, and it composes the module rather than being
    /// part of it. Picking an adapter for a port is exactly what a host does, so
    /// it is excluded here for the same reason a <c>.UI</c> project is: the rule
    /// below is about a module reaching for an implementation, and a composition
    /// root reaching for one is the rule working rather than being broken. It
    /// sits under <c>src/Modules</c> because that is the context it serves, not
    /// because it is a module.</para>
    /// </summary>
    private static IEnumerable<FileInfo> DomainProjects() =>
        Repository.ProjectsUnder("src", "Modules")
            .Where(project => !Repository.IsUserInterface(project) && !IsHost(project));

    /// <summary>A composition root: it hosts one context's surface rather than
    /// owning a decision.</summary>
    private static bool IsHost(FileInfo project) =>
        project.Name.EndsWith(".Api.csproj", StringComparison.OrdinalIgnoreCase);

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
    /// <c>Backlog.Modules.Tasks.Abstractions</c> and friends belong to the same
    /// module as <c>Backlog.Modules.Tasks</c>; only the first three segments identify it.
    /// </summary>
    private static string OwningModuleOf(string projectName)
    {
        var parts = projectName.Split('.');
        return parts.Length >= 3 ? string.Join('.', parts[..3]) : projectName;
    }
}
