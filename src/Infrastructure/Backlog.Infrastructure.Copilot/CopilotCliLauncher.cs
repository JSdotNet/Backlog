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

    public ProcessCopilotCliLauncher()
        : this(Environment.GetEnvironmentVariable("BACKLOG_COPILOT_CLI"))
    {
    }

    internal ProcessCopilotCliLauncher(string? executable)
    {
        _executable = string.IsNullOrWhiteSpace(executable) ? DefaultExecutable : executable.Trim();
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
            if (Process.Start(startInfo) is null)
            {
                throw new CopilotCliException("GitHub Copilot CLI did not start.");
            }
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
