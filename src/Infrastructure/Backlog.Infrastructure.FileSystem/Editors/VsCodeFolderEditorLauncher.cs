using System.ComponentModel;
using System.Diagnostics;
using Backlog.SharedKernel;

namespace Backlog.Infrastructure.FileSystem;

/// <summary>What became of a started process inside the launcher's grace window:
/// whether it had already exited, and with which code if it had.</summary>
internal readonly record struct VsCodeLaunchOutcome(bool Exited, int ExitCode);

/// <summary>The process seam. Production starts a real process; the tests assert
/// what the launcher does with the outcome without spawning VS Code.</summary>
internal delegate Task<VsCodeLaunchOutcome> VsCodeProcessLauncher(ProcessStartInfo startInfo, CancellationToken cancellationToken);

/// <summary>
/// Opens a folder in the installed VS Code.
///
/// <para>The adapters under <c>Editors/</c> are here for the same reason the ones
/// under <c>Workspace/</c> are: finding the installed VS Code means reading
/// environment variables, walking PATH and probing for files on this machine's
/// disk, which is file-system work and nothing else. No context owns it — a
/// knowledge chapter, a repository clone and a session worktree are all just a
/// folder by the time this class sees one — so it answers
/// <see cref="IFolderEditorLauncher"/> from the shared kernel rather than living
/// inside whichever module happened to want it first.</para>
///
/// <para>The launch target is always an absolute path, and that is not a tidiness
/// preference — it is the whole fix for a bug that shipped. Starting the bare
/// name "code.cmd" with UseShellExecute=false does start something: Windows finds
/// the batch file on PATH and hands it to cmd.exe with %0 set to "code.cmd" and
/// no directory, so the shim's own "%~dp0..\Code.exe" resolves against the app's
/// working directory, misses, and the process exits 9009. Process.Start returned
/// non-null and threw nothing, so the button reported success while nothing
/// opened. Do not simplify this back to "code.cmd".</para>
/// </summary>
public sealed class VsCodeFolderEditorLauncher : IFolderEditorLauncher
{
    private const string EnvironmentVariable = "BACKLOG_VSCODE_CLI";
    private const string DefaultCommand = "code";

    /// <summary>How long a started process gets to prove it is still alive. Long
    /// enough to catch a CreateProcess-level failure (9009 and friends land in
    /// milliseconds), short enough that the button's spinner is not noticeably
    /// waiting on a window that is already opening.</summary>
    private static readonly TimeSpan EarlyExitGrace = TimeSpan.FromSeconds(1);

    /// <summary>Where VS Code puts itself, relative to one of the base directories
    /// below. Stable before Insiders: someone with both installed means the plain
    /// one, and Insiders is a last resort rather than a preference.</summary>
    private static readonly string[] KnownRelativeExecutables =
    [
        Path.Combine("Microsoft VS Code", "Code.exe"),
        Path.Combine("Microsoft VS Code Insiders", "Code - Insiders.exe")
    ];

    private readonly Func<string?> _resolveExecutable;
    private readonly VsCodeProcessLauncher _launch;

    public VsCodeFolderEditorLauncher()
        : this(
            () => ResolveExecutablePath(
                Environment.GetEnvironmentVariable(EnvironmentVariable),
                Environment.GetEnvironmentVariable("PATH"),
                KnownInstallDirectories(),
                File.Exists),
            StartAsync)
    {
    }

    internal VsCodeFolderEditorLauncher(Func<string?> resolveExecutable, VsCodeProcessLauncher launch)
    {
        _resolveExecutable = resolveExecutable;
        _launch = launch;
    }

    public async Task OpenFolderAsync(string folderPath, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!Directory.Exists(folderPath)) throw new FolderEditorLaunchException($"The folder does not exist: {folderPath}");

        var executable = _resolveExecutable() ?? throw new FolderEditorLaunchException(NotFoundMessage);

        var startInfo = new ProcessStartInfo(executable)
        {
            UseShellExecute = false,
            CreateNoWindow = true
        };
        startInfo.ArgumentList.Add(folderPath);

        VsCodeLaunchOutcome outcome;
        try
        {
            outcome = await _launch(startInfo, cancellationToken).ConfigureAwait(false);
        }
        catch (Win32Exception ex)
        {
            throw new FolderEditorLaunchException(NotFoundMessage, ex);
        }

        // A process still running is a window opening, and an immediate 0 is
        // Code.exe handing the folder to an instance that was already up. An
        // immediate non-zero is the failure this class used to report as success.
        if (outcome.Exited && outcome.ExitCode != 0)
        {
            throw new FolderEditorLaunchException(
                $"VS Code exited with code {outcome.ExitCode} without opening the folder. Set {EnvironmentVariable} to the VS Code executable to point Backlog at the right one.");
        }
    }

    private static string NotFoundMessage =>
        $"Couldn't open VS Code. Install the 'code' command or set {EnvironmentVariable} to the executable path.";

    /// <summary>
    /// Picks the VS Code to launch: the configured override, then whatever PATH
    /// points at, then the places the installers use. Returns an absolute path, or
    /// null when this machine has no VS Code the launcher can name.
    /// </summary>
    internal static string? ResolveExecutablePath(
        string? configuredOverride,
        string? pathVariable,
        IReadOnlyList<string> installBaseDirectories,
        Func<string, bool> fileExists)
    {
        var configured = configuredOverride?.Trim();
        if (!string.IsNullOrEmpty(configured))
        {
            if (fileExists(configured)) return Path.GetFullPath(configured);

            // The override predates this fix and was allowed to be a bare command
            // name, so it still is: it goes through the same PATH scan the default
            // name takes rather than being rejected for lacking a directory.
            var fromOverride = ResolveFromPath(configured, pathVariable, fileExists);
            if (fromOverride is not null) return fromOverride;
        }

        return ResolveFromPath(DefaultCommand, pathVariable, fileExists)
            ?? ResolveFromKnownInstalls(installBaseDirectories, fileExists);
    }

    /// <summary>The directories the VS Code installers write to, per-user first
    /// because that is what the default download does.</summary>
    internal static IReadOnlyList<string> KnownInstallDirectories()
    {
        if (!OperatingSystem.IsWindows()) return [];

        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return
        [
            string.IsNullOrEmpty(localAppData) ? string.Empty : Path.Combine(localAppData, "Programs"),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86)
        ];
    }

    private static string? ResolveFromPath(string command, string? pathVariable, Func<string, bool> fileExists)
    {
        if (string.IsNullOrWhiteSpace(pathVariable)) return null;

        var fileNames = CommandFileNames(command);
        foreach (var entry in pathVariable.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var directory = entry.Trim('"');
            foreach (var fileName in fileNames)
            {
                var candidate = Path.Combine(directory, fileName);
                // A relative PATH entry cannot produce the absolute target this
                // class exists to launch, so it is skipped rather than resolved
                // against a working directory that is not the user's.
                if (!Path.IsPathFullyQualified(candidate) || !fileExists(candidate)) continue;

                return PreferInstalledExecutable(candidate, fileExists) ?? candidate;
            }
        }

        return null;
    }

    /// <summary>On Windows the command on PATH is extensionless, so the candidates
    /// are the executable forms of it.</summary>
    private static string[] CommandFileNames(string command) =>
        OperatingSystem.IsWindows() && string.IsNullOrEmpty(Path.GetExtension(command))
            ? [command + ".cmd", command + ".exe"]
            : [command];

    /// <summary>The PATH hit is usually VS Code's <c>bin\code.cmd</c> shim, whose
    /// only job is to start the executable one directory up. Starting that
    /// executable directly skips the cmd.exe hop and the console window that
    /// flashes with it.</summary>
    private static string? PreferInstalledExecutable(string shimPath, Func<string, bool> fileExists)
    {
        if (!string.Equals(Path.GetExtension(shimPath), ".cmd", StringComparison.OrdinalIgnoreCase)) return null;

        var shimDirectory = Path.GetDirectoryName(shimPath);
        if (shimDirectory is null || !string.Equals(Path.GetFileName(shimDirectory), "bin", StringComparison.OrdinalIgnoreCase)) return null;

        var installRoot = Path.GetDirectoryName(shimDirectory);
        if (installRoot is null) return null;

        var executable = Path.Combine(installRoot, "Code.exe");
        return fileExists(executable) ? executable : null;
    }

    private static string? ResolveFromKnownInstalls(IReadOnlyList<string> baseDirectories, Func<string, bool> fileExists)
    {
        foreach (var relativeExecutable in KnownRelativeExecutables)
        {
            foreach (var baseDirectory in baseDirectories)
            {
                if (string.IsNullOrWhiteSpace(baseDirectory)) continue;

                var candidate = Path.Combine(baseDirectory, relativeExecutable);
                if (Path.IsPathFullyQualified(candidate) && fileExists(candidate)) return candidate;
            }
        }

        return null;
    }

    private static async Task<VsCodeLaunchOutcome> StartAsync(ProcessStartInfo startInfo, CancellationToken cancellationToken)
    {
        using var process = Process.Start(startInfo) ?? throw new FolderEditorLaunchException("VS Code did not start.");

        using var grace = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        grace.CancelAfter(EarlyExitGrace);
        try
        {
            await process.WaitForExitAsync(grace.Token).ConfigureAwait(false);
            return new VsCodeLaunchOutcome(Exited: true, process.ExitCode);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // Only the grace window closed. The process is alive, so VS Code is
            // starting; disposing the Process handle does not disturb it.
            return new VsCodeLaunchOutcome(Exited: false, ExitCode: 0);
        }
    }
}
