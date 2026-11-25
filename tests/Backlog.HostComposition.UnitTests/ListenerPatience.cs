namespace Backlog.HostComposition.UnitTests;

/// <summary>
/// How long a test here lets a local listener take to come up, or to say why it did not —
/// <see cref="McpServerWorkerTests"/>'s worker and <see cref="DesktopMcpEndpointSourceTests"/>'s
/// endpoint source alike.
/// </summary>
internal static class ListenerPatience
{
    /// <summary>
    /// A bound, not a wait: every test returns as soon as the listener's <c>Changed</c> says
    /// so. Thirty seconds because the nightly run, with every test module at once on one
    /// runner, saw a Kestrel start take longer than ten with nothing wrong (run 45167248847:
    /// two listeners on free ports, no <c>LastError</c>, both out of time at the same
    /// instant).
    /// </summary>
    public static readonly TimeSpan Bound = TimeSpan.FromSeconds(30);
}
