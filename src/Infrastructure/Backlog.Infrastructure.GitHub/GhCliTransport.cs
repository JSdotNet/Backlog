using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace Backlog.Infrastructure.GitHub;

/// <summary>
/// Talks to GitHub through the <c>gh</c> CLI the person is already signed in to.
/// Preferred over a token because it means the app never holds a credential:
/// <c>gh</c> keeps it, refreshes it, and revokes it.
/// <para>
/// Narrowed to the <b>default path only</b>. <c>gh api</c> has no per-call account
/// selector, so this can speak as one identity — whoever <c>gh</c> is currently
/// switched to — and that is exactly right for a repository nobody has bound to an
/// account, which is today's behaviour and the great majority of calls. A bound
/// repository goes over HTTP instead, through <see cref="TokenTransport"/>, because
/// there is no way to ask this to be somebody else for one call and asking the
/// machine to switch would change state the app does not own.
/// </para>
/// <para>
/// Kept rather than replaced by extracting a token for the active account too: for
/// the default path no credential extraction is needed at all, which preserves the
/// property this class exists for.
/// </para>
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
        string? apiVersion = null,
        CancellationToken cancellationToken = default)
    {
        var arguments = new List<string> { "api", "--method", method.Method, path.TrimStart('/') };

        // `gh api` sends its own X-GitHub-Api-Version, so the version has to be
        // overridden per call rather than left to the CLI's default — otherwise the
        // billing endpoints answer 404 through the CLI while working through a
        // token, which is the sort of difference between two transports that takes
        // a long afternoon to find.
        arguments.AddRange(
        [
            "--header",
            "X-GitHub-Api-Version: "
                + (string.IsNullOrWhiteSpace(apiVersion) ? IGitHubTransport.DefaultApiVersion : apiVersion.Trim())
        ]);

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
