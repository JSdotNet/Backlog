using System.ComponentModel;
using System.Diagnostics;
using Backlog.Modules.Repositories.Abstractions.Services;

namespace Backlog.Infrastructure.FileSystem;

/// <summary>
/// Runs an environment's command (local ADR 0073 §3) in a console window of its own, so
/// the person sees what it prints and closes it themselves. Here beside the editor
/// launcher for the reason that one is: starting a process on this machine is
/// local-machine work, and no context owns it.
/// <para>
/// <c>cmd.exe /k</c> takes the rest of the line verbatim, so the command runs exactly as
/// it was typed, in the repository's clone directory when it has one on this device.
/// </para>
/// </summary>
public sealed class ConsoleEnvironmentCommandRunner : IEnvironmentCommandRunner
{
    public const string WindowsOnly = "Running a command is only available on Windows.";

    private readonly Func<ProcessStartInfo, Process?> _start;

    public ConsoleEnvironmentCommandRunner()
        : this(Process.Start)
    {
    }

    internal ConsoleEnvironmentCommandRunner(Func<ProcessStartInfo, Process?> start) => _start = start;

    public string? Run(string commandLine, string? workingDirectory)
    {
        if (string.IsNullOrWhiteSpace(commandLine)) return "There is no command to run.";
        if (!OperatingSystem.IsWindows()) return WindowsOnly;

        // A clone directory named but missing would run the command wherever the app
        // happens to be, with every relative path in it wrong: refused and said.
        if (!string.IsNullOrWhiteSpace(workingDirectory) && !Directory.Exists(workingDirectory))
        {
            return $"The clone directory {workingDirectory} isn't on this device, so the command wasn't run.";
        }

        var startInfo = StartInfo(commandLine, workingDirectory);

        try
        {
            using var process = _start(startInfo);
            return process is null ? "The command couldn't be started." : null;
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException)
        {
            return $"The command couldn't be started: {ex.Message}";
        }
    }

    /// <summary>What is started: a new console window running the command, in the
    /// working directory when one is given and exists.</summary>
    internal static ProcessStartInfo StartInfo(string commandLine, string? workingDirectory)
    {
        var startInfo = new ProcessStartInfo("cmd.exe", "/k " + commandLine.Trim())
        {
            // A window of its own rather than one borrowed from the app.
            UseShellExecute = true
        };

        if (!string.IsNullOrWhiteSpace(workingDirectory) && Directory.Exists(workingDirectory))
        {
            startInfo.WorkingDirectory = workingDirectory;
        }

        return startInfo;
    }
}
