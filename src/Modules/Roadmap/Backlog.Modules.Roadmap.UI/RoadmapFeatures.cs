namespace Backlog.Modules.Roadmap.UI;

/// <summary>
/// The feature keys Roadmap planning owns.
/// <para>
/// These sit in the UI project rather than an <c>.Abstractions</c> one because
/// Roadmap has no domain module yet — there is a planning view and nothing
/// underneath it. Creating an abstractions project to hold one constant would
/// publish a contract for a module nobody has written; when the module arrives
/// and something below the shell wants to gate on this, the key moves with it.
/// </para>
/// </summary>
public static class RoadmapFeatures
{
    /// <summary>Show the roadmap band above the panes in the Home shell.</summary>
    public const string Roadmap = "roadmap";
}
