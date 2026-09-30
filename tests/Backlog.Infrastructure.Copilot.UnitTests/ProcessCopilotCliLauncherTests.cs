using System.ComponentModel;
using System.Diagnostics;

namespace Backlog.Infrastructure.Copilot.UnitTests;

/// <summary>
/// What the launcher starts and what it does with the Process it gets back. Every
/// launch goes through the internal start seam, so nothing here spawns the CLI.
/// </summary>
public sealed class ProcessCopilotCliLauncherTests : IDisposable
{
    private readonly List<string> _tempDirs = [];

    // The regression this class exists for: the launcher checked the returned
    // Process for null and dropped it, so every launch held a process handle until
    // the finalizer ran. It never waits for the CLI, so disposing right away is safe.
    [Fact]
    public async Task Disposes_the_started_process()
    {
        using var process = new DisposeTrackingProcess();
        var launcher = new ProcessCopilotCliLauncher("copilot", _ => process);

        var launch = launcher.LaunchAsync(new CopilotCliRequest("Explain this", null), TestContext.Current.CancellationToken);

        Assert.True(launch.IsCompletedSuccessfully);
        await launch;
        Assert.True(process.WasDisposed, "The Process returned by the start call must be disposed.");
    }

    [Fact]
    public async Task Starts_the_cli_in_its_own_console_with_the_prompt_as_the_only_argument()
    {
        var folder = CreateTempDir();
        ProcessStartInfo? started = null;
        var launcher = new ProcessCopilotCliLauncher(null, startInfo =>
        {
            started = startInfo;
            return new Process();
        });

        await launcher.LaunchAsync(new CopilotCliRequest("Explain \"this\" file", folder), TestContext.Current.CancellationToken);

        Assert.NotNull(started);
        Assert.Equal("copilot", started.FileName);
        Assert.False(started.UseShellExecute);
        Assert.False(started.CreateNoWindow);
        Assert.Equal(["Explain \"this\" file"], started.ArgumentList);
        Assert.Equal(folder, started.WorkingDirectory);
    }

    [Fact]
    public async Task Starts_the_configured_executable_and_leaves_the_working_directory_unset_when_none_is_given()
    {
        ProcessStartInfo? started = null;
        var launcher = new ProcessCopilotCliLauncher(@"  C:\tools\copilot.exe  ", startInfo =>
        {
            started = startInfo;
            return new Process();
        });

        await launcher.LaunchAsync(new CopilotCliRequest("Explain this", null), TestContext.Current.CancellationToken);

        Assert.NotNull(started);
        Assert.Equal(@"C:\tools\copilot.exe", started.FileName);
        Assert.Equal(string.Empty, started.WorkingDirectory);
    }

    [Fact]
    public async Task Reports_a_cli_that_did_not_start()
    {
        var launcher = new ProcessCopilotCliLauncher("copilot", _ => null);

        var ex = await Assert.ThrowsAsync<CopilotCliException>(
            () => launcher.LaunchAsync(new CopilotCliRequest("Explain this", null), TestContext.Current.CancellationToken));

        Assert.Equal("GitHub Copilot CLI did not start.", ex.Message);
    }

    [Fact]
    public async Task Maps_a_missing_executable_to_install_guidance()
    {
        var notFound = new Win32Exception(2);
        var launcher = new ProcessCopilotCliLauncher("copilot", _ => throw notFound);

        var ex = await Assert.ThrowsAsync<CopilotCliException>(
            () => launcher.LaunchAsync(new CopilotCliRequest("Explain this", null), TestContext.Current.CancellationToken));

        Assert.Equal(
            "Couldn't start GitHub Copilot CLI. Install 'copilot' or set BACKLOG_COPILOT_CLI to the executable path.",
            ex.Message);
        Assert.Same(notFound, ex.InnerException);
    }

    [Fact]
    public async Task Refuses_an_empty_prompt_without_starting_anything()
    {
        var starts = 0;
        var launcher = new ProcessCopilotCliLauncher("copilot", _ =>
        {
            starts++;
            return new Process();
        });

        await Assert.ThrowsAsync<CopilotCliException>(
            () => launcher.LaunchAsync(new CopilotCliRequest("   ", null), TestContext.Current.CancellationToken));

        Assert.Equal(0, starts);
    }

    [Fact]
    public async Task Refuses_a_missing_working_folder_without_starting_anything()
    {
        var starts = 0;
        var missing = Path.Combine(Path.GetTempPath(), "backlog-copilot-" + Guid.NewGuid().ToString("N"));
        var launcher = new ProcessCopilotCliLauncher("copilot", _ =>
        {
            starts++;
            return new Process();
        });

        var ex = await Assert.ThrowsAsync<CopilotCliException>(
            () => launcher.LaunchAsync(new CopilotCliRequest("Explain this", missing), TestContext.Current.CancellationToken));

        Assert.Contains(missing, ex.Message, StringComparison.Ordinal);
        Assert.Equal(0, starts);
    }

    public void Dispose()
    {
        foreach (var dir in _tempDirs)
        {
            try { Directory.Delete(dir, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
    }

    private string CreateTempDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), "backlog-copilot-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        _tempDirs.Add(dir);
        return dir;
    }

    /// <summary>Process.Dispose never raises Component.Disposed, so the only way to
    /// see the launcher release the handle is to override Dispose itself.</summary>
    private sealed class DisposeTrackingProcess : Process
    {
        public bool WasDisposed { get; private set; }

        protected override void Dispose(bool disposing)
        {
            WasDisposed = true;
            base.Dispose(disposing);
        }
    }
}
