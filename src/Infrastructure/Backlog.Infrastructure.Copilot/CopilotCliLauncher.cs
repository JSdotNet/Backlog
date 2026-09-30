using System.ComponentModel;
using System.Diagnostics;

namespace Backlog.Infrastructure.Copilot;

public sealed record CopilotCliRequest(string Prompt, string? WorkingDirectory);

public interface ICopilotCliLauncher
{
    Task LaunchAsync(CopilotCliRequest request, CancellationToken cancellationToken = default);
}

public sealed class ProcessCopilotCliLauncher : ICopilotCliLauncher
{
    private const string DefaultExecutable = "copilot";
    private readonly string _executable;
    private readonly Func<ProcessStartInfo, Process?> _startProcess;

    public ProcessCopilotCliLauncher()
        : this(Environment.GetEnvironmentVariable("BACKLOG_COPILOT_CLI"))
    {
    }

    internal ProcessCopilotCliLauncher(string? executable)
        : this(executable, Process.Start)
    {
    }

    /// <summary>The process seam: the tests assert what the launcher starts and
    /// what it does with the returned Process without spawning the CLI.</summary>
    internal ProcessCopilotCliLauncher(string? executable, Func<ProcessStartInfo, Process?> startProcess)
    {
        _executable = string.IsNullOrWhiteSpace(executable) ? DefaultExecutable : executable.Trim();
        _startProcess = startProcess;
    }

    public Task LaunchAsync(CopilotCliRequest request, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (string.IsNullOrWhiteSpace(request.Prompt))
        {
            throw new CopilotCliException("There is no prompt to send to GitHub Copilot CLI.");
        }

        var startInfo = new ProcessStartInfo(_executable)
        {
            UseShellExecute = false,
            CreateNoWindow = false
        };

        if (!string.IsNullOrWhiteSpace(request.WorkingDirectory))
        {
            if (!Directory.Exists(request.WorkingDirectory))
            {
                throw new CopilotCliException($"The Copilot CLI working folder does not exist: {request.WorkingDirectory}");
            }

            startInfo.WorkingDirectory = request.WorkingDirectory;
        }

        startInfo.ArgumentList.Add(request.Prompt);

        try
        {
            // The CLI runs on in its own console window and is never waited on;
            // disposing releases the handle without touching the child.
            using var process = _startProcess(startInfo) ?? throw new CopilotCliException("GitHub Copilot CLI did not start.");
        }
        catch (Win32Exception ex)
        {
            throw new CopilotCliException(
                $"Couldn't start GitHub Copilot CLI. Install '{DefaultExecutable}' or set BACKLOG_COPILOT_CLI to the executable path.",
                ex);
        }

        return Task.CompletedTask;
    }
}

public sealed class CopilotCliException : Exception
{
    public CopilotCliException(string message)
        : base(message)
    {
    }

    public CopilotCliException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

public sealed class UnavailableCopilotCliLauncher : ICopilotCliLauncher
{
    public Task LaunchAsync(CopilotCliRequest request, CancellationToken cancellationToken = default) =>
        throw new CopilotCliException("GitHub Copilot CLI support is not registered in this build.");
}
