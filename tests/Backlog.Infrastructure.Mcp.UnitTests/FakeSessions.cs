using Backlog.Modules.Sessions.Abstractions;
using Backlog.SharedKernel;

namespace Backlog.Infrastructure.Mcp.UnitTests;

/// <summary>
/// The composite source the unkeyed registration holds, standing in as one
/// catalog.
/// </summary>
internal sealed class FakeAgentSessionSource(AgentSessionCatalog catalog) : IAgentSessionSource
{
    /// <summary>Which of the two reads the tool made. The horizon read answers a
    /// different question, and a tool that called it would be answering one.</summary>
    public int NewestReads { get; private set; }

    public Task<AgentSessionCatalog> GetSessionsAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        NewestReads++;

        return Task.FromResult(catalog);
    }

    public Task<AgentSessionCatalog> GetSessionsAsync(AgentSessionQuery query, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        return Task.FromResult(catalog);
    }
}

/// <summary>Sessions with the fields these tests are about and defaults for the
/// rest.</summary>
internal static class Sessions
{
    internal static AgentSession Session(
        string id,
        string? repository = null,
        string? resolvedRepository = null,
        AgentSessionKind kind = AgentSessionKind.Claude,
        AgentSessionState state = AgentSessionState.Running) =>
        new(
            id,
            kind,
            EnvironmentId: "this-machine",
            Environment: "This machine",
            Title: $"Session {id}",
            WorkingFolder: @"D:\Repos\Backlog",
            repository,
            Branch: "main",
            StartedAt: new DateTimeOffset(2026, 9, 22, 8, 0, 0, TimeSpan.Zero),
            LastActivityAt: new DateTimeOffset(2026, 9, 22, 9, 0, 0, TimeSpan.Zero),
            state,
            TurnCount: 12,
            AgentSessionOrigin.Local)
        {
            ResolvedRepository = resolvedRepository
        };
}

/// <summary>
/// The feature switches, as a set of keys that are on.
/// <para>
/// It throws for a key it has never heard of, the way the real store does: an
/// unknown feature is a typo in code, not a choice somebody made, and a double
/// that answered <c>false</c> would turn a misspelled key into a silently hidden
/// tool.
/// </para>
/// </summary>
internal sealed class FakeAppFeatureSettings(params string[] known) : IAppFeatureSettings
{
    private readonly HashSet<string> _known = new(known, StringComparer.OrdinalIgnoreCase);

    public event Action? Changed
    {
        add { }
        remove { }
    }

    public AppFeatureSettings Current { get; } = new();

    public string SettingsPath => "in-memory";

    public HashSet<string> Enabled { get; } = new(known, StringComparer.OrdinalIgnoreCase);

    public bool IsEnabled(string key) => _known.Contains(key)
        ? Enabled.Contains(key)
        : throw new ArgumentOutOfRangeException(nameof(key), key, "No catalog defines this feature.");

    public string? SetEnabled(string key, bool enabled)
    {
        if (enabled) Enabled.Add(key); else Enabled.Remove(key);

        return null;
    }
}
