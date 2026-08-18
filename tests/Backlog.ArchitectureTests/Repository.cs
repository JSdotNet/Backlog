using System.Xml.Linq;

namespace Backlog.ArchitectureTests;

/// <summary>
/// Walks up from the test binary to the repository root — the folder that holds
/// <c>src</c>, <c>tests</c>, and <c>Backlog.sln</c> — so these tests keep working no
/// matter where the build output lands.
/// </summary>
internal static class Repository
{
    public static DirectoryInfo Root { get; } = Locate();

    public static IEnumerable<FileInfo> ProjectsUnder(params string[] segments)
    {
        var folder = new DirectoryInfo(Path.Combine([Root.FullName, .. segments]));
        return folder.Exists
            ? folder.EnumerateFiles("*.csproj", SearchOption.AllDirectories)
                .Where(f => !f.FullName.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}")
                         && !f.FullName.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}"))
            : [];
    }

    /// <summary>
    /// Every project that renders screens: the <c>.UI</c> projects under
    /// <c>src/App</c> and the per-context <c>.UI</c> projects under
    /// <c>src/Modules</c>.
    ///
    /// <para>The desktop's three contexts used to be folders inside
    /// <c>Backlog.Desktop.UI</c>, so a rule scoped to <c>src/App</c> covered
    /// them by accident. They are their own projects under <c>src/Modules</c>
    /// now, and a rule that still reads only <c>src/App</c> keeps passing while
    /// seeing nothing but the shell's chrome and the mobile app — green for the
    /// wrong reason, which is worse than red. Anything that is about screens
    /// asks for this instead of naming a folder.</para>
    /// </summary>
    public static IEnumerable<FileInfo> UserInterfaceProjects() =>
        ProjectsUnder("src", "App")
            .Concat(ProjectsUnder("src", "Modules"))
            .Where(IsUserInterface);

    /// <summary>
    /// The folders the application's own UI lives in: all of <c>src/App</c> —
    /// which holds the executable heads' <c>wwwroot</c> as well as the UI
    /// projects — plus each module's <c>.UI</c> project folder. The counterpart
    /// to <see cref="UserInterfaceProjects"/> for rules that read files rather
    /// than project references.
    /// </summary>
    public static IEnumerable<DirectoryInfo> UserInterfaceFolders()
    {
        var app = new DirectoryInfo(Path.Combine(Root.FullName, "src", "App"));
        if (app.Exists) yield return app;

        foreach (var project in ProjectsUnder("src", "Modules").Where(IsUserInterface))
        {
            yield return project.Directory!;
        }
    }

    /// <summary>A presentation assembly: it renders one area's screens rather
    /// than owning a decision.</summary>
    public static bool IsUserInterface(FileInfo project) =>
        project.Name.EndsWith(".UI.csproj", StringComparison.OrdinalIgnoreCase);

    public static IEnumerable<string> ReferencedProjectNames(FileInfo project)
    {
        return XDocument.Load(project.FullName)
            .Descendants("ProjectReference")
            .Select(r => (string?)r.Attribute("Include"))
            .Where(include => !string.IsNullOrWhiteSpace(include))
            .Select(include => Path.GetFileNameWithoutExtension(include!.Replace('\\', Path.DirectorySeparatorChar)));
    }

    private static DirectoryInfo Locate()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (Directory.Exists(Path.Combine(dir.FullName, "src"))
                && Directory.Exists(Path.Combine(dir.FullName, "tests"))
                && File.Exists(Path.Combine(dir.FullName, "Backlog.sln")))
            {
                return dir;
            }

            dir = dir.Parent;
        }

        throw new DirectoryNotFoundException(
            "Could not locate the repository root above " + AppContext.BaseDirectory);
    }
}
