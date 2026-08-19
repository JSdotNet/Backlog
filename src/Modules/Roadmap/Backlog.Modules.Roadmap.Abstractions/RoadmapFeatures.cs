namespace Backlog.Modules.Roadmap.Abstractions;

/// <summary>
/// The feature keys Roadmap planning owns.
/// <para>
/// This used to sit in the UI project, with a comment saying it would move here
/// when a domain module arrived and something below the shell wanted to gate on
/// it. The module has arrived (<c>.domain/roadmap</c>), so the key moved with it:
/// it names a capability of this bounded context rather than a control in one
/// shell, and both the band and anything else that later asks "is the roadmap
/// switched on" can read it without referencing a Razor project.
/// </para>
/// </summary>
public static class RoadmapFeatures
{
    /// <summary>Show the roadmap band above the panes in the Home shell.</summary>
    public const string Roadmap = "roadmap";
}
