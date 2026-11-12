using Backlog.SharedKernel.Mcp;

namespace Backlog.Tests;

/// <summary>
/// A grant store that answers through <paramref name="inner"/> until a test sets
/// <see cref="Failure"/>, then throws it from every call — what a locked, read-only or
/// unreadable <c>mcp-grants.json</c> does to the real store's reads and reseal writes.
/// </summary>
internal sealed class FailableMcpGrantStore(IMcpGrantStore inner) : IMcpGrantStore
{
    /// <summary>What every call throws while set.</summary>
    public Exception? Failure { get; set; }

    public event Action? Changed
    {
        add => inner.Changed += value;
        remove => inner.Changed -= value;
    }

    public IReadOnlyList<McpGrant> List() => Answer(inner.List);

    public McpGrant? Find(string id) => Answer(() => inner.Find(id));

    public McpGrant Create(string name, IReadOnlyList<string> scopes, string createdBy) => Answer(() => inner.Create(name, scopes, createdBy));

    public McpGrant? Revoke(string id) => Answer(() => inner.Revoke(id));

    private T Answer<T>(Func<T> call) => Failure is { } failure ? throw failure : call();
}
