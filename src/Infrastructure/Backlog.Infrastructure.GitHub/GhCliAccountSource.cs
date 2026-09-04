using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace Backlog.Infrastructure.GitHub;

/// <summary>One login the <c>gh</c> CLI is signed in to on this machine.</summary>
public sealed record GhCliAccount(string Login, string Host, bool Active, string? Scopes);

/// <summary>
/// The <c>gh</c> CLI as a source of credentials rather than as a way of sending
/// requests.
/// <para>
/// <c>gh api</c> has no per-call account selector — it has <c>--hostname</c> and
/// nothing else — so the CLI can only ever speak as whoever it is currently
/// switched to. That is the whole reason calls for one owner's repository used to
/// leave carrying another owner's identity. What the CLI <em>can</em> do is hand
/// over the token for a named login, including an inactive one, and the app can
/// then send the request itself.
/// </para>
/// <para>
/// <c>gh auth switch</c> is the alternative and is rejected: it rewrites
/// <c>~/.config/gh/hosts.yml</c>, which the user's terminals, their editor, every
/// other <c>gh</c> invocation and any second Backlog window all read. It would race
/// with all of them and change state this app does not own.
/// </para>
/// </summary>
public interface IGhCliAccountSource
{
    /// <summary>Every login <c>gh</c> is signed in to. Feeds the Settings picker, so
    /// an account is chosen from a list rather than spelled out — a typo in a login
    /// surfaces as a 404, which is the class of failure being removed.</summary>
    Task<IReadOnlyList<GhCliAccount>> ListAsync(CancellationToken cancellationToken = default);

    /// <summary>The token for one login, or null when <c>gh</c> has none. Held in
    /// memory only, never written down.</summary>
    Task<string?> GetTokenAsync(string login, string? host = null, CancellationToken cancellationToken = default);

    /// <summary>Forgets what was worked out, so a <c>gh auth login</c> or
    /// <c>gh auth logout</c> in another window is noticed without restarting the
    /// app. Reached from Settings' "Check the connection" button.</summary>
    void Invalidate();
}

/// <summary>
/// Asks the real <c>gh</c> CLI.
/// <para>
/// A gh-sourced token is <b>never persisted</b>. <c>gho_</c> tokens are OAuth
/// tokens the CLI refreshes and rotates, so one written into the settings file
/// would be a stale secret in a file — a correctness regression and a security
/// one. They are cached in memory for a few minutes instead, which is ample: the
/// cost of a miss is one fast subprocess.
/// </para>
/// </summary>
public sealed class GhCliAccountSource : IGhCliAccountSource
{
    /// <summary>How long a fetched token is reused before <c>gh</c> is asked again.
    /// Short enough that a rotation is picked up on its own, long enough that a
    /// screen full of calls costs one subprocess rather than thirty.</summary>
    public static readonly TimeSpan TokenLifetime = TimeSpan.FromMinutes(5);

    private readonly string _executable;
    private readonly TimeProvider _time;
    private readonly Lock _gate = new();
    private readonly Dictionary<string, CachedToken> _tokens = new(StringComparer.OrdinalIgnoreCase);

    private IReadOnlyList<GhCliAccount>? _accounts;

    public GhCliAccountSource(string executable = "gh", TimeProvider? time = null)
    {
        _executable = executable;
        _time = time ?? TimeProvider.System;
    }

    public async Task<IReadOnlyList<GhCliAccount>> ListAsync(CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            if (_accounts is { } cached) return cached;
        }

        var accounts = await ReadAccountsAsync(cancellationToken);

        lock (_gate)
        {
            _accounts = accounts;
        }

        return accounts;
    }

    public async Task<string?> GetTokenAsync(
        string login,
        string? host = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(login)) return null;

        var key = Key(login, host);
        var now = _time.GetUtcNow();

        lock (_gate)
        {
            if (_tokens.TryGetValue(key, out var cached) && cached.Expires > now) return cached.Token;
        }

        var arguments = new List<string> { "auth", "token", "--user", login.Trim() };
        if (!string.IsNullOrWhiteSpace(host)) arguments.AddRange(["--hostname", host.Trim()]);

        string? token;
        try
        {
            var result = await RunAsync(arguments, cancellationToken);
            token = result.ExitCode == 0 ? Blank(result.StandardOutput.Trim()) : null;
        }
        catch (Exception)
        {
            // No gh on PATH, or a gh too old to know `--user`. Not a failure: the
            // account can be given a pasted token instead, and the caller reports
            // the binding it could not satisfy.
            token = null;
        }

        lock (_gate)
        {
            // A negative answer is remembered too, so a bound account the CLI cannot
            // satisfy does not launch a subprocess per call for the whole session.
            _tokens[key] = new CachedToken(token, now + TokenLifetime);
        }

        return token;
    }

    public void Invalidate()
    {
        lock (_gate)
        {
            _accounts = null;
            _tokens.Clear();
        }
    }

    /// <summary>
    /// <c>gh auth status --json hosts</c>, which answers
    /// <c>{"hosts":{"github.com":[{"login","state","scopes",…}]}}</c>. Settings can
    /// therefore enumerate accounts without anybody pasting a token.
    /// </summary>
    private async Task<IReadOnlyList<GhCliAccount>> ReadAccountsAsync(CancellationToken cancellationToken)
    {
        try
        {
            var result = await RunAsync(["auth", "status", "--json", "hosts"], cancellationToken);
            if (result.ExitCode != 0) return [];

            using var document = JsonDocument.Parse(result.StandardOutput);
            if (!document.RootElement.TryGetProperty("hosts", out var hosts)) return [];

            var accounts = new List<GhCliAccount>();

            foreach (var host in hosts.EnumerateObject())
            {
                if (host.Value.ValueKind is not JsonValueKind.Array) continue;

                foreach (var entry in host.Value.EnumerateArray())
                {
                    if (Text(entry, "login") is not { } login) continue;

                    accounts.Add(new GhCliAccount(
                        login,
                        Text(entry, "host") ?? host.Name,
                        string.Equals(Text(entry, "state"), "active", StringComparison.OrdinalIgnoreCase),
                        Text(entry, "scopes")));
                }
            }

            return accounts;
        }
        catch (Exception)
        {
            // No gh, a gh too old for `--json hosts`, or an answer that was not
            // JSON. An empty list degrades the Settings picker to manual entry
            // rather than blocking the panel.
            return [];
        }
    }

    private static string? Text(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) && value.ValueKind is JsonValueKind.String
            ? Blank(value.GetString())
            : null;

    private static string? Blank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;

    private static string Key(string login, string? host) => $"{host?.Trim() ?? string.Empty}/{login.Trim()}";

    private async Task<ProcessResult> RunAsync(IReadOnlyList<string> arguments, CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = _executable,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,

            // The desktop head has no console of its own, so Windows would give
            // every one of these a brand new window. Redirecting the streams does
            // not ask for that to be suppressed; this is the only thing that does.
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

        var standardOutput = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var standardError = process.StandardError.ReadToEndAsync(cancellationToken);

        await process.WaitForExitAsync(cancellationToken);

        return new ProcessResult(process.ExitCode, await standardOutput, await standardError);
    }

    private sealed record ProcessResult(int ExitCode, string StandardOutput, string StandardError);

    private sealed record CachedToken(string? Token, DateTimeOffset Expires);
}
