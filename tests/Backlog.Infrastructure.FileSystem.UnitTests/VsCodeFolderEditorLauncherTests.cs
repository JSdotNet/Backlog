using System.Diagnostics;

using Backlog.SharedKernel;

namespace Backlog.Infrastructure.FileSystem.UnitTests;

/// <summary>
/// How the launcher decides *which* VS Code to start, and how it notices that the
/// one it started never opened anything.
///
/// The resolution half runs against a fake file-exists probe and a fake PATH, so
/// it asserts the order rather than whatever happens to be installed on the
/// machine running the suite. The launch half goes through the internal process
/// seam, so nothing here actually spawns VS Code.
/// </summary>
public sealed class VsCodeFolderEditorLauncherTests : IDisposable
{
    private const string PerUserPrograms = @"C:\fake\Users\dev\AppData\Local\Programs";
    private const string MachinePrograms = @"C:\fake\Program Files";
    private const string MachineProgramsX86 = @"C:\fake\Program Files (x86)";

    private static readonly string[] InstallBases = [PerUserPrograms, MachinePrograms, MachineProgramsX86];

    private static readonly string PerUserStable = Path.Combine(PerUserPrograms, "Microsoft VS Code", "Code.exe");
    private static readonly string MachineStable = Path.Combine(MachinePrograms, "Microsoft VS Code", "Code.exe");
    private static readonly string MachineStableX86 = Path.Combine(MachineProgramsX86, "Microsoft VS Code", "Code.exe");
    private static readonly string PerUserInsiders = Path.Combine(PerUserPrograms, "Microsoft VS Code Insiders", "Code - Insiders.exe");
    private static readonly string MachineInsiders = Path.Combine(MachinePrograms, "Microsoft VS Code Insiders", "Code - Insiders.exe");

    private const string ToolsInstall = @"C:\fake\tools\VSCode";
    private static readonly string ToolsShimDirectory = Path.Combine(ToolsInstall, "bin");
    private static readonly string ToolsShim = Path.Combine(ToolsShimDirectory, "code.cmd");
    private static readonly string ToolsExecutable = Path.Combine(ToolsInstall, "Code.exe");

    private readonly List<string> _tempDirs = [];

    // The regression this class exists for: a bare "code.cmd" starts cmd.exe with
    // %0 = "code.cmd" and no directory, so the shim's own "%~dp0..\Code.exe"
    // misses, the process exits 9009 and nothing opens. Only an absolute target
    // is safe, and beside a bin\code.cmd shim the real executable is better still.
    [Fact]
    public void Resolves_the_absolute_executable_beside_the_code_shim_found_on_path()
    {
        var resolved = VsCodeFolderEditorLauncher.ResolveExecutablePath(
            configuredOverride: null,
            pathVariable: PathVariable(@"C:\fake\Windows\System32", ToolsShimDirectory),
            installBaseDirectories: InstallBases,
            fileExists: Existing(ToolsShim, ToolsExecutable));

        Assert.Equal(ToolsExecutable, resolved);
        Assert.True(Path.IsPathFullyQualified(resolved!), $"'{resolved}' must be an absolute path.");
    }

    [Fact]
    public void Resolves_the_shim_itself_when_no_executable_sits_beside_it()
    {
        var resolved = VsCodeFolderEditorLauncher.ResolveExecutablePath(
            configuredOverride: null,
            pathVariable: PathVariable(ToolsShimDirectory),
            installBaseDirectories: InstallBases,
            fileExists: Existing(ToolsShim));

        Assert.Equal(ToolsShim, resolved);
    }

    [Fact]
    public void Skips_path_entries_that_are_not_absolute()
    {
        var resolved = VsCodeFolderEditorLauncher.ResolveExecutablePath(
            configuredOverride: null,
            pathVariable: PathVariable(".", "bin", ToolsShimDirectory),
            installBaseDirectories: InstallBases,
            fileExists: path => Path.GetFileName(path).Equals("code.cmd", StringComparison.OrdinalIgnoreCase));

        Assert.Equal(ToolsShim, resolved);
    }

    [Fact]
    public void Prefers_the_per_user_install_when_no_code_command_is_on_path()
    {
        var resolved = VsCodeFolderEditorLauncher.ResolveExecutablePath(
            configuredOverride: null,
            pathVariable: PathVariable(@"C:\fake\Windows\System32"),
            installBaseDirectories: InstallBases,
            fileExists: Existing(PerUserStable, MachineStable, MachineStableX86));

        Assert.Equal(PerUserStable, resolved);
    }

    [Fact]
    public void Falls_back_to_the_machine_wide_install_when_the_per_user_one_is_missing()
    {
        var resolved = VsCodeFolderEditorLauncher.ResolveExecutablePath(
            configuredOverride: null,
            pathVariable: null,
            installBaseDirectories: InstallBases,
            fileExists: Existing(MachineStable, MachineStableX86));

        Assert.Equal(MachineStable, resolved);
    }

    [Fact]
    public void Falls_back_to_the_x86_install_when_no_other_stable_install_exists()
    {
        var resolved = VsCodeFolderEditorLauncher.ResolveExecutablePath(
            configuredOverride: null,
            pathVariable: null,
            installBaseDirectories: InstallBases,
            fileExists: Existing(MachineStableX86));

        Assert.Equal(MachineStableX86, resolved);
    }

    [Fact]
    public void Falls_back_to_insiders_only_when_no_stable_install_exists()
    {
        var resolved = VsCodeFolderEditorLauncher.ResolveExecutablePath(
            configuredOverride: null,
            pathVariable: null,
            installBaseDirectories: InstallBases,
            fileExists: Existing(PerUserInsiders, MachineInsiders));

        Assert.Equal(PerUserInsiders, resolved);
    }

    [Fact]
    public void Prefers_a_stable_install_over_insiders_even_when_insiders_is_the_per_user_one()
    {
        var resolved = VsCodeFolderEditorLauncher.ResolveExecutablePath(
            configuredOverride: null,
            pathVariable: null,
            installBaseDirectories: InstallBases,
            fileExists: Existing(PerUserInsiders, MachineStable));

        Assert.Equal(MachineStable, resolved);
    }

    [Fact]
    public void Configured_override_pointing_at_a_file_wins_over_path_and_known_installs()
    {
        const string configured = @"C:\fake\portable\VSCode\Code.exe";

        var resolved = VsCodeFolderEditorLauncher.ResolveExecutablePath(
            configuredOverride: $"  {configured}  ",
            pathVariable: PathVariable(ToolsShimDirectory),
            installBaseDirectories: InstallBases,
            fileExists: Existing(configured, ToolsShim, ToolsExecutable, PerUserStable));

        Assert.Equal(configured, resolved);
    }

    [Fact]
    public void Configured_override_naming_a_bare_command_resolves_through_path()
    {
        var insidersShimDirectory = Path.Combine(ToolsInstall, "insiders", "bin");
        var insidersShim = Path.Combine(insidersShimDirectory, "code-insiders.cmd");

        var resolved = VsCodeFolderEditorLauncher.ResolveExecutablePath(
            configuredOverride: "code-insiders",
            pathVariable: PathVariable(ToolsShimDirectory, insidersShimDirectory),
            installBaseDirectories: InstallBases,
            fileExists: Existing(insidersShim, ToolsShim, ToolsExecutable));

        Assert.Equal(insidersShim, resolved);
    }

    [Fact]
    public void Returns_nothing_when_no_code_installation_can_be_found()
    {
        var resolved = VsCodeFolderEditorLauncher.ResolveExecutablePath(
            configuredOverride: null,
            pathVariable: PathVariable(@"C:\fake\Windows\System32"),
            installBaseDirectories: InstallBases,
            fileExists: _ => false);

        Assert.Null(resolved);
    }

    [Fact]
    public async Task Throws_naming_the_override_variable_when_vs_code_cannot_be_found()
    {
        var launcher = new VsCodeFolderEditorLauncher(() => null, NeverStarted);

        var error = await Assert.ThrowsAsync<FolderEditorLaunchException>(
            () => launcher.OpenFolderAsync(TempDir()));

        Assert.Contains("BACKLOG_VSCODE_CLI", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Throws_naming_the_exit_code_when_the_process_exits_non_zero_right_away()
    {
        var launcher = new VsCodeFolderEditorLauncher(
            () => ToolsExecutable,
            (_, _) => Task.FromResult(new VsCodeLaunchOutcome(Exited: true, ExitCode: 9009)));

        var error = await Assert.ThrowsAsync<FolderEditorLaunchException>(
            () => launcher.OpenFolderAsync(TempDir()));

        Assert.Contains("9009", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Accepts_an_immediate_zero_exit_because_vs_code_forwards_to_a_running_instance()
    {
        var launcher = new VsCodeFolderEditorLauncher(
            () => ToolsExecutable,
            (_, _) => Task.FromResult(new VsCodeLaunchOutcome(Exited: true, ExitCode: 0)));

        await launcher.OpenFolderAsync(TempDir());
    }

    [Fact]
    public async Task Accepts_a_process_that_is_still_running_when_the_grace_window_closes()
    {
        var launcher = new VsCodeFolderEditorLauncher(
            () => ToolsExecutable,
            (_, _) => Task.FromResult(new VsCodeLaunchOutcome(Exited: false, ExitCode: 0)));

        await launcher.OpenFolderAsync(TempDir());
    }

    [Fact]
    public async Task Starts_the_resolved_executable_with_the_folder_as_its_only_argument()
    {
        var folder = TempDir();
        ProcessStartInfo? started = null;
        var launcher = new VsCodeFolderEditorLauncher(
            () => ToolsExecutable,
            (startInfo, _) =>
            {
                started = startInfo;
                return Task.FromResult(new VsCodeLaunchOutcome(Exited: false, ExitCode: 0));
            });

        await launcher.OpenFolderAsync(folder);

        Assert.NotNull(started);
        Assert.Equal(ToolsExecutable, started!.FileName);
        Assert.Equal([folder], started.ArgumentList);
    }

    [Fact]
    public async Task Throws_when_the_folder_does_not_exist()
    {
        var missing = Path.Combine(TempDir(), "not-there");
        var launcher = new VsCodeFolderEditorLauncher(() => ToolsExecutable, NeverStarted);

        await Assert.ThrowsAsync<FolderEditorLaunchException>(() => launcher.OpenFolderAsync(missing));
    }

    [Fact]
    public async Task Honours_a_cancelled_token_before_starting_anything()
    {
        var launcher = new VsCodeFolderEditorLauncher(() => ToolsExecutable, NeverStarted);
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => launcher.OpenFolderAsync(TempDir(), cancelled.Token));
    }

    public void Dispose()
    {
        foreach (var dir in _tempDirs.Where(Directory.Exists))
        {
            try { Directory.Delete(dir, recursive: true); } catch (IOException) { }
        }
    }

    private static Task<VsCodeLaunchOutcome> NeverStarted(ProcessStartInfo startInfo, CancellationToken cancellationToken)
    {
        Assert.Fail("The launcher started a process when it should not have.");
        return Task.FromResult(default(VsCodeLaunchOutcome));
    }

    private static string PathVariable(params string[] directories) =>
        string.Join(Path.PathSeparator, directories);

    /// <summary>A file-exists probe that answers for exactly these paths, so the
    /// resolution order is asserted against a fixed machine rather than the
    /// build agent's own VS Code installation.</summary>
    private static Func<string, bool> Existing(params string[] files)
    {
        var present = new HashSet<string>(files, StringComparer.OrdinalIgnoreCase);
        return path => present.Contains(path);
    }

    private string TempDir()
    {
        var path = Path.Combine(Path.GetTempPath(), "vscode-launcher-tests", Guid.NewGuid().ToString("n"));
        Directory.CreateDirectory(path);
        _tempDirs.Add(path);
        return path;
    }
}
