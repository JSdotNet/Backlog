using System.Text.Json;

namespace Backlog.Infrastructure.GitHub;

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
    /// <param name="apiVersion">
    /// The <c>X-GitHub-Api-Version</c> to send, or null for
    /// <see cref="DefaultApiVersion"/>.
    /// <para>
    /// A per-request choice rather than one setting for the whole transport,
    /// because GitHub does not move its endpoints to a new version together. The
    /// billing usage reports only exist from <c>2026-03-10</c>, while everything
    /// else this app calls is still documented against <c>2022-11-28</c> — so a
    /// transport pinned to either version can reach some endpoints and not
    /// others, and pinning it to the newer one to reach billing would silently
    /// re-version every issue and pull request call as a side effect.
    /// </para>
    /// </param>
    Task<JsonElement> SendAsync(
        HttpMethod method,
        string path,
        object? body = null,
        string? apiVersion = null,
        CancellationToken cancellationToken = default);

    /// <summary>The version every endpoint that has not asked for another one is
    /// called against.</summary>
    const string DefaultApiVersion = "2022-11-28";
}

/// <summary>Anything GitHub, the CLI, or the network refused. Carries a message
/// fit to put in front of a person rather than a stack trace.</summary>
public sealed class GitHubException(string message, Exception? inner = null) : Exception(message, inner);

/// <summary>Raised when there is no repository configured, or no way to
/// authenticate — a settings problem, not a failure.</summary>
public sealed class GitHubNotConfiguredException(string message) : Exception(message);
