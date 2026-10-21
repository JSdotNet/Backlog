namespace Backlog.UI.Components.Diagrams.C4;

/// <summary>One class box, placed.</summary>
/// <param name="Members">The member lines drawn in it, cut to what the box holds.</param>
/// <param name="Hidden">How many member lines did not fit, said under the last one.</param>
public sealed record C4PlacedCodeType(
    C4CodeType Type,
    double X,
    double Y,
    double Width,
    double Height,
    IReadOnlyList<string> Members,
    int Hidden);

/// <summary>One use, placed: the path <see cref="C4LayoutEngine"/> routed.</summary>
public sealed record C4PlacedCodeUse(C4CodeUse Use, string Path, double LabelX, double LabelY);

/// <summary>The Code level of one component, laid out.</summary>
public sealed record C4CodeDiagram(
    double Width,
    double Height,
    IReadOnlyList<C4PlacedCodeType> Types,
    IReadOnlyList<C4PlacedCodeUse> Uses)
{
    public static C4CodeDiagram Empty { get; } = new(0, 0, [], []);
}

/// <summary>
/// Lays the Code level out with <see cref="C4LayoutEngine"/>, the layout every other
/// level of the zoom tab is drawn by, so a use reads top to bottom the way a
/// relationship does one level up.
/// <para>
/// The engine lays out a C4 view, so each type is handed to it as a software system of
/// a landscape and each use as a relationship: the landscape is the one view kind that
/// layers every element on the edges between them rather than gridding them inside a
/// boundary. The cards are asked for at the class box's size — one size for the whole
/// picture, tall enough for the longest member list shown, up to
/// <see cref="MaxMemberLines"/>.
/// </para>
/// </summary>
public static class C4CodeLayout
{
    /// <summary>The width of a class box, as the design draws it.</summary>
    public const double TypeWidth = 300;

    /// <summary>One line of a class box.</summary>
    public const double LineHeight = 20;

    /// <summary>The most member lines a box shows before it says how many more there
    /// are. The type panel and the editor hold the rest.</summary>
    public const int MaxMemberLines = 8;

    /// <summary>Above and below the lines, and the stereotype and name over them.</summary>
    private const double Padding = 24;
    private const int HeaderLines = 2;

    /// <summary>The element id a type is handed to the engine under: by position, so
    /// a generic's angle brackets never reach an alias.</summary>
    private static string ElementId(int index) => "code-type-" + index.ToString(System.Globalization.CultureInfo.InvariantCulture);

    public static C4CodeDiagram Build(C4CodeLevel level, bool allMembers)
    {
        ArgumentNullException.ThrowIfNull(level);
        if (level.Types.Count == 0) return C4CodeDiagram.Empty;

        var ids = new Dictionary<string, string>(StringComparer.Ordinal);
        var elements = new List<C4Element>(level.Types.Count);

        for (var index = 0; index < level.Types.Count; index++)
        {
            var type = level.Types[index];
            if (!ids.TryAdd(type.Id, ElementId(index))) continue;
            elements.Add(new C4Element(ElementId(index), C4ElementKind.SoftwareSystem, type.Name, null, null, [], null, null));
        }

        // A use of a type outside the level, or of itself, has nowhere to be drawn.
        var drawn = level.Uses
            .Where(use => !string.Equals(use.From, use.To, StringComparison.Ordinal)
                && ids.ContainsKey(use.From)
                && ids.ContainsKey(use.To))
            .ToList();

        var relationships = drawn
            .Select(use => new C4Relationship(ids[use.From], ids[use.To], use.Inherits ? "inherits" : null, null, []))
            .ToList();

        var view = new C4View("code", C4ViewKind.SystemLandscape, null, null, null, [], [], true, null, []);
        var workspace = new C4Workspace(level.Component, null, elements, relationships, [view], []);

        var lines = Math.Min(MaxMemberLines, level.Types.Max(type => Members(type, allMembers).Count));
        var height = Padding + (HeaderLines + lines) * LineHeight;

        var diagram = C4LayoutEngine.Build(workspace, view, new C4LayoutOptions { CardWidth = TypeWidth, CardHeight = height });

        var byAlias = new Dictionary<string, C4CodeType>(StringComparer.Ordinal);
        foreach (var type in level.Types)
        {
            if (ids.TryGetValue(type.Id, out var id)) byAlias.TryAdd(C4MermaidWriter.AliasOf(id), type);
        }

        var types = new List<C4PlacedCodeType>();
        foreach (var node in diagram.Nodes)
        {
            if (!byAlias.TryGetValue(node.Alias, out var type)) continue;

            var members = Members(type, allMembers);
            var shown = members.Count > lines ? lines - 1 : members.Count;
            types.Add(new C4PlacedCodeType(type, node.X, node.Y, node.Width, node.Height, [.. members.Take(shown)], members.Count - shown));
        }

        // Each relationship came from one use, in order, and the engine draws an edge
        // per relationship; the edge is matched back by its two ends.
        var uses = new List<C4PlacedCodeUse>();
        var pending = new List<C4CodeUse>(drawn);

        foreach (var edge in diagram.Edges)
        {
            var match = pending.FirstOrDefault(use =>
                string.Equals(C4MermaidWriter.AliasOf(ids[use.From]), edge.FromAlias, StringComparison.Ordinal)
                && string.Equals(C4MermaidWriter.AliasOf(ids[use.To]), edge.ToAlias, StringComparison.Ordinal));
            if (match is null) continue;

            pending.Remove(match);
            uses.Add(new C4PlacedCodeUse(match, edge.Path, edge.LabelX, edge.LabelY));
        }

        return new C4CodeDiagram(diagram.Width, diagram.Height, types, uses);
    }

    private static IReadOnlyList<string> Members(C4CodeType type, bool allMembers) =>
        allMembers ? type.AllMembers : type.PublicMembers;
}
