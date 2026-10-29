using Backlog.SharedKernel;

namespace Backlog.Infrastructure.Mcp.UnitTests;

/// <summary>
/// An <see cref="ISurfaceWindowHost"/> that records what it was asked, with a native
/// window or without one, and a web page served or not.
/// </summary>
internal sealed class FakeSurfaceWindows(bool nativeWindows = true, int? listenerPort = 5757) : ISurfaceWindowHost
{
    /// <summary>The feature a test switches off to take <c>roadmap</c> out of the catalog.</summary>
    public const string RoadmapFeature = "roadmap";

    public List<(string SurfaceId, IReadOnlyDictionary<string, string> Query)> Opened { get; } = [];

    public List<(string SurfaceId, IReadOnlyDictionary<string, string> Query)> Linked { get; } = [];

    public bool TryOpenWindow(string surfaceId, IReadOnlyDictionary<string, string> query)
    {
        if (!nativeWindows) return false;

        Opened.Add((surfaceId, query));
        return true;
    }

    public Uri? WebLink(string surfaceId, IReadOnlyDictionary<string, string> query)
    {
        if (listenerPort is not { } port) return null;

        Linked.Add((surfaceId, query));
        return new Uri($"http://localhost:{port}" + SurfaceLink.Path(surfaceId, query.Append(new("t", "token"))));
    }

    /// <summary>
    /// A catalog of the surfaces the desktop registers, with the keys each takes: Tasks,
    /// Board, the Roadmap behind <see cref="RoadmapFeature"/>, and the Devbook.
    /// </summary>
    public static SurfaceCatalog Catalog(FakeAppFeatureSettings? features = null) =>
        new(
            [
                new ShellSurface("tasks", "Tasks", ShellSurfaceGroup.View, 0, typeof(object), QueryKeys: ["plan", "linked", "entry"]),
                new ShellSurface("board", "Board", ShellSurfaceGroup.View, 3, typeof(object), QueryKeys: ["plan", "linked"]),
                new ShellSurface("roadmap", "Roadmap", ShellSurfaceGroup.View, 1, typeof(object), RoadmapFeature, ["plan"]),
                new ShellSurface("devbook", "Devbook", ShellSurfaceGroup.Takeover, 0, typeof(object), QueryKeys: ["chapter", "scope", "switch", "request"]),
            ],
            features ?? Everything());

    private static FakeAppFeatureSettings Everything() => new(RoadmapFeature);
}
