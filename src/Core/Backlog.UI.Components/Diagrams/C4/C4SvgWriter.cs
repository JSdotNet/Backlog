using System.Globalization;
using System.Text;

namespace Backlog.UI.Components.Diagrams.C4;

/// <summary>
/// What a host may add to the picture beyond what every C4 view draws.
/// <para>
/// Every option is off by default, and a writer handed no options draws exactly
/// what it drew before options existed. The C4 tab's explorer never passes any; the
/// C4 zoom tab passes the ones its navigation needs. That is the rule local ADR 0043
/// sets for the two tabs while they stand side by side: the new one may ask the shared
/// renderer for more, and the old one must not notice.
/// </para>
/// </summary>
/// <param name="ZoomInAliases">The cards that carry a <c>Zoom in</c> chip: a clickable
/// mark, <c>g.c4-diagram__zoom-in</c> with <c>data-c4-zoom-in</c>, on the top edge of the
/// card. Named by the host rather than read off <see cref="C4PlacedNode.Drillable"/>
/// because where a card zooms to is the host's navigation, not the layout's: on a
/// landscape the zoom tab opens a system's context view, which no drill offers.</param>
/// <param name="NumberSteps">Whether a dynamic view's arrows carry their step number in a
/// badge, <c>g.c4-diagram__step-badge</c>, rather than as a prefix to the label, and every
/// numbered arrow says which step it is with <c>data-c4-step</c>. The zoom tab's steps
/// panel plays the view by these numbers.</param>
/// <param name="CurrentStep">The step drawn as the current one: its arrow, label and
/// badge carry <c>--current</c>, and every other numbered arrow does not. Read only with
/// <paramref name="NumberSteps"/>.</param>
public sealed record C4SvgOptions(
    IReadOnlySet<string>? ZoomInAliases = null,
    bool NumberSteps = false,
    int? CurrentStep = null)
{
    /// <summary>No additions: the picture every view drew before options existed.</summary>
    public static C4SvgOptions None { get; } = new();

    /// <summary>
    /// The frames, by the alias of the element each is drawn for, that a reader can
    /// select as well as look at. Such a frame is written as a <c>g.node</c> with the
    /// card's id contract, <c>&lt;svg id&gt;-&lt;alias&gt;</c>, so the zoom tab's viewer
    /// selects it the way it selects a card. The zoom tab sets it on a deployment
    /// view, where a deployment node that runs something is drawn as a frame and is
    /// still the thing a reader asks about.
    /// </summary>
    public IReadOnlySet<string>? SelectableBoundaryAliases { get; init; }

    /// <summary>
    /// The record counts the zoom tab's overlay puts on cards, by the alias of each
    /// card. A card named here with a decision count draws an <c>n ADR</c> chip; with an
    /// open-debt count it draws an <c>n TDR</c> chip and is drawn on the debt surface
    /// (<c>c4-diagram__node--debt</c>). A count of zero draws nothing, and a card not
    /// named here is drawn as it always was. Null, the default, is no overlay.
    /// </summary>
    public IReadOnlyDictionary<string, C4CardRecords>? RecordChips { get; init; }
}

/// <summary>What the overlay counts on one card: the decision records (ADRs) and the
/// open debt records (TDRs not yet resolved) that name its element. The host puts in
/// only the count its overlay shows, so the other one is zero.</summary>
public sealed record C4CardRecords(int Decisions, int OpenDebt);

/// <summary>
/// Draws a laid-out C4 view as SVG.
/// <para>
/// This replaced mermaid for C4 views. Mermaid could not be made to look like the tool
/// the model is authored in: it sizes its boxes from their own text, draws no icon,
/// routes edges its own way, and writes every colour inline where no stylesheet can
/// reach. Themeing it was never going to get past "a mermaid diagram in this
/// product's colours".
/// </para>
/// <para>
/// A string rather than Razor markup for two reasons. Razor reserves <c>&lt;text&gt;</c>
/// as its own markup-transition tag, so SVG text cannot be written inline at all. And
/// a string is testable the way <see cref="C4MermaidWriter"/> is — the output can be
/// asserted without rendering a component, which is what lets the card, the frame and
/// the edge be pinned directly.
/// </para>
/// <para>
/// The DOM contract is mermaid's on purpose: every card is a <c>g.node</c> whose id is
/// <c>&lt;svg id&gt;-&lt;alias&gt;</c>. That is what the explorer's viewer already reads
/// for click-to-drill, for dimming and for the search ring, so swapping the renderer
/// underneath it needed no change to any of that.
/// </para>
/// </summary>
public static class C4SvgWriter
{
    public static string Write(C4Diagram diagram, string id, string? ariaLabel = null, C4SvgOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(diagram);
        ArgumentException.ThrowIfNullOrWhiteSpace(id);

        var svg = new StringBuilder();
        var width = Math.Max(diagram.Width, 320);
        var height = Math.Max(diagram.Height, 200);

        svg.Append(Invariant($"<svg id=\"{Escape(id)}\" class=\"c4-diagram\" viewBox=\"0 0 {width:0.##} {height:0.##}\""))
            .Append(" width=\"100%\" height=\"100%\" preserveAspectRatio=\"xMidYMid meet\" role=\"img\"")
            .Append(Invariant($" aria-label=\"{Escape(ariaLabel ?? "C4 diagram")}\""))
            .Append(" xmlns=\"http://www.w3.org/2000/svg\">");

        // One marker per diagram rather than one per edge, and namespaced by the svg's
        // own id so two diagrams on a page cannot borrow each other's arrowhead.
        svg.Append(Invariant($"<defs><marker id=\"{Escape(id)}-arrow\" viewBox=\"0 0 10 10\" refX=\"9\" refY=\"5\""))
            .Append(" markerWidth=\"7\" markerHeight=\"7\" orient=\"auto-start-reverse\">")
            .Append("<path d=\"M 0 0 L 10 5 L 0 10 z\" class=\"c4-diagram__arrowhead\" /></marker></defs>");

        if (!diagram.HasContent)
        {
            svg.Append("<text x=\"50%\" y=\"50%\" class=\"c4-diagram__empty\" text-anchor=\"middle\">")
                .Append(Escape("This view selected no elements."))
                .Append("</text></svg>");

            return svg.ToString();
        }

        // Bands, then frames, then edges, then labels, then cards. Painting order is the
        // reading order: a card is never behind the line that leaves it, and a label is
        // never under the card it points at.
        options ??= C4SvgOptions.None;
        foreach (var group in diagram.Groups) WriteGroup(svg, group);
        foreach (var boundary in diagram.Boundaries) WriteBoundary(svg, id, boundary, options);
        foreach (var edge in diagram.Edges) WriteEdge(svg, id, edge, options);
        foreach (var edge in diagram.Edges) WriteEdgeLabel(svg, edge, options);
        foreach (var node in diagram.Nodes) WriteNode(svg, id, node, options);

        svg.Append("</svg>");
        return svg.ToString();
    }

    private static void WriteBoundary(StringBuilder svg, string id, C4PlacedBoundary boundary, C4SvgOptions options)
    {
        var alias = C4MermaidWriter.AliasOf(boundary.Id);

        svg.Append(options.SelectableBoundaryAliases?.Contains(alias) == true
            ? Invariant($"<g class=\"c4-diagram__boundary c4-diagram__boundary--selectable node\" id=\"{Escape(id)}-{Escape(alias)}\">")
            : "<g class=\"c4-diagram__boundary\">")
            .Append(Invariant($"<rect x=\"{boundary.X:0.##}\" y=\"{boundary.Y:0.##}\" width=\"{boundary.Width:0.##}\" height=\"{boundary.Height:0.##}\" rx=\"10\" />"))
            .Append(Invariant($"<text x=\"{boundary.X + 16:0.##}\" y=\"{boundary.Y + 22:0.##}\" class=\"c4-diagram__boundary-name\">{Escape(boundary.Label)}</text>"))
            .Append(Invariant($"<text x=\"{boundary.X + 16:0.##}\" y=\"{boundary.Y + 38:0.##}\" class=\"c4-diagram__boundary-kind\">[{Escape(boundary.Kind)}]</text>"))
            .Append("</g>");
    }

    /// <summary>
    /// A group band: a dashed, lightly filled frame with the group's name in its top
    /// corner, as the design's landscape draws the outside systems grouped by role.
    /// <para>
    /// No <c>node</c> class and no id: a band is a heading, not an element, so the
    /// viewer never selects, dims or drills it. <c>data-c4-group</c> names it for a
    /// host and a test. Only a layout asked for bands produces any, so a diagram without
    /// them writes exactly what it wrote before.
    /// </para>
    /// </summary>
    private static void WriteGroup(StringBuilder svg, C4PlacedGroup group) =>
        svg.Append(Invariant($"<g class=\"c4-diagram__group\" data-c4-group=\"{Escape(group.Name)}\">"))
            .Append(Invariant($"<rect x=\"{group.X:0.##}\" y=\"{group.Y:0.##}\" width=\"{group.Width:0.##}\" height=\"{group.Height:0.##}\" rx=\"12\" />"))
            .Append(Invariant($"<text x=\"{group.X + 14:0.##}\" y=\"{group.Y + 22:0.##}\" class=\"c4-diagram__group-name\">{Escape(Truncate(group.Name, GroupNameFits(group)))}</text>"))
            .Append("</g>");

    /// <summary>How many characters of a group's name fit across its band at the name's
    /// 12px size, so a long name ends in an ellipsis inside the frame rather than
    /// running out past its dashed edge.</summary>
    private static int GroupNameFits(C4PlacedGroup group) => Math.Max(8, (int)((group.Width - 28) / 6.6));

    private static void WriteEdge(StringBuilder svg, string id, C4PlacedEdge edge, C4SvgOptions options)
    {
        svg.Append("<g class=\"c4-diagram__edge").Append(StepModifiers(edge, options, "c4-diagram__edge"))
            .Append(edge.Group is null ? string.Empty : " c4-diagram__edge--group").Append('"')
            .Append(edge.Group is null ? string.Empty : Invariant($" data-c4-group=\"{Escape(edge.Group)}\""))
            .Append(StepAttribute(edge, options)).Append('>')
            .Append(Invariant($"<path d=\"{Escape(edge.Path)}\" marker-end=\"url(#{Escape(id)}-arrow)\" />"))
            .Append("</g>");
    }

    private static void WriteEdgeLabel(StringBuilder svg, C4PlacedEdge edge, C4SvgOptions options)
    {
        if (string.IsNullOrWhiteSpace(edge.Label) && edge.Order is null) return;

        svg.Append("<g class=\"c4-diagram__edge-label").Append(StepModifiers(edge, options, "c4-diagram__edge-label")).Append('"')
            .Append(StepAttribute(edge, options))
            .Append(Invariant($" transform=\"translate({edge.LabelX:0.##}, {edge.LabelY:0.##})\">"));

        // The badge sits above the label's first line, centred on it like the label,
        // so it never has to know how wide the text beneath it is drawn.
        if (options.NumberSteps && edge.Order is { } badge)
        {
            svg.Append("<g class=\"c4-diagram__step-badge\" aria-hidden=\"true\">")
                .Append("<circle cx=\"0\" cy=\"-17\" r=\"9\" />")
                .Append(Invariant($"<text text-anchor=\"middle\" x=\"0\" y=\"-13.5\">{badge}</text>"))
                .Append("</g>");
        }

        svg.Append("<text text-anchor=\"middle\">");

        if (!options.NumberSteps && edge.Order is { } order)
        {
            svg.Append(Invariant($"<tspan class=\"c4-diagram__edge-order\">{order}. </tspan>"));
        }

        svg.Append(Escape(edge.Label ?? string.Empty)).Append("</text>");

        if (!string.IsNullOrWhiteSpace(edge.Technology))
        {
            svg.Append(Invariant($"<text text-anchor=\"middle\" y=\"13\" class=\"c4-diagram__edge-tech\">[{Escape(edge.Technology)}]</text>"));
        }

        svg.Append("</g>");
    }

    /// <summary>The <c>--step</c> and <c>--current</c> modifiers a numbered arrow and its
    /// label carry when the host plays the steps; nothing otherwise.</summary>
    private static string StepModifiers(C4PlacedEdge edge, C4SvgOptions options, string block)
    {
        if (!options.NumberSteps || edge.Order is not { } order) return string.Empty;

        return order == options.CurrentStep
            ? $" {block}--step {block}--current"
            : $" {block}--step";
    }

    private static string StepAttribute(C4PlacedEdge edge, C4SvgOptions options) =>
        options.NumberSteps && edge.Order is { } order ? Invariant($" data-c4-step=\"{order}\"") : string.Empty;

    private static void WriteNode(StringBuilder svg, string id, C4PlacedNode node, C4SvgOptions options)
    {
        var person = node.Node.Kind is C4ElementKind.Person;

        // A person is drawn as a stadium and everything else as a rounded rectangle.
        // C4 has always distinguished an actor by outline before anything else, and a
        // person in the same box as a container reads as another piece of software —
        // which is exactly the confusion the notation exists to prevent.
        var radius = person ? node.Height / 2 : 8;

        var records = options.RecordChips is { } chips && chips.TryGetValue(node.Alias, out var counted) ? counted : null;

        svg.Append(Invariant($"<g class=\"{NodeClass(node, records)}\" id=\"{Escape(id)}-{Escape(node.Alias)}\">"))
            .Append(Invariant($"<rect x=\"{node.X:0.##}\" y=\"{node.Y:0.##}\" width=\"{node.Width:0.##}\" height=\"{node.Height:0.##}\" rx=\"{radius:0.##}\" />"));

        // A person gets a drawn figure rather than a character. The glyphs are fine as
        // a category mark for a box of software; an actor is the one thing on a C4
        // diagram that is not software, and a head and shoulders says so at any size
        // and in any font.
        var iconX = node.X + (person ? 26 : 14);
        var iconY = node.Y + 22;

        if (person)
        {
            svg.Append(Invariant($"<circle class=\"c4-diagram__figure\" cx=\"{iconX:0.##}\" cy=\"{iconY - 4:0.##}\" r=\"5.5\" />"))
                .Append(Invariant($"<path class=\"c4-diagram__figure\" d=\"M {iconX - 8:0.##} {iconY + 8:0.##} a 8 7 0 0 1 16 0\" />"));
        }
        else
        {
            svg.Append(Invariant($"<text x=\"{iconX:0.##}\" y=\"{iconY + 5:0.##}\" class=\"c4-diagram__glyph\">{Escape(Glyph(node.Node))}</text>"));
        }

        var textX = node.X + (person ? 44 : 36);

        svg.Append(Invariant($"<text x=\"{textX:0.##}\" y=\"{node.Y + 26:0.##}\" class=\"c4-diagram__name\">{Escape(Truncate(node.Node.Name, person ? 20 : 24))}</text>"))
            .Append(Invariant($"<text x=\"{textX:0.##}\" y=\"{node.Y + 42:0.##}\" class=\"c4-diagram__kind\">{Escape(Bracket(node))}</text>"));

        // One line when the card carries a chip, because the chip sits on the second
        // one. Two lines of description with "EXTERNAL" printed across the end of them
        // is worse than one line and a clear label.
        var lines = node.External ? 1 : 2;

        var line = 0;
        foreach (var text in Wrap(node.Node.Description, person ? 26 : 30, lines))
        {
            svg.Append(Invariant($"<text x=\"{textX:0.##}\" y=\"{node.Y + 58 + line * 13:0.##}\" class=\"c4-diagram__descr\">{Escape(text)}</text>"));
            line++;
        }

        // Said in words as well as drawn in an outline. The dashed border carries it
        // for a reader who knows the notation; the chip carries it for one who does
        // not, and it is the same thing c4hero puts on the card.
        if (node.External) WriteChip(svg, node, "EXTERNAL");

        if (options.ZoomInAliases?.Contains(node.Alias) == true) WriteZoomIn(svg, node);

        if (records is not null) WriteRecordChips(svg, node, records);

        svg.Append("</g>");
    }

    /// <summary>
    /// The overlay's record chips — <c>n ADR</c>, <c>n TDR</c> — in a row straddling the
    /// card's top edge from the left, clear of the <c>Zoom in</c> chip at the right and
    /// of the <c>EXTERNAL</c> chip at the foot, so a card may carry all three.
    /// <para>
    /// Inside the card's own group, so a press on a chip still selects the card, and
    /// each says what it counts in a <c>title</c> for a reader who hovers it.
    /// </para>
    /// </summary>
    private static void WriteRecordChips(StringBuilder svg, C4PlacedNode node, C4CardRecords records)
    {
        var x = node.X + 12;
        var y = node.Y - 7.5;

        if (records.Decisions > 0)
        {
            x = WriteRecordChip(svg, x, y, $"{records.Decisions} ADR", "adr",
                $"{records.Decisions} decision record{(records.Decisions == 1 ? string.Empty : "s")} name{(records.Decisions == 1 ? "s" : string.Empty)} this element");
        }

        if (records.OpenDebt > 0)
        {
            WriteRecordChip(svg, x, y, $"{records.OpenDebt} TDR", "tdr",
                $"{records.OpenDebt} open debt record{(records.OpenDebt == 1 ? string.Empty : "s")} name{(records.OpenDebt == 1 ? "s" : string.Empty)} this element");
        }
    }

    /// <summary>One record chip at <paramref name="x"/>; answers where the next one
    /// starts.</summary>
    private static double WriteRecordChip(StringBuilder svg, double x, double y, string label, string kind, string title)
    {
        var width = 14 + label.Length * 5.6;

        svg.Append(Invariant($"<g class=\"c4-diagram__records c4-diagram__records--{kind}\" data-c4-records=\"{kind}\">"))
            .Append(Invariant($"<title>{Escape(title)}</title>"))
            .Append(Invariant($"<rect x=\"{x:0.##}\" y=\"{y:0.##}\" width=\"{width:0.##}\" height=\"15\" rx=\"7.5\" />"))
            .Append(Invariant($"<text x=\"{x + width / 2:0.##}\" y=\"{y + 10.5:0.##}\" text-anchor=\"middle\">{Escape(label)}</text>"))
            .Append("</g>");

        return x + width + 6;
    }

    /// <summary>
    /// The <c>Zoom in</c> chip, straddling the card's top edge at the right, clear of the name.
    /// <para>
    /// A group of its own carrying <c>data-c4-zoom-in</c>, so the viewer can tell a
    /// press on the chip from a press on the card: the card selects, the chip zooms.
    /// The top edge rather than the foot, because the foot is where the
    /// <c>EXTERNAL</c> chip sits and a card may carry both.
    /// </para>
    /// </summary>
    private static void WriteZoomIn(StringBuilder svg, C4PlacedNode node)
    {
        const string label = "Zoom in ›";
        var width = 12 + label.Length * 5.4;
        var x = node.X + node.Width - width - 8;
        var y = node.Y - 7.5;

        svg.Append("<g class=\"c4-diagram__zoom-in\" data-c4-zoom-in=\"true\">")
            .Append("<title>Zoom in</title>")
            .Append(Invariant($"<rect x=\"{x:0.##}\" y=\"{y:0.##}\" width=\"{width:0.##}\" height=\"15\" rx=\"7.5\" />"))
            .Append(Invariant($"<text x=\"{x + width / 2:0.##}\" y=\"{y + 10.5:0.##}\" text-anchor=\"middle\">{Escape(label)}</text>"))
            .Append("</g>");
    }

    private static void WriteChip(StringBuilder svg, C4PlacedNode node, string label)
    {
        var width = 8 + label.Length * 5.6;
        var x = node.X + node.Width - width - 12;
        var y = node.Y + node.Height - 20;

        svg.Append(Invariant($"<rect class=\"c4-diagram__chip\" x=\"{x:0.##}\" y=\"{y:0.##}\" width=\"{width:0.##}\" height=\"13\" rx=\"6.5\" />"))
            .Append(Invariant($"<text class=\"c4-diagram__chip-text\" x=\"{x + width / 2:0.##}\" y=\"{y + 9.5:0.##}\" text-anchor=\"middle\">{Escape(label)}</text>"));
    }

    /// <summary>
    /// The classes a card carries.
    /// <para>
    /// <c>node</c> and the alias-bearing id are mermaid's contract, kept so the
    /// explorer's viewer works unchanged. The rest is this renderer's: which level, is
    /// it outside the scope, and does it lead anywhere.
    /// </para>
    /// </summary>
    private static string NodeClass(C4PlacedNode node, C4CardRecords? records)
    {
        var classes = new List<string> { "node", "c4-diagram__node", $"c4-diagram__node--{Level(node.Node.Kind)}" };

        if (node.External) classes.Add("c4-diagram__node--external");
        if (node.Drillable) classes.Add("c4-drillable");
        if (records is { OpenDebt: > 0 }) classes.Add("c4-diagram__node--debt");

        return string.Join(' ', classes);
    }

    private static string Level(C4ElementKind kind) => kind switch
    {
        C4ElementKind.Person => "person",
        C4ElementKind.SoftwareSystem or C4ElementKind.SoftwareSystemInstance => "system",
        C4ElementKind.Container or C4ElementKind.ContainerInstance => "container",
        C4ElementKind.Component => "component",
        C4ElementKind.DeploymentNode => "node",
        C4ElementKind.InfrastructureNode => "infrastructure",
        _ => "element"
    };

    /// <summary>The second line of a card: what C4 calls the element, plus its
    /// technology where it has one — <c>[Container: SQLite]</c>, the way every C4 tool
    /// writes it.</summary>
    private static string Bracket(C4PlacedNode node) =>
        string.IsNullOrWhiteSpace(node.Node.Technology)
            ? $"[{C4LayoutEngine.KindLabel(node.Node.Kind)}]"
            : $"[{C4LayoutEngine.KindLabel(node.Node.Kind)}: {Truncate(node.Node.Technology, 22)}]";

    /// <summary>A glyph per level, because shape has to carry what one hue's ramp
    /// cannot — the rule `color-scheme.md#chart-roles` states and the graph atlas's
    /// status marks already follow. Characters rather than an icon set: nothing should pull a font
    /// or a sprite sheet in for six marks.</summary>
    private static string Glyph(C4Node node) => node.Kind switch
    {
        C4ElementKind.SoftwareSystem or C4ElementKind.SoftwareSystemInstance => "▣",
        C4ElementKind.Container or C4ElementKind.ContainerInstance =>
            node.Tags.Any(tag => string.Equals(tag, "Database", StringComparison.OrdinalIgnoreCase)) ? "▤" : "▢",
        C4ElementKind.Component => "◈",
        C4ElementKind.DeploymentNode => "▦",
        C4ElementKind.InfrastructureNode => "▧",
        _ => "▢"
    };

    private static string Truncate(string? value, int length) =>
        string.IsNullOrWhiteSpace(value) ? string.Empty
        : value.Length <= length ? value
        : value[..(length - 1)].TrimEnd() + "…";

    /// <summary>
    /// A description broken into at most a couple of lines, on word boundaries.
    /// <para>
    /// SVG text does not wrap, so this is the wrapping. Two lines because a card is a
    /// label rather than a paragraph: the whole description is in the chapter the view
    /// documents, and a card that grew to fit its prose would make every card in its
    /// row grow with it.
    /// </para>
    /// </summary>
    private static IEnumerable<string> Wrap(string? value, int width, int maximum)
    {
        if (string.IsNullOrWhiteSpace(value)) yield break;

        var line = string.Empty;
        var emitted = 0;

        foreach (var word in value.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            var candidate = line.Length == 0 ? word : $"{line} {word}";

            if (candidate.Length <= width)
            {
                line = candidate;
                continue;
            }

            if (emitted == maximum - 1)
            {
                yield return Truncate($"{line} {word}", width);
                yield break;
            }

            yield return line;
            emitted++;
            line = word;
        }

        if (line.Length > 0) yield return line;
    }

    /// <summary>
    /// XML-escaped. Every string reaching the SVG came out of a <c>.dsl</c> somebody
    /// authored, and this is emitted as raw markup — so an ampersand in an element
    /// name has to stay an ampersand rather than becoming the start of an entity, and
    /// an angle bracket must never become a tag.
    /// </summary>
    private static string Escape(string? value) =>
        string.IsNullOrEmpty(value)
            ? string.Empty
            : value
                .Replace("&", "&amp;", StringComparison.Ordinal)
                .Replace("<", "&lt;", StringComparison.Ordinal)
                .Replace(">", "&gt;", StringComparison.Ordinal)
                .Replace("\"", "&quot;", StringComparison.Ordinal);

    private static string Invariant(FormattableString value) => value.ToString(CultureInfo.InvariantCulture);
}
