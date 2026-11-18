namespace Backlog.Modules.Sync.Abstractions;

/// <summary>
/// The bound on the remote connector's refresh-token lifetime (local ADR 0052 §8), written
/// down once because two assemblies depend on it agreeing: the service's
/// <c>ConnectorOptions</c> refuses a <c>RefreshTokenDays</c> above it, and the Cosmos store
/// keeps a revoked family's marker for at least that long. A marker reaped before the last
/// token of its family expires would let that token refresh again, so raising this raises
/// both together.
/// </summary>
public static class ConnectorTokenLimits
{
    /// <summary>The longest one refresh token may live, in days.</summary>
    public const int MaxRefreshTokenDays = 90;
}
