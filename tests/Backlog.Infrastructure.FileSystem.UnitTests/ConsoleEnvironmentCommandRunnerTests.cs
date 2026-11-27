using System.Diagnostics;

namespace Backlog.Infrastructure.FileSystem.UnitTests;

/// <summary>
/// An environment's command, run in a console window of its own in the repository's
/// clone directory — asserted on what would be started, without starting it.
/// </summary>
public sealed class ConsoleEnvironmentCommandRunnerTests
{
    [Fact]
    public void A_command_runs_verbatim_in_a_window_of_its_own_in_the_clone_directory()
    {
        var clone = Path.GetTempPath();

        var startInfo = ConsoleEnvironmentCommandRunner.StartInfo(" aspire start --isolated ", clone);

        Assert.Equal("cmd.exe", startInfo.FileName);
        Assert.Equal("/k aspire start --isolated", startInfo.Arguments);
        Assert.True(startInfo.UseShellExecute);
        Assert.Equal(clone, startInfo.WorkingDirectory);
    }

    [Fact]
    public void A_clone_directory_missing_on_this_device_is_not_used()
    {
        var startInfo = ConsoleEnvironmentCommandRunner.StartInfo("dir", Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N")));

        Assert.Equal(string.Empty, startInfo.WorkingDirectory);
    }

    [Fact]
    public void A_start_that_fails_says_so()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "The runner starts cmd.exe on Windows only.");
        ProcessStartInfo? started = null;
        var runner = new ConsoleEnvironmentCommandRunner(info =>
        {
            started = info;
            return null;
        });

        Assert.NotNull(runner.Run("dir", null));
        Assert.NotNull(started);
        Assert.NotNull(runner.Run("  ", null));
    }

    [Fact]
    public void A_clone_directory_missing_on_this_device_refuses_the_run()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "The runner starts cmd.exe on Windows only.");
        var started = false;
        var runner = new ConsoleEnvironmentCommandRunner(_ =>
        {
            started = true;
            return null;
        });

        Assert.NotNull(runner.Run("dir", Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"))));
        Assert.False(started);
    }
}
