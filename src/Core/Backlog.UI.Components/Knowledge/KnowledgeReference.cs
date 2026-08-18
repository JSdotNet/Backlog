namespace Backlog.UI.Components.Knowledge;

/// <summary>
/// One entry of a <c>related</c>, <c>depends-on</c>, or <c>implements</c> field:
/// <c>&lt;path&gt;#&lt;heading-slug&gt;</c> for a chapter, or a bare
/// <c>&lt;path&gt;</c> for a whole file.
///
/// <para>Chapters have no stored id — the path plus the heading's anchor slug
/// <em>is</em> the address, which is why <c>#</c> is a separator here and never a
/// comment marker. A parser that treated it as one would truncate every chapter
/// reference in the repository to its file.</para>
/// </summary>
public sealed record KnowledgeReference
{
    private KnowledgeReference(string raw, string path, string? slug)
    {
        Raw = raw;
        Path = path;
        Slug = slug;
        Folder = KnowledgeFolders.FromPath(path);
        FileName = NameOf(path);
    }

    /// <summary>The reference as authored, with surrounding quotes stripped and
    /// trimmed. Kept whole because it is what a reader recognises and what a
    /// <c>title</c> should show when the visible label is shortened.</summary>
    public string Raw { get; }

    /// <summary>The repository-relative path, without the heading slug.</summary>
    public string Path { get; }

    /// <summary>The heading slug, or <see langword="null"/> when the reference
    /// addresses the file as a whole.</summary>
    public string? Slug { get; }

    /// <summary>The knowledge folder the target lives in, read from
    /// <see cref="Path"/>.</summary>
    public KnowledgeFolder Folder { get; }

    /// <summary>The file name at the end of <see cref="Path"/>.</summary>
    public string FileName { get; }

    /// <summary>Whether this addresses a chapter rather than the whole file.</summary>
    public bool IsChapter => Slug is not null;

    /// <summary>Whether this addresses a file rather than one of its chapters.</summary>
    public bool IsFile => Slug is null;

    /// <summary>
    /// Short display text: the file's name without its extension, or — for a
    /// chapter — the slug read back as words. A metadata strip that printed the
    /// full path of every reference would be mostly punctuation; the full form
    /// stays on <see cref="Raw"/> for the tooltip and the link target.
    /// </summary>
    public string Label => Slug is null
        ? StripExtension(FileName)
        : Slug.Replace('-', ' ');

    /// <summary>
    /// Reads a reference, or returns <see langword="null"/> when there is nothing
    /// addressable there. Splits on the <em>first</em> <c>#</c>: everything after
    /// it belongs to the slug, so a heading whose own slug contains a hyphen or a
    /// second hash still resolves to one chapter.
    /// </summary>
    public static KnowledgeReference? Parse(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;

        var text = Unquote(raw);
        if (text.Length == 0) return null;

        var hash = text.IndexOf('#');
        if (hash < 0) return new KnowledgeReference(text, text, null);

        var path = text[..hash].Trim();
        if (path.Length == 0) return null;

        // A trailing `#` with nothing after it addresses no chapter. Keeping it
        // as an empty slug would make the reference read as a chapter that does
        // not exist rather than as the file it plainly points at.
        var slug = text[(hash + 1)..].Trim();
        return new KnowledgeReference(text, path, slug.Length == 0 ? null : slug);
    }

    /// <summary>The <c>Try</c> shape of <see cref="Parse"/>, for callers that
    /// keep unparseable entries rather than dropping them.</summary>
    public static bool TryParse(string? raw, out KnowledgeReference? reference)
    {
        reference = Parse(raw);
        return reference is not null;
    }

    /// <summary>Quotes are how a YAML inline list keeps a path with a comma or a
    /// colon in one piece; they are never part of the reference itself.</summary>
    private static string Unquote(string value)
    {
        var text = value.Trim();
        if (text.Length >= 2
            && ((text[0] == '"' && text[^1] == '"') || (text[0] == '\'' && text[^1] == '\'')))
        {
            text = text[1..^1].Trim();
        }

        return text;
    }

    /// <summary>Split by hand rather than through <see cref="System.IO.Path"/>:
    /// these paths are always <c>/</c>-separated repository paths, and they must
    /// read the same on every platform.</summary>
    private static string NameOf(string path)
    {
        var separator = path.LastIndexOfAny(['/', '\\']);
        return separator < 0 ? path : path[(separator + 1)..];
    }

    private static string StripExtension(string fileName)
    {
        var dot = fileName.LastIndexOf('.');
        return dot <= 0 ? fileName : fileName[..dot];
    }
}
