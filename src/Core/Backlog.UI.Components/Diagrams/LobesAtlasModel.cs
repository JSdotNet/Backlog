namespace Backlog.UI.Components.Diagrams;

/// <summary>
/// What the lobes drawing of the atlas renderer (<c>backlogLobesAtlas.render</c>
/// in <c>components.js</c>) takes: scopes drawn as brains, each mark sitting in
/// one region of its scope's brain.
///
/// <para>Handed to <see cref="GraphAtlas"/> as its <c>Data</c>, with
/// <c>JsFunction</c> naming the lobes renderer, and serialized by the JS interop
/// in camel case — the property names are the renderer's contract. Every word is
/// the host's; the renderer knows the seven regions and three scope kinds by
/// their keys and nothing about what a Brain or a devbook is.</para>
///
/// <para>Nothing here is a colour or a position. The renderer reads colours off
/// the design tokens and derives every position from a mark's id, so the same
/// model draws the same picture twice.</para>
/// </summary>
/// <param name="Layout"><see cref="AllLayout"/> for every scope at once, or the
/// key of the one scope drawn at full size.</param>
/// <param name="Scopes">The scopes, in the order the host lists them. In
/// <see cref="AllLayout"/> the first <see cref="LobesAtlasScope.Brain"/> scope is
/// centred and the rest are seated around it.</param>
/// <param name="Marks">Every mark of every scope. Marks whose scope is not drawn
/// in <paramref name="Layout"/> are skipped by the renderer, so a host can hand
/// over one model and change only the layout.</param>
/// <param name="Links">The edges between marks.</param>
/// <param name="HiddenScopes">Scope keys not drawn in <see cref="AllLayout"/>.</param>
/// <param name="HiddenLobes">Regions not drawn while their scope is the
/// <paramref name="Layout"/>; ignored in <see cref="AllLayout"/>, where a region
/// toggle does not apply.</param>
/// <param name="ShowLabels">Whether the region, hub and scope names are written
/// on the picture.</param>
/// <param name="EmptyMessage">What the canvas says when nothing is left to draw.</param>
public sealed record LobesAtlasModel(
    string Layout,
    IReadOnlyList<LobesAtlasScope> Scopes,
    IReadOnlyList<LobesAtlasMark> Marks,
    IReadOnlyList<LobesAtlasLink> Links,
    IReadOnlyList<string> HiddenScopes,
    IReadOnlyList<LobesAtlasRegionKey> HiddenLobes,
    bool ShowLabels = true,
    string EmptyMessage = "Nothing to draw.")
{
    /// <summary>The layout that draws every scope at once.</summary>
    public const string AllLayout = "all";

    /// <summary>The renderer's function, for <see cref="GraphAtlas.JsFunction"/>.</summary>
    public const string JsFunction = "backlogLobesAtlas.render";
}

/// <summary>One brain of the atlas.</summary>
/// <param name="Key">Unique among the scopes; what marks name as their scope.</param>
/// <param name="Label">The scope's name, written above its brain in the All layout.</param>
/// <param name="Kind"><see cref="Brain"/>, <see cref="Devbook"/> or
/// <see cref="Ghost"/> — the brain drawn in the accent, a gold one, or a dashed
/// outline.</param>
/// <param name="Count">The number written beside its name; null writes none.</param>
/// <param name="Regions">Each region's name and count, written over the region in
/// the scope's own layout. A region left out is not named.</param>
/// <param name="SatelliteLabel">In this scope's own layout, the words beside the
/// Brain hub drawn next to it, whose bundles reach this scope's marks. Null draws
/// no satellite.</param>
/// <param name="Status">Written beside its name in the All layout where it has no
/// <paramref name="Count"/> — a ghost's "not registered". Null writes none.</param>
public sealed record LobesAtlasScope(
    string Key,
    string Label,
    string Kind,
    int? Count = null,
    IReadOnlyList<LobesAtlasRegion>? Regions = null,
    string? SatelliteLabel = null,
    string? Status = null)
{
    public const string Brain = "brain";
    public const string Devbook = "devbook";
    public const string Ghost = "ghost";
}

/// <summary>A region's name and count, as written over it.</summary>
/// <param name="Lobe">One of the <see cref="LobesAtlasMark"/> lobe keys.</param>
public sealed record LobesAtlasRegion(string Lobe, string Label, int Count);

/// <summary>A region of one scope, as a <see cref="LobesAtlasModel.HiddenLobes"/>
/// entry names it.</summary>
public sealed record LobesAtlasRegionKey(string Scope, string Lobe);

/// <summary>One mark: a note, a chapter or a hub, in one region of one scope.</summary>
/// <param name="Id">Unique across every scope, and what a pick reports.</param>
/// <param name="Label">Its name, written beside a hub.</param>
/// <param name="Scope">The <see cref="LobesAtlasScope.Key"/> it belongs to.</param>
/// <param name="Lobe"><see cref="Frontal"/>, <see cref="Parietal"/>,
/// <see cref="Temporal"/>, <see cref="Occipital"/>, <see cref="Cerebellum"/>,
/// <see cref="Stem"/>, or <see cref="Dust"/> for the faint outline.</param>
/// <param name="Kind"><see cref="Item"/>, <see cref="Hub"/> or
/// <see cref="GhostItem"/>: a dot, a ringed hub, or a hollow dashed ring.</param>
/// <param name="Dashed">A hub drawn with a dashed outer ring — one whose links
/// cannot be followed here.</param>
public sealed record LobesAtlasMark(
    string Id,
    string Label,
    string Scope,
    string Lobe,
    string Kind = LobesAtlasMark.Item,
    bool Dashed = false)
{
    public const string Frontal = "frontal";
    public const string Parietal = "parietal";
    public const string Temporal = "temporal";
    public const string Occipital = "occipital";
    public const string Cerebellum = "cerebellum";
    public const string Stem = "stem";
    public const string Dust = "dust";

    public const string Item = "item";
    public const string Hub = "hub";
    public const string GhostItem = "ghost";
}

/// <summary>An edge between two marks.</summary>
/// <param name="Kind"><see cref="Own"/> inside one scope, or <see cref="Bundle"/>
/// from a hub into another scope — teal into gold, or dashed when
/// <paramref name="Dashed"/>.</param>
public sealed record LobesAtlasLink(string Source, string Target, string Kind = LobesAtlasLink.Own, bool Dashed = false)
{
    public const string Own = "own";
    public const string Bundle = "bundle";
}
