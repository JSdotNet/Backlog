namespace Backlog.Modules.Sessions.Abstractions;

/// <summary>What the renderer produced for one specification: the artifact, or why
/// there is none.</summary>
/// <param name="Html">The standalone Archify document, or null.</param>
/// <param name="ArtifactPath">Where it was written. Named after a hash of the
/// specification, so a changed run is a changed path — which is what tells the diagram
/// view to reload its frame.</param>
/// <param name="Unavailable">Why there is no artifact, in words a reader can act on,
/// or null when there is one.</param>
public sealed record DeliveryRunDiagram(string? Html, string? ArtifactPath, string? Unavailable)
{
    public static DeliveryRunDiagram Failed(string reason) => new(null, null, reason);
}

/// <summary>
/// Turns an Archify specification into the artifact a run's fold shows.
/// <para>
/// A port rather than a call, for one reason: the line resolves it optionally. A host
/// that registers none, and every test that renders the pane, keeps the live flow it
/// always had instead of failing to inject. Published here rather than kept beside the
/// line because the generator it runs is an adapter under <c>src/Infrastructure</c>,
/// which the pane's project may not see.
/// </para>
/// </summary>
public interface IDeliveryRunDiagrams
{
    /// <summary>Renders <paramref name="specification"/>. Never throws for a
    /// failure a reader could have — no Node, no generator, a rejected
    /// specification — and answers with the reason instead.</summary>
    Task<DeliveryRunDiagram> RenderAsync(string specification, CancellationToken cancellationToken = default);
}
