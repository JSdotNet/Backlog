using System.Text.Json;

namespace Backlog.GitHub;

/// <summary>
/// A way of reaching the GitHub REST API. Both implementations speak the same
/// resource paths (<c>repos/owner/name/issues</c>), so everything above this
/// interface is written once regardless of how the call actually leaves the
/// machine.
/// </summary>
public interface IGitHubTransport
{
    /// <summary>How this transport is described in Settings, e.g. "GitHub CLI".</summary>
    string Description { get; }

    /// <summary>True when this transport can actually authenticate right now.</summary>
    Task<bool> IsAvailableAsync(CancellationToken cancellationToken = default);

    /// <summary>Sends a request and returns the parsed JSON response. Throws
    /// <see cref="GitHubException"/> for anything GitHub refused.</summary>
    Task<JsonElement> SendAsync(
        HttpMethod method,
        string path,
        object? body = null,
        CancellationToken cancellationToken = default);
}

/// <summary>Anything GitHub, the CLI, or the network refused. Carries a message
/// fit to put in front of a person rather than a stack trace.</summary>
public sealed class GitHubException(string message, Exception? inner = null) : Exception(message, inner);

/// <summary>Raised when there is no repository configured, or no way to
/// authenticate — a settings problem, not a failure.</summary>
public sealed class GitHubNotConfiguredException(string message) : Exception(message);
