namespace Backlog.UI.Components.Badges;

/// <summary>
/// The vocabulary <see cref="CaptureKindMarker"/> can draw, published so a
/// consumer can decide its own fallback without keeping a second copy of the
/// list.
///
/// <para>Ten content kinds, spelled as slugs, because the library depends on no
/// module and a module's enum is out of reach here. A consumer maps its own
/// enum onto these strings; the two have to agree, and the tests on each side
/// name the same values so a change to one shows up as a failure on the
/// other.</para>
///
/// <para>Recognition is what the caller's fallback hangs off. A value set that
/// grows must never make a page look broken, so an unrecognised value draws no
/// glyph at all and the caller goes on showing the plain word — the same rule
/// <c>KnowledgeTypeMarker</c> keeps, for the same reason.</para>
/// </summary>
public static class CaptureKinds
{
    /// <summary>Every kind the marker draws, in the order a reader meets them:
    /// the plain note first, then the things a link can turn out to be, then the
    /// things that arrive as files or from a machine.</summary>
    public static IReadOnlyList<string> All { get; } =
    [
        "text",
        "article",
        "link",
        "youtube",
        "image",
        "document",
        "email",
        "code",
        "voice",
        "claude-artifact"
    ];

    private static readonly HashSet<string> Known = new(All, StringComparer.OrdinalIgnoreCase);

    /// <summary>Whether the marker has a glyph for this value. Nothing in,
    /// false — a missing kind is not a kind nobody drew.</summary>
    public static bool IsRecognised(string? value) =>
        !string.IsNullOrWhiteSpace(value) && Known.Contains(value.Trim());

    /// <summary>The value as the lookup and the class modifier spell it.</summary>
    public static string Normalise(string value) => value.Trim().ToLowerInvariant();

    /// <summary>The kind as a reader reads it. The library keeps a label beside
    /// each slug so a caller that has nothing else to print — a storybook, a
    /// tooltip — never has to invent one.</summary>
    public static string Label(string value) => Normalise(value) switch
    {
        "text" => "Text",
        "article" => "Article",
        "link" => "Link",
        "youtube" => "YouTube",
        "image" => "Image",
        "document" => "Document",
        "email" => "Email",
        "code" => "Code",
        "voice" => "Voice memo",
        "claude-artifact" => "Claude artifact",
        _ => value.Trim()
    };
}
