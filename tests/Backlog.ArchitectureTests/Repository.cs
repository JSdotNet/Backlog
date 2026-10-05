using System.Xml.Linq;

namespace Backlog.ArchitectureTests;

/// <summary>
/// The repository layout these tests read, rooted at the folder that holds
/// <c>src</c>, <c>tests</c>, and <c>Backlog.sln</c>, so they keep working no
/// matter where the build output lands.
/// </summary>
internal static class Repository
{
    /// <inheritdoc cref="RepositoryRoot.Root" />
    public static DirectoryInfo Root { get; } = RepositoryRoot.Root;

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

    /// <summary>
    /// Every source file of <c>Backlog.Desktop.Composition</c>, the one copy of
    /// what the two desktop heads compose, read as one text; <see langword="null"/>
    /// when the project is not there.
    /// </summary>
    public static string? DesktopCompositionSource()
    {
        var folder = new DirectoryInfo(Path.Combine(Root.FullName, "src", "App", "Backlog.Desktop.Composition"));

        if (!folder.Exists) return null;

        return string.Join(
            '\n',
            folder.EnumerateFiles("*.cs", SearchOption.AllDirectories)
                .Where(f => !f.FullName.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}")
                         && !f.FullName.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}"))
                .OrderBy(f => f.FullName, StringComparer.Ordinal)
                .Select(f => File.ReadAllText(f.FullName)));
    }

    /// <summary>
    /// A composition root's text with the shared registrations it calls read in
    /// after it: the desktop composition when the root calls
    /// <c>AddDesktopComposition(</c>, and the Sqlite registration when what has
    /// been read so far calls <c>AddSqlite(</c>.
    ///
    /// <para>The text-scanning rules ask whether a head that opts into something
    /// also registers what it needs. Once the head hands that registration to a
    /// shared extension the answer lives in the extension's file, so a rule that
    /// read the head alone would fail on a head that is correct — and one that
    /// stopped asking would pass on a shared composition that dropped the line.
    /// Following the call keeps the question and moves where it looks. Only calls
    /// the text actually makes are followed, so a head that composes by hand is
    /// still read by itself.</para>
    /// </summary>
    public static string ComposedText(string rootText)
    {
        var text = rootText;

        if (text.Contains("AddDesktopComposition(", StringComparison.Ordinal))
        {
            text += "\n" + DesktopCompositionSource();
        }

        var sqlite = Path.Combine(Root.FullName, "src", "Infrastructure", "Backlog.Infrastructure.Sqlite", "SqliteRegistration.cs");

        if (text.Contains(".AddSqlite(", StringComparison.Ordinal) && File.Exists(sqlite))
        {
            text += "\n" + File.ReadAllText(sqlite);
        }

        return text;
    }

    /// <summary>
    /// The namespace a project's types are declared in: its <c>RootNamespace</c>
    /// when it sets one, else its own name, which is what MSBuild falls back to.
    /// The desktop's first three contexts kept <c>Backlog.Desktop.UI.*</c> when
    /// they moved into module projects and the later ones did not, so a rule
    /// about a project's namespace asks the project rather than assuming.
    /// </summary>
    public static string RootNamespaceOf(FileInfo project)
    {
        var declared = XDocument.Load(project.FullName)
            .Descendants("RootNamespace")
            .Select(element => element.Value.Trim())
            .FirstOrDefault(value => value.Length > 0);

        return declared ?? Path.GetFileNameWithoutExtension(project.Name);
    }

    /// <summary>
    /// Every file under <paramref name="folder"/> with one of
    /// <paramref name="extensions"/>, leaving out build output: the generated
    /// Razor sources under <c>obj</c> repeat every namespace and call their
    /// component makes, so reading them back would report each file twice.
    /// </summary>
    public static IEnumerable<FileInfo> SourceFilesUnder(DirectoryInfo folder, params string[] extensions) =>
        folder.Exists
            ? folder.EnumerateFiles("*", SearchOption.AllDirectories)
                .Where(f => extensions.Contains(f.Extension, StringComparer.OrdinalIgnoreCase))
                .Where(f => !f.FullName.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}")
                         && !f.FullName.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}"))
            : [];

    /// <summary>A path relative to <see cref="Root"/>, with forward slashes, the
    /// way the exception lists in these tests spell one.</summary>
    public static string RelativePath(FileSystemInfo item) =>
        Path.GetRelativePath(Root.FullName, item.FullName).Replace('\\', '/');

    public static IEnumerable<string> ReferencedProjectNames(FileInfo project)
    {
        return XDocument.Load(project.FullName)
            .Descendants("ProjectReference")
            .Select(r => (string?)r.Attribute("Include"))
            .Where(include => !string.IsNullOrWhiteSpace(include))
            .Select(include => Path.GetFileNameWithoutExtension(include!.Replace('\\', Path.DirectorySeparatorChar)));
    }
}
