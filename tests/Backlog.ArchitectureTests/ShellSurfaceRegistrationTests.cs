using System.Text.RegularExpressions;

namespace Backlog.ArchitectureTests;

/// <summary>
/// A module's shell surface is reached only through its registration (local ADR 0040
/// §1). Each module's <c>.UI</c> project registers a <c>ShellSurface</c> whose
/// component is <c>typeof(...)</c> of one of its own components, and the shell renders
/// it through <c>DynamicComponent</c> from the catalog. If
/// <c>Backlog.Desktop.UI</c> named that component — rendered it as a tag, or as a
/// type — the module's screen would be a shell edit again, and a window or a page
/// that renders from the catalog would no longer see what the main window shows.
///
/// <para>
/// A text scan over the source, like the other composition rules here: it finds every
/// <c>new ShellSurface(...)</c> under <c>src/Modules</c>, takes the component its
/// <c>typeof</c> names, and asks that no <c>.razor</c> or <c>.cs</c> file of
/// <c>src/App/Backlog.Desktop.UI</c> mention that name as a word. Comments count —
/// a component the shell only talks about can be named in prose with a
/// <c>&lt;c&gt;</c> it does not mind losing.
/// </para>
/// </summary>
public partial class ShellSurfaceRegistrationTests
{
    [Fact]
    public void A_module_surface_component_is_not_named_by_the_desktop_shell()
    {
        var components = RegisteredSurfaceComponents().ToList();

        // The Devbook and the five views register one each; an empty list would pass
        // for the wrong reason.
        foreach (var expected in new[] { "DevbookSurface", "TasksSurface", "BoardSurface", "CalendarSurface", "RoadmapSurface", "GoalsSurface" })
        {
            Assert.Contains(components, component => component.Name == expected);
        }

        var shell = new DirectoryInfo(Path.Combine(Repository.Root.FullName, "src", "App", "Backlog.Desktop.UI"));
        var shellFiles = Repository.SourceFilesUnder(shell, ".razor", ".cs")
            .Select(file => (Path: Repository.RelativePath(file), Text: File.ReadAllText(file.FullName)))
            .ToList();
        Assert.NotEmpty(shellFiles);

        var offenders = (
            from component in components
            from file in shellFiles
            where Regex.IsMatch(file.Text, $@"\b{Regex.Escape(component.Name)}\b")
            select $"{file.Path} names {component.Name}, registered by {component.RegisteredIn}").ToList();

        Assert.True(
            offenders.Count == 0,
            "The desktop shell names a module's surface component. Reach it through the module's ShellSurface "
            + "registration and the surface catalog instead:\n" + string.Join('\n', offenders));
    }

    [Theory]
    [InlineData("AddDevbookSurface(")]
    [InlineData("AddTasksSurfaces(")]
    [InlineData("AddRoadmapSurface(")]
    [InlineData("AddGoalsSurface(")]
    public void The_desktop_composition_registers_each_module_surface(string registration) =>
        Assert.Contains(registration, Repository.DesktopCompositionSource(), StringComparison.Ordinal);

    /// <summary>The component of every <c>ShellSurface</c> a module constructs.</summary>
    private static IEnumerable<(string Name, string RegisteredIn)> RegisteredSurfaceComponents()
    {
        var modules = new DirectoryInfo(Path.Combine(Repository.Root.FullName, "src", "Modules"));

        foreach (var file in Repository.SourceFilesUnder(modules, ".cs"))
        {
            var text = File.ReadAllText(file.FullName);
            foreach (Match registration in SurfaceRegistration().Matches(text))
            {
                yield return (registration.Groups["component"].Value, Repository.RelativePath(file));
            }
        }
    }

    /// <summary><c>new ShellSurface(</c>, then the first <c>typeof(X)</c> before the
    /// call closes — the component argument.</summary>
    [GeneratedRegex(@"new\s+ShellSurface\s*\((?:[^;]*?)typeof\(\s*(?:[\w.]+\.)?(?<component>\w+)\s*\)", RegexOptions.Singleline)]
    private static partial Regex SurfaceRegistration();
}
