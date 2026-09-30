using System.Text.RegularExpressions;

namespace Backlog.ArchitectureTests;

/// <summary>
/// A Debug build of the desktop head keeps its per-user state under
/// <c>%LOCALAPPDATA%\Backlog.Debug</c> and the installed app under
/// <c>%LOCALAPPDATA%\Backlog</c>, through
/// <c>WorkspaceSettingsStore.DefaultAppDataFolderName</c>. A sync file composed
/// with a literal <c>"Backlog"</c> segment instead lands in the installed app's
/// folder even from a Debug head, which then reads the installed app's
/// credential, sync URL and flags and replicates its own database to the cloud
/// as that device.
///
/// <para>
/// A text scan, because a MAUI head cannot be composed in a test process. It is
/// deliberately narrow: it looks for <c>LocalApplicationData</c> followed by a
/// literal <c>"Backlog"</c> segment in the desktop composition root and in the
/// sync client, which has no reference to the workspace store and so receives
/// its paths from the host.
/// </para>
/// </summary>
public sealed partial class DesktopSyncAppDataIsolationTests
{
    [Fact]
    public void No_sync_path_is_composed_under_a_literal_Backlog_folder()
    {
        var files = new[] { Path.Combine(Repository.Root.FullName, "src", "App", "Backlog.Desktop", "MauiProgram.cs") }
            .Concat(SourceUnder("src", "Infrastructure", "Backlog.Infrastructure.Sync"))
            // The shared composition both desktop heads call, which is where the
            // sync stores are registered from the paths the head hands it.
            .Concat(SourceUnder("src", "App", "Backlog.Desktop.Composition"))
            .ToList();

        Assert.True(File.Exists(files[0]));
        Assert.Contains(files, file => file.Contains("Backlog.Desktop.Composition", StringComparison.Ordinal));

        var offenders = files
            .Where(file => LiteralBacklogFolder().IsMatch(File.ReadAllText(file)))
            .Select(file => Path.GetRelativePath(Repository.Root.FullName, file))
            .ToList();

        Assert.True(
            offenders.Count == 0,
            "These files compose a LocalApplicationData path under a literal \"Backlog\" folder, which a Debug "
            + "head shares with the installed app. Compose it under WorkspaceSettingsStore.DefaultAppDataDirectory, "
            + "or take the path from the host:\n" + string.Join('\n', offenders));
    }

    private static IEnumerable<string> SourceUnder(params string[] segments)
    {
        var folder = Path.Combine([Repository.Root.FullName, .. segments]);

        return Directory.Exists(folder)
            ? Directory.EnumerateFiles(folder, "*.cs", SearchOption.AllDirectories)
                .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                         && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"))
            : [];
    }

    [GeneratedRegex(@"SpecialFolder\.LocalApplicationData\)\s*,\s*""Backlog""")]
    private static partial Regex LiteralBacklogFolder();
}
