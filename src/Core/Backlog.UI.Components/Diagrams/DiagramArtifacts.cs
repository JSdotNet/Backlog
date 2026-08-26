using System.Security.Cryptography;
using System.Text;

namespace Backlog.UI.Components.Diagrams;

/// <summary>
/// The identity of a diagram, for the purpose of finding a picture somebody
/// authored from it.
/// <para>
/// A hash of the source rather than a name, a path or an ordinal, because a
/// mermaid fence has none of those: <see cref="DiagramView"/> is handed a source
/// and a language and nothing else, at all five places that render one. Threading
/// a chapter path and a diagram number through every one of them would be the
/// obvious alternative, and it would buy a weaker answer — an ordinal shifts when
/// somebody inserts a diagram above it, and a renumbered diagram must not start
/// showing its neighbour's picture.
/// </para>
/// <para>
/// Hashing the source also settles drift for free, which is the failure this
/// whole feature has to avoid. An Archify artifact is a re-authoring of a
/// diagram, not a rendering of one: nothing in it points back at the mermaid it
/// came from, so an edited fence would otherwise keep showing the old picture
/// with complete confidence. Here the edit changes the hash, the lookup misses,
/// and the reader gets the mermaid — the true answer — with an offer to
/// regenerate.
/// </para>
/// </summary>
public static class DiagramSourceHash
{
    /// <summary>
    /// The text that is hashed: LF line endings, no trailing whitespace on any
    /// line, and no blank lines at either end.
    /// <para>
    /// Every one of those varies without the diagram changing. The generator
    /// reads a fence out of a file that may be checked out with CRLF; this side
    /// receives whatever the markdown parser kept, which may have dropped the
    /// final newline; and an editor may or may not trim. Hashing the raw bytes
    /// would make artifacts stop matching for reasons nobody edited, which reads
    /// to a user as the feature being broken.
    /// </para>
    /// <para>
    /// <c>normalizeDiagramSource</c> in
    /// <c>tools/diagrams/archify-artifacts.mjs</c> is the same rule on the
    /// generating side. The two must agree exactly: if they drift, every lookup
    /// misses and the app quietly shows mermaid everywhere, which is the one
    /// failure mode this design cannot detect from the inside.
    /// </para>
    /// </summary>
    public static string Normalize(string? source)
    {
        if (string.IsNullOrEmpty(source)) return string.Empty;

        var lines = source.Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .Split('\n')
            .Select(line => line.TrimEnd(' ', '\t'))
            .ToList();

        while (lines.Count > 0 && lines[0].Length == 0) lines.RemoveAt(0);
        while (lines.Count > 0 && lines[^1].Length == 0) lines.RemoveAt(lines.Count - 1);

        return string.Join('\n', lines);
    }

    /// <summary>The lowercase hex SHA-256 of <see cref="Normalize"/>'s output —
    /// the key an <c>_archify/index.json</c> entry is filed under.</summary>
    public static string Of(string? source) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(Normalize(source))));
}

/// <summary>
/// Which Archify diagram type re-authors which kind of mermaid diagram, and which
/// kinds have none.
/// <para>
/// It decides one visible thing: whether the app offers to generate an artifact
/// for a diagram it has none for. Offering where nothing can be generated is the
/// worse failure of the two, because the offer is a promise — so a kind this
/// table does not know is treated as unsupported rather than guessed at.
/// </para>
/// </summary>
public static class ArchifyDiagramTypes
{
    /// <summary>The five types Archify has.</summary>
    public static IReadOnlyList<string> All { get; } = ["architecture", "workflow", "sequence", "dataflow", "lifecycle"];

    /// <summary>
    /// The mermaid keyword to Archify type mapping, as
    /// <c>tools/diagrams/archify-artifacts.mjs</c> declares it.
    /// <para>
    /// <c>classDiagram</c> is present with no type rather than absent, because
    /// "no Archify type fits this" is an answer worth writing down. Twelve of
    /// this repository's fifty-one mermaid blocks are class diagrams and every
    /// one is a bounded context's aggregate model; none of Archify's five types
    /// can say "aggregate root", "value object" or "0..*", so there is nothing to
    /// generate and the app must not offer to.
    /// </para>
    /// </summary>
    private static readonly Dictionary<string, string?> ByMermaidKind = new(StringComparer.OrdinalIgnoreCase)
    {
        ["flowchart"] = "workflow",
        ["graph"] = "workflow",
        ["sequenceDiagram"] = "sequence",
        ["stateDiagram-v2"] = "lifecycle",
        ["stateDiagram"] = "lifecycle",
        ["C4Context"] = "architecture",
        ["C4Container"] = "architecture",
        ["C4Component"] = "architecture",
        ["classDiagram"] = null,
        ["erDiagram"] = null,
        ["gantt"] = null,
        ["pie"] = null,
        ["journey"] = null,
        ["mindmap"] = null,
        ["timeline"] = null,
        ["quadrantChart"] = null,
        ["requirementDiagram"] = null,
        ["gitGraph"] = null,
        ["block"] = null,
        ["block-beta"] = null,
        ["architecture"] = null,
        ["architecture-beta"] = null
    };

    /// <summary>
    /// The mermaid keyword a source opens with, or null when it opens with
    /// nothing recognisable.
    /// <para>
    /// Comments and <c>%%{init}%%</c> directives are skipped, because a fence is
    /// allowed to begin with either and several in this repository do.
    /// </para>
    /// </summary>
    public static string? MermaidKind(string? source)
    {
        foreach (var line in DiagramSourceHash.Normalize(source).Split('\n'))
        {
            var trimmed = line.Trim();
            if (trimmed.Length == 0 || trimmed.StartsWith("%%", StringComparison.Ordinal)) continue;

            var length = 0;
            while (length < trimmed.Length && (char.IsAsciiLetterOrDigit(trimmed[length]) || trimmed[length] is '_' or '-')) length++;
            return length == 0 ? null : trimmed[..length];
        }

        return null;
    }

    /// <summary>The type an artifact for this source would be authored as, or null
    /// when no type fits. A flowchart that describes a state machine is really a
    /// <c>lifecycle</c> and an author may say so in the specification's filename;
    /// this is only the default, and only used to decide whether to offer.</summary>
    public static string? For(string? source)
    {
        var kind = MermaidKind(source);
        return kind is not null && ByMermaidKind.TryGetValue(kind, out var type) ? type : null;
    }

    /// <summary>Whether generating an artifact for this source is possible at all.
    /// The question the Generate affordance is gated on.</summary>
    public static bool IsSupported(string? source) => For(source) is not null;
}

/// <summary>What the app knows about the Archify artifact for one diagram.</summary>
/// <param name="Html">The whole self-contained document, or null when there is
/// none to show. Not a URL: the artifact sits in whichever repository clone is
/// configured, not in the app's own assets, so it reaches the browser as
/// content.</param>
/// <param name="ArtifactPath">Where it came from, for a person who wants to open
/// or regenerate it.</param>
/// <param name="SpecPath">The specification it was rendered from, when one
/// exists. Present without <see cref="Html"/> is the case the render action
/// exists for: somebody authored the specification and nobody has run the
/// generator.</param>
/// <param name="ArchifyType">The type it was authored as, or would be.</param>
/// <param name="IsOutOfDate">An artifact was authored for this diagram and the
/// fence has changed since. Said out loud rather than shown: the whole point of
/// hashing the source is that a stale picture never renders, and the reader is
/// told why they are looking at mermaid instead.</param>
/// <param name="Quality">The Archify quality profile the artifact is rendered
/// at: <c>showcase</c> for everything the repository can draw without a forced
/// edge crossing, and <c>standard</c> for the recorded exceptions, which a
/// specification opts into by naming it in its filename. Null only when no
/// Archify type fits the diagram at all, since a diagram nothing can be authored
/// for has no profile to be authored under. Carried so the view could say so
/// later; nothing draws it yet.</param>
public sealed record DiagramArtifact(
    string? Html,
    string? ArtifactPath,
    string? SpecPath,
    string? ArchifyType,
    bool IsOutOfDate,
    string? Quality = null)
{
    public bool CanRender => !string.IsNullOrEmpty(Html);
}

/// <summary>
/// Whether a diagram has an Archify artifact, and the two things a reader can do
/// about it when it has none.
/// <para>
/// Implemented by the host, because every part of the answer is the host's: the
/// feature flag, which repository clone the chapters came from, and whether an
/// agent CLI is installed. This library only asks. Resolved optionally, so a
/// storybook page or a unit test that renders a <see cref="DiagramView"/> without
/// registering anything gets today's behaviour rather than an injection error.
/// </para>
/// </summary>
public interface IDiagramArtifactSource
{
    /// <summary>What exists for this diagram. Returns null — not an empty
    /// artifact — when the feature is off, so that "switched off" and "nothing
    /// authored" cannot be confused by a caller.</summary>
    DiagramArtifact? Find(string? source, string? language);

    /// <summary>Renders an already-authored specification by running the pinned
    /// generator. Deterministic and agentless: this is the half of "generate"
    /// that is genuinely mechanical. Returns an error message rather than
    /// throwing.</summary>
    Task<string?> RenderAsync(string? source, CancellationToken cancellationToken = default);

    /// <summary>Opens an agent session to author the specification, with a brief
    /// describing the diagram. The other half of "generate", and the half no
    /// running app can do by itself — there is no mermaid-to-Archify converter,
    /// and Archify's own instructions describe an agent reading the mermaid for
    /// meaning and writing fresh JSON. Returns an error message rather than
    /// throwing.</summary>
    Task<string?> AuthorAsync(string? source, CancellationToken cancellationToken = default);

    /// <summary>Whether <see cref="AuthorAsync"/> can do anything on this
    /// machine. False hides the offer rather than letting somebody press a button
    /// that reports a missing CLI.</summary>
    bool CanAuthor { get; }
}
