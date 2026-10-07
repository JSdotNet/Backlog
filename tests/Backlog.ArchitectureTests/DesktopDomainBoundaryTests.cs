namespace Backlog.ArchitectureTests;

/// <summary>
/// Rules for the desktop app's own bounded contexts. Inbox, Tasks
/// and Devbook (the Devbook folder) each have their own UI project now —
/// <c>src/Modules/&lt;Context&gt;/Backlog.Modules.&lt;Context&gt;.UI</c> — under a Shell
/// that composes them.
/// <para>
/// They used to be three folders in one project, and that is why this class was
/// written: a folder stops nothing, so nothing kept a Devbook panel from
/// injecting the backlog list except somebody noticing. The split made those
/// boundaries project references, and a project reference the compiler will not
/// let you forge. <see cref="ModuleBoundaryTests"/> and
/// <see cref="ModuleSurfaceTests"/> read those references and are now the first
/// line; what is left here is the two things a reference cannot say.
/// </para>
/// <para>
/// First, that the split is still the shape of the repository — a project per
/// context and a Shell that is not one, and nothing quietly moved back inside
/// another. Second, the finer grain: a reference is coarse, and the
/// moment <c>Backlog.Modules.Tasks.UI</c> takes its one allowed reference on
/// the Inbox the compiler will accept any Backlog-Management file naming any
/// Inbox type. These rules read the source text instead, so a <c>using</c> is
/// caught in the file that wrote it rather than at the reference it would
/// eventually need. They read namespaces, which for the first three contexts
/// still say <c>Backlog.Desktop.UI.*</c> — see the <c>RootNamespace</c> comments
/// in those csproj files for why renaming them does not compile — and for the
/// later ones say <c>Backlog.Modules.*.UI</c>, so each rule asks the project.
/// </para>
/// <para>
/// The contexts are read from <c>src/Modules</c> rather than listed here. The
/// list used to name Inbox, Tasks and Devbook, and Capture, Dashboard, DevPc,
/// Roadmap and Sessions each arrived with a UI project of their own that these
/// rules never read (issue #741). A new module's UI project is covered the day
/// it lands.
/// </para>
/// </summary>
public class DesktopDomainBoundaryTests
{
    /// <summary>The Shell's namespace. It is a folder of the desktop app rather
    /// than a project, so there is no csproj to ask.</summary>
    private const string ShellNamespace = "Backlog.Desktop.UI.Shell";

    /// <summary>The desktop's contexts, keyed by their module folder under
    /// <c>src/Modules</c> and valued by the <c>.UI</c> project that holds their
    /// screens. A module with no screens is not one.</summary>
    private static readonly Dictionary<string, string> ContextProjects = ModuleUiProjects()
        .ToDictionary(
            project => project.Directory!.Parent!.Name,
            project => Repository.RelativePath(project.Directory!),
            StringComparer.Ordinal);

    /// <summary>The namespace each context's types are declared in: its UI
    /// project's own <c>RootNamespace</c>.</summary>
    private static readonly Dictionary<string, string> ContextNamespaces = ModuleUiProjects()
        .ToDictionary(
            project => project.Directory!.Parent!.Name,
            Repository.RootNamespaceOf,
            StringComparer.Ordinal);

    /// <summary>The one area that is not a context: the Shell that composes the
    /// rest. It is a folder rather than a project because it <em>is</em> the
    /// desktop app — what is left of <c>Backlog.Desktop.UI</c> once the contexts
    /// moved out.
    /// <para>
    /// There used to be a second: a <c>Backlog.Desktop.Workspace</c> project
    /// holding where the backlog lived, which repositories were configured and
    /// which features were on, which every context was allowed to read. It is
    /// gone, and its absence is the point rather than an omission. A layer every
    /// context may read is a layer every context shares, and sharing it is how
    /// Tasks came to consume a devbook-folder resolver and Devbook
    /// came to consume the backlog root — two contexts the map calls a
    /// Partnership, coupled through a project neither owned. What lived there is
    /// now two module ports with adapters behind them in <c>src/Infrastructure</c>:
    /// each context asks its own module, and the adapter holds the join. The rules
    /// that named the Workspace went with it; what replaced them is
    /// <see cref="ModuleBoundaryTests.A_module_ui_asks_only_its_own_modules_published_surface"/>.
    /// </para></summary>
    private const string ShellProject = "src/App/Backlog.Desktop.UI";
    private const string ShellFolder = "src/App/Backlog.Desktop.UI/Shell";
    private const string RemovedWorkspaceProject = "src/App/Backlog.Desktop.Workspace";

    /// <summary>Where each namespace segment's source lives, the contexts and the
    /// Shell alike, so a rule can name an area and get a folder.</summary>
    private static readonly Dictionary<string, string> Areas =
        new(ContextProjects.Append(new("Shell", ShellFolder)), StringComparer.Ordinal);

    /// <summary>The namespace each area's types carry, the same keys as
    /// <see cref="Areas"/>.</summary>
    private static readonly Dictionary<string, string> AreaNamespaces =
        new(ContextNamespaces.Append(new("Shell", ShellNamespace)), StringComparer.Ordinal);

    /// <summary>Every ordered pair of two different contexts.</summary>
    public static TheoryData<string, string> ContextPairs()
    {
        var pairs = new TheoryData<string, string>();

        foreach (var context in ContextProjects.Keys.Order(StringComparer.Ordinal))
        {
            foreach (var other in ContextProjects.Keys.Order(StringComparer.Ordinal).Where(other => other != context))
            {
                pairs.Add(context, other);
            }
        }

        return pairs;
    }

    /// <summary>Every context.</summary>
    public static TheoryData<string> Contexts()
    {
        var contexts = new TheoryData<string>();

        foreach (var context in ContextProjects.Keys.Order(StringComparer.Ordinal))
        {
            contexts.Add(context);
        }

        return contexts;
    }

    /// <summary>
    /// The contexts are read from the module folders, and a reading that broke
    /// would leave every rule below iterating nothing — green for the wrong
    /// reason. These anchors are the contexts there are today: the three the
    /// list used to name and the five it missed.
    /// </summary>
    [Fact]
    public void The_contexts_are_read_from_the_module_folders()
    {
        foreach (var context in new[] { "Inbox", "Tasks", "Devbook", "Capture", "Dashboard", "DevPc", "Roadmap", "Sessions" })
        {
            Assert.True(
                ContextProjects.ContainsKey(context),
                $"{context} has a UI project under src/Modules but is not one of the contexts these rules read.");
        }

        Assert.Equal("Backlog.Desktop.UI.Tasks", ContextNamespaces["Tasks"]);
        Assert.Equal("Backlog.Modules.Sessions.UI", ContextNamespaces["Sessions"]);
    }

    /// <summary>
    /// Each context is its own compilation unit. That is what turns "a knowledge
    /// panel should not inject the backlog list" from a review comment into a
    /// missing reference, and it is the whole point of the split — a context
    /// filed back into a folder of somebody else's project would take the
    /// enforcement with it.
    /// </summary>
    [Fact]
    public void Each_context_has_its_own_project()
    {
        foreach (var (context, path) in ContextProjects)
        {
            var folder = Folder(path);

            Assert.True(
                Directory.Exists(folder),
                $"{context} lives in {path}; splitting the contexts is the whole point.");

            Assert.True(
                Directory.EnumerateFiles(folder, "*.csproj").Any(),
                $"{path} holds no .csproj, so {context} is a folder again and the compiler has stopped "
                + "enforcing its boundary.");
        }
    }

    /// <summary>
    /// The Shell composes the contexts into a screen. It is not a context, and it
    /// has to stay somewhere a context is not: holding one would put it in the
    /// same compilation unit as the panes it wires together, which is the
    /// arrangement the split undid.
    /// <para>
    /// The same rule used to say this about the Workspace too, and asserted that
    /// the Workspace project existed. It says the opposite now, for the reason
    /// given above <see cref="RemovedWorkspaceProject"/>: a project every context
    /// is allowed to read is somewhere any two of them can quietly meet, and the
    /// meeting is what this suite exists to prevent. Recreating it would give a
    /// shared type a home again without either context having to publish it.
    /// </para>
    /// </summary>
    [Fact]
    public void The_shell_is_separate_from_every_context()
    {
        Assert.True(
            Directory.Exists(Folder(ShellFolder)),
            $"The Shell composes the contexts and lives in {ShellFolder}.");

        Assert.False(
            Directory.Exists(Folder(RemovedWorkspaceProject)),
            $"{RemovedWorkspaceProject} is back. A layer every context may read is a layer every context "
            + "shares; what lived there is now a module port each context asks on its own, with an "
            + "adapter behind it holding the join.");

        foreach (var context in ContextProjects.Keys)
        {
            Assert.False(
                Directory.Exists(Path.Combine(Folder(ShellProject), context)),
                $"{context} is back inside the shell project. The Shell may compose a context; holding "
                + "one puts it in the same compilation unit as the panes it wires together.");
        }

        foreach (var (context, path) in ContextProjects)
        {
            Assert.False(
                Directory.Exists(Path.Combine(Folder(path), "Shell")),
                $"{context} has grown its own Shell. There is one, above every context; a second "
                + "copy is a context deciding for everybody.");
        }
    }

    /// <summary>
    /// Inbox is upstream of both Tasks and Devbook, and the
    /// two downstream contexts are a Partnership that coordinates by id rather
    /// than by reaching into each other. No pair is exempt: the edge Tasks once
    /// had on the Inbox — a conversion into the Inbox's published item
    /// contract — went when the Inbox got a module of its own, and what the
    /// Inbox routes to Tasks now travels through a port an adapter under
    /// src/Infrastructure answers, where neither context's UI sees the other.
    /// <para>
    /// The project references say most of this already. This says it a file at a
    /// time, so a <c>using</c> that reaches sideways is caught where it is
    /// written rather than once a reference has been added to carry it.
    /// </para>
    /// </summary>
    [Theory]
    [MemberData(nameof(ContextPairs))]
    public void A_context_never_names_another_context(string context, string forbidden)
    {
        var offenders = FilesNaming(context, AreaNamespaces[forbidden]);

        Assert.True(
            offenders.Count == 0,
            $"{context} must not depend on {forbidden} — see .devbook/domain/context-map.md for the relationships that do exist. "
            + string.Join(", ", offenders));
    }

    /// <summary>The shell composes the contexts, so it may see all of them. The
    /// reverse would make the panes unable to exist without the page they happen
    /// to sit on today.</summary>
    [Theory]
    [MemberData(nameof(Contexts))]
    public void Nothing_below_the_shell_depends_on_the_shell(string area)
    {
        var offenders = FilesNaming(area, ShellNamespace);

        Assert.True(
            offenders.Count == 0,
            $"{area} must not reach up into the Shell; the Shell wires it in. " + string.Join(", ", offenders));
    }

    /// <summary>
    /// The shell project's <c>_Imports.razor</c> is what decides which namespaces
    /// its components see without asking. Listing a context there would hand the
    /// app's chrome every context's types, and the project split would be filing
    /// rather than a boundary. The Shell folder has an <c>_Imports.razor</c> of its
    /// own that does name all three, which is correct and is the point: composing
    /// them is what that folder is for, and it is the only folder that says so.
    /// </summary>
    [Fact]
    public void The_shared_razor_imports_stay_free_of_the_contexts()
    {
        var imports = Path.Combine(Folder(ShellProject), "_Imports.razor");
        Assert.True(File.Exists(imports), $"{ShellProject} should keep a project-wide _Imports.razor.");

        var text = File.ReadAllText(imports);
        foreach (var area in Areas.Keys)
        {
            Assert.DoesNotContain(AreaNamespaces[area], text, StringComparison.Ordinal);
        }
    }

    /// <summary>
    /// Each context project now has an <c>_Imports.razor</c> of its own, and it
    /// decides the same thing for the same reason: what its components see
    /// without asking. A sibling context named there would be handed to every
    /// component in the project at once.
    /// <para>
    /// The Inbox's own imports name its module's published surface and the
    /// shared library, and nothing of Tasks or Devbook — the same line the
    /// other two hold, now that no context conforms to another's contract in
    /// code.
    /// </para>
    /// </summary>
    [Theory]
    [MemberData(nameof(Contexts))]
    public void A_contexts_own_razor_imports_name_no_other_context(string context)
    {
        var imports = Path.Combine(Folder(ContextProjects[context]), "_Imports.razor");
        Assert.True(File.Exists(imports), $"{ContextProjects[context]} should keep its own _Imports.razor.");

        var text = File.ReadAllText(imports);

        foreach (var other in ContextProjects.Keys.Where(name => name != context).Append("Shell"))
        {
            Assert.DoesNotContain(AreaNamespaces[other], text, StringComparison.Ordinal);
        }
    }

    private static IEnumerable<FileInfo> ModuleUiProjects() =>
        Repository.ProjectsUnder("src", "Modules").Where(Repository.IsUserInterface);

    private static string Folder(string relativePath) =>
        Path.Combine([Repository.Root.FullName, .. relativePath.Split('/')]);

    private static List<string> FilesNaming(string area, string namespaceName)
    {
        var directory = new DirectoryInfo(Folder(Areas[area]));
        if (!directory.Exists) return [];

        return [.. directory
            .EnumerateFiles("*", SearchOption.AllDirectories)
            .Where(file => file.Extension is ".cs" or ".razor")
            // Each area is a project folder now rather than a folder inside one,
            // so it carries build output — and the generated Razor sources under
            // obj/ name every namespace their component imports. Reading those
            // back would make every rule here report the file it just approved.
            .Where(file => !file.FullName.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                        && !file.FullName.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"))
            .Where(file => File.ReadAllText(file.FullName).Contains(namespaceName, StringComparison.Ordinal))
            .Select(file => file.Name)];
    }
}
