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
