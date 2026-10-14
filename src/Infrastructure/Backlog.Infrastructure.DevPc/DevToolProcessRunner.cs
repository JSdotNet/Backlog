using System.ComponentModel;
using System.Diagnostics;
using System.Text;

namespace Backlog.Infrastructure.DevPc;

/// <summary>One command <see cref="DevToolService"/> starts, as the runner is
/// handed it.</summary>
/// <param name="FileName">What to start.</param>
/// <param name="Arguments">Its argument vector, never a joined string.</param>
/// <param name="Encoding">How to read both redirected streams.</param>
/// <param name="WorkingDirectory">Where to start it, or nothing to inherit this
/// process's own.</param>
internal sealed record DevToolProcessLaunch(
    string FileName,
    IReadOnlyList<string> Arguments,
    Encoding Encoding,
    string? WorkingDirectory);

/// <summary>What one command printed and how it exited.</summary>
internal sealed record DevToolProcessResult(int ExitCode, string Output, string Error);

/// <summary>
/// The one place <see cref="DevToolService"/> starts a process.
///
/// <para>A seam so that a listing can be driven without a machine full of CLIs:
/// what the listing does with an answer — the order it is logged in, the timeout
/// it is held to, how many launches are in flight at once — stays in the service
/// and is what a test observes; only the launch itself is replaced.</para>
///
/// <para>Two ways out besides a result, and the service reads both. A command
/// that cannot be started at all throws <see cref="Win32Exception"/>, which the
/// service turns into "not found" rather than a failure. A cancelled token
/// throws <see cref="OperationCanceledException"/> once whatever was started has
/// been stopped; whether that was the caller or the deadline is the service's
/// question, because the service owns the deadline.</para>
/// </summary>
internal interface IDevToolProcessRunner
{
    Task<DevToolProcessResult> RunAsync(DevToolProcessLaunch launch, CancellationToken ct);
}

/// <summary>The runner every host composes: a real child process with both
/// streams redirected and no console of its own.</summary>
internal sealed class DevToolProcessRunner : IDevToolProcessRunner
{
    public static DevToolProcessRunner Instance { get; } = new();

    private DevToolProcessRunner()
    {
    }

    public async Task<DevToolProcessResult> RunAsync(DevToolProcessLaunch launch, CancellationToken ct)
    {
        using var process = new Process();
        process.StartInfo = new ProcessStartInfo(launch.FileName)
        {
            // Empty means "inherit", which is what ProcessStartInfo already does
            // with it, so the ordinary launch is unchanged.
            WorkingDirectory = launch.WorkingDirectory ?? string.Empty,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            // Redirecting the streams is not what keeps the console away — only
            // this flag is. The desktop head is a GUI-subsystem process with no
            // console of its own, so Windows hands every child it starts a brand
            // new one, and opening the tools tab meant a dozen of them blinking
            // across the screen while the pane quietly read their output.
            CreateNoWindow = true,
            StandardOutputEncoding = launch.Encoding,
            StandardErrorEncoding = launch.Encoding
        };

        foreach (var argument in launch.Arguments)
        {
            process.StartInfo.ArgumentList.Add(argument);
        }

        // A CLI that is not on this machine throws Win32Exception here, and it is
        // left to escape: see the interface for who reads it.
        process.Start();

        try
        {
            var outputTask = process.StandardOutput.ReadToEndAsync(ct);
            var errorTask = process.StandardError.ReadToEndAsync(ct);
            await process.WaitForExitAsync(ct).ConfigureAwait(false);

            return new DevToolProcessResult(
                process.ExitCode,
                await outputTask.ConfigureAwait(false),
                await errorTask.ConfigureAwait(false));
        }
        catch (OperationCanceledException)
        {
            // The whole tree, not the process: winget's own installer runs as a
            // child of it, and killing only the parent leaves that installer
            // holding the package lock with nothing left to report on it.
            //
            // Killed whichever token fired. A caller that cancelled must not leave
            // a winget running against a pane that has gone, and a deadline that
            // fired is a hung CLI nobody is waiting for any more.
            KillProcessTree(process);
            throw;
        }
    }

    /// <summary>Stops a command that ran out of time, and says nothing when it
    /// cannot. Between the timeout firing and this line the process may already
    /// have exited on its own, which is a race rather than a problem — and a
    /// failure to kill is not something the row can act on either way.</summary>
    private static void KillProcessTree(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch (Exception exception) when (exception is InvalidOperationException or Win32Exception or NotSupportedException or AggregateException)
        {
        }
    }
}
