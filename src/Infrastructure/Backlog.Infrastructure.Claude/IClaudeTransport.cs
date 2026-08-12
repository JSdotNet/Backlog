using System.Text.Json;

namespace Backlog.Infrastructure.Claude;

/// <summary>
/// A way of reaching the Claude Admin API. Everything above this interface is
/// written in terms of resource paths
/// (<c>v1/organizations/usage_report/messages</c>), so how a request is
/// authenticated never leaks into the usage client.
/// </summary>
public interface IClaudeTransport
{
    /// <summary>How this transport is described in Settings, e.g. "Admin API key".</summary>
    string Description { get; }

    /// <summary>True when this transport can actually authenticate right now.</summary>
    Task<bool> IsAvailableAsync(CancellationToken cancellationToken = default);

    /// <summary>Sends a request and returns the parsed JSON response. Throws
    /// <see cref="ClaudeException"/> for anything Anthropic refused.</summary>
    Task<JsonElement> SendAsync(
        HttpMethod method,
        string path,
        CancellationToken cancellationToken = default);
}

/// <summary>Anything Anthropic or the network refused. Carries a message fit to
/// put in front of a person rather than a stack trace.</summary>
public sealed class ClaudeException(string message, Exception? inner = null) : Exception(message, inner);

/// <summary>
/// Raised when there is no admin key configured — a settings problem, not a
/// failure.
/// <para>
/// This is also the honest answer for a personal Anthropic account. The usage
/// and cost reports live on the Admin API, which Anthropic documents as
/// unavailable to individual accounts, so reaching them at all needs an
/// organization in the Claude Console.
/// </para>
/// </summary>
public sealed class ClaudeNotConfiguredException(string message) : Exception(message);
