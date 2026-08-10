using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace Backlog.GitHub;

/// <summary>
/// Talks to GitHub through the <c>gh</c> CLI the person is already signed in to.
/// Preferred over a token because it means the app never holds a credential:
/// <c>gh</c> keeps it, refreshes it, and revokes it.
/// </summary>
public sealed class GhCliTransport : IGitHubTransport
{
    private readonly string _executable;
    private bool? _available;

    public GhCliTransport(string executable = "gh") => _executable = executable;

    public string Description => "GitHub CLI";

    /// <summary>Who <c>gh</c> is signed in as, once availability has been
    /// checked. Null when it isn't signed in.</summary>
    public string? Account { get; private set; }

    /// <summary>Forgets the cached availability result, so a fresh
    /// <c>gh auth login</c> is noticed without restarting the app.</summary>
    public void Invalidate()
    {
        _available = null;
        Account = null;
    }

    public async Task<bool> IsAvailableAsync(CancellationToken cancellationToken = default)
    {
        if (_available is { } cached) return cached;

        try
        {
            // `gh api user` proves both halves at once: the CLI exists, and its
            // stored credential is actually good. `gh auth status` can pass with
            // a token that no longer works.
            var result = await RunAsync(["api", "user"], input: null, cancellationToken);
            if (result.ExitCode != 0)
            {
                _available = false;
                return false;
            }

            using var document = JsonDocument.Parse(result.StandardOutput);
            Account = document.RootElement.TryGetProperty("login", out var login) ? login.GetString() : null;
            _available = true;
        }
        catch (Exception)
        {
            // No gh on PATH, or it produced something that wasn't JSON.
            _available = false;
        }

        return _available.Value;
    }

    public async Task<JsonElement> SendAsync(
        HttpMethod method,
        string path,
        object? body = null,
        CancellationToken cancellationToken = default)
    {
        var arguments = new List<string> { "api", "--method", method.Method, path.TrimStart('/') };

        string? input = null;
        if (body is not null)
        {
            input = JsonSerializer.Serialize(body, GitHubJson.Options);
            arguments.AddRange(["--input", "-"]);
        }

        var result = await RunAsync(arguments, input, cancellationToken);

        if (result.ExitCode != 0)
        {
            var message = result.StandardError.Trim();
            throw new GitHubException(message.Length == 0
                ? $"The GitHub CLI failed on {method.Method} {path}."
                : message);
        }

        try
        {
            return JsonDocument.Parse(
                result.StandardOutput.Length == 0 ? "null" : result.StandardOutput).RootElement.Clone();
        }
        catch (JsonException ex)
        {
            throw new GitHubException("The GitHub CLI returned something that wasn't JSON.", ex);
        }
    }

    private async Task<ProcessResult> RunAsync(
        IReadOnlyList<string> arguments,
        string? input,
        CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = _executable,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = input is not null,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };

        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = Process.Start(startInfo)
            ?? throw new GitHubException("The GitHub CLI could not be started.");

        if (input is not null)
        {
            await process.StandardInput.WriteAsync(input.AsMemory(), cancellationToken);
            process.StandardInput.Close();
        }

        var standardOutput = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var standardError = process.StandardError.ReadToEndAsync(cancellationToken);

        await process.WaitForExitAsync(cancellationToken);

        return new ProcessResult(process.ExitCode, await standardOutput, await standardError);
    }

    private sealed record ProcessResult(int ExitCode, string StandardOutput, string StandardError);
}
