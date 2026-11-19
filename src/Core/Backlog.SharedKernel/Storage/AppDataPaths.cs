namespace Backlog.SharedKernel.Storage;

/// <summary>
/// Where a per-user file lives under <see cref="Environment.SpecialFolder.LocalApplicationData"/>.
/// Two folders, because the stores already use two and every file has to stay where it is:
/// <see cref="Shared"/> is <c>Backlog</c> in every build, so a Debug head and the installed
/// app read the same settings file; <see cref="Head"/> is <c>Backlog.Debug</c> for a Debug
/// build and <c>Backlog</c> for a Release one, so a Debug head never reads the installed
/// app's workspace, device identity or logs.
/// </summary>
/// <remarks>
/// The folder names are persisted locations: renaming either one strands every settings
/// file written under it.
/// </remarks>
public static class AppDataPaths
{
    /// <summary>The folder every build shares.</summary>
    public const string SharedFolderName = "Backlog";

    /// <summary>The folder of this build's own head: <c>Backlog.Debug</c> for a Debug
    /// build, <c>Backlog</c> for a Release one.</summary>
#if DEBUG
    public const string HeadFolderName = "Backlog.Debug";
#else
    public const string HeadFolderName = "Backlog";
#endif

    /// <summary>The per-user local application data folder itself.</summary>
    public static string LocalApplicationData =>
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

    /// <summary>A path under the folder every build shares —
    /// <c>%LOCALAPPDATA%\Backlog\…</c>. With no parts, the folder itself.</summary>
    public static string Shared(params string[] parts) =>
        Path.Combine([LocalApplicationData, SharedFolderName, .. parts]);

    /// <summary>A path under this build's own folder —
    /// <c>%LOCALAPPDATA%\Backlog.Debug\…</c> for a Debug build. With no parts, the
    /// folder itself.</summary>
    public static string Head(params string[] parts) =>
        Path.Combine([LocalApplicationData, HeadFolderName, .. parts]);
}
