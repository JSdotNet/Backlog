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

    /// <summary>
    /// The same reading, refused unless the text is unmistakably a reference to a
    /// knowledge file.
    ///
    /// <para><see cref="Parse"/> is deliberately permissive because a metadata
    /// field is a list of references and nothing else — every entry in it was
    /// meant to be one. Prose is not like that: a code span in a chapter is a
    /// path as often as it is a command, a type name or a single word, and
    /// turning the wrong one into a link puts a destination on something that has
    /// none. So this asks for all three of the things a real reference has — a
    /// known knowledge folder at the front, <c>.md</c> at the end, and no
    /// whitespace in between — and hands back nothing when any is missing.</para>
    /// </summary>
    public static KnowledgeReference? ParseKnowledgePath(string? raw)
    {
        if (Parse(raw) is not { } reference) return null;
        if (reference.Folder is KnowledgeFolder.Unknown) return null;
        if (!reference.Path.EndsWith(".md", StringComparison.OrdinalIgnoreCase)) return null;

        // `.domain/naming.md and .domain/model.md` clears both tests above and is
        // two references written as one span, which is a link to neither.
        return reference.Raw.Any(char.IsWhiteSpace) ? null : reference;
    }

    /// <summary>
    /// The same reading, for text written relative to the document it appears in.
    ///
    /// <para>An overload rather than a type of its own, because it answers the
    /// question above with more context and not a different question: the outcome
    /// is a <see cref="KnowledgeReference"/>, and the three things it refuses are
    /// the three <see cref="ParseKnowledgePath(string?)"/> already refuses — a
    /// folder that is not a section, a target that is not markdown, and a span
    /// holding more than one path. A separate resolver would either restate those
    /// or exist only to call this, and the <c>#</c> splitting would have to move
    /// out of this record to reach it.</para>
    ///
    /// <para>The reason it is needed at all is that the rooted form is not the form
    /// the knowledge folders are written in. A <c>meta</c> fence spells a reference
    /// from the repository root, but a link in prose is written the way every
    /// markdown viewer resolves one — <c>../tasks/domain.md#task</c>,
    /// or a bare sibling <c>domain.md</c> — because those folders are read outside
    /// this product too. Refusing that form meant refusing almost every link that
    /// had actually been authored.</para>
    ///
    /// <para>A target that already opens with a section's folder is left alone, so
    /// nothing that resolved before resolves anywhere else now. Everything else is
    /// walked against <paramref name="fromDocument"/>'s own folder, and the result
    /// still has to land in a section: <c>assets/diagram.png</c> resolves fine and
    /// is not a chapter, <c>../../..</c> resolves to nothing because there is no
    /// outside-the-repository to point at, and both are better left as the words
    /// the author wrote than dressed as a destination.</para>
    ///
    /// <para>An anchor with no path — <c>#surface-and-border-deviation</c> — is the
    /// one form that addresses <paramref name="fromDocument"/> itself. It is a
    /// heading of the chapter already on screen, and the reader who clicks it
    /// belongs in that chapter rather than in a browser.</para>
    ///
    /// <para><paramref name="fromDocument"/> need not be in a knowledge folder. The
    /// instruction files are read in the same pane and link into <c>.design</c> and
    /// <c>.domain</c> from the repository root; where their own folder sits decides
    /// nothing about where their links point.</para>
    /// </summary>
    /// <param name="raw">The target as authored, rooted or relative.</param>
    /// <param name="fromDocument">The repository-relative path of the document the
    /// target was written in. Without it a relative target resolves to nothing —
    /// the same three characters address a different chapter from every folder in
    /// the repository, and a guess is worse than no link.</param>
    public static KnowledgeReference? ParseKnowledgePath(string? raw, string? fromDocument)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;

        var text = Unquote(raw);

        // A colon is a scheme, and a scheme is somebody else's destination. No
        // knowledge path has ever contained one, so this is a cheaper and blunter
        // rule than the renderer's allow-list and refuses the same things: an
        // `https:` link stays the anchor it is, and a `javascript:` one stays the
        // text the renderer already refuses to navigate to.
        if (text.Contains(':', StringComparison.Ordinal)) return null;

        var hash = text.IndexOf('#');
        var target = hash < 0 ? text : text[..hash];
        var slug = hash < 0 ? string.Empty : text[(hash + 1)..];

        // Nothing before the `#`: the author is naming a heading of the document
        // they are writing in, so the document is the path.
        var resolved = target.Trim().Length == 0
            ? Normalize(fromDocument)
            : Resolve(target, fromDocument);

        if (resolved is null) return null;

        return ParseKnowledgePath(slug.Trim().Length == 0 ? resolved : $"{resolved}#{slug}");
    }

    /// <summary>
    /// The repository-relative path a target names, or <see langword="null"/> when
    /// it names none.
    ///
    /// <para>Walked segment by segment rather than handed to
    /// <see cref="System.IO.Path"/>: these are repository paths and they must read
    /// the same on every platform, and <c>Path.GetFullPath</c> would answer with
    /// the machine's own root for a target that climbed past this one.</para>
    /// </summary>
    private static string? Resolve(string target, string? fromDocument)
    {
        var forward = target.Replace('\\', '/').Trim();

        // Already rooted at a section — today's reading, and the one form that
        // needs no document to make sense of it.
        if (KnowledgeFolders.FromPath(forward) is not KnowledgeFolder.Unknown) return Normalize(forward);

        // `./` is dropped first, before anything is asked about the first segment,
        // and the question below is why the order matters: `./.github/x.md` and
        // `.github/x.md` are the same target, and asking while the marker is still
        // there would let the one spelling walk into the chapter's folder that the
        // other is refused for.
        while (forward.StartsWith("./", StringComparison.Ordinal)) forward = forward[2..];

        // Rooted at the repository rather than at the document, which is the same
        // walk from an empty starting point.
        var segments = forward.StartsWith('/') || OpensWithDotFolder(forward)
            ? []
            : DirectorySegments(fromDocument);
        if (segments is null) return null;

        foreach (var segment in forward.Split('/', StringSplitOptions.RemoveEmptyEntries))
        {
            if (segment == ".") continue;

            if (segment == "..")
            {
                // Above the repository root there is nothing this product can
                // address, so a target that keeps climbing resolves to nothing
                // rather than to whatever happens to be up there.
                if (segments.Count == 0) return null;
                segments.RemoveAt(segments.Count - 1);
                continue;
            }

            segments.Add(segment);
        }

        return segments.Count == 0 ? null : string.Join('/', segments);
    }

    /// <summary>
    /// Whether the target opens with the name of a dot folder, which means it is
    /// rooted at the repository and not at the document.
    ///
    /// <para>This repository keeps almost everything that is not source in a folder
    /// whose name begins with a dot — <c>.domain</c>, <c>.arc42</c>, <c>.github</c>,
    /// <c>.claude</c>, <c>.agents</c> — and nobody writing one of those means "under
    /// the chapter I am in". They are all written from the root, which is where the
    /// convention puts them and how they already read in a code span. Five of them
    /// are sections and the check above has claimed those; the rest have to reach
    /// the same refusal, and reach it as themselves. Walked against the chapter
    /// instead, <c>.github/instructions/naming.instructions.md</c> written in a
    /// <c>.design</c> chapter became <c>.design/.github/…</c> — a path opening with
    /// a section, so a followable reference to a file that has never existed.</para>
    ///
    /// <para>Which is a different dot from the leading <c>./</c> that
    /// <see cref="KnowledgeFolders.FromPath"/> and <see cref="Normalize"/> already
    /// forgive, and the difference is whether the dot begins a <em>name</em>.
    /// <c>./</c> and <c>../</c> are the two things a relative path is made of: they
    /// name no folder, they say where to start counting from, and reading them as
    /// roots would refuse every sibling link in the repository. So the test is a dot
    /// with a name behind it, and <c>.</c> and <c>..</c> are excluded by being
    /// nothing but dots.</para>
    /// </summary>
    private static bool OpensWithDotFolder(string path)
    {
        var end = path.IndexOf('/');
        var segment = end < 0 ? path : path[..end];

        return segment.Length > 1 && segment[0] == '.' && !segment.All(character => character == '.');
    }

    /// <summary>The folders a document sits in, innermost last, or
    /// <see langword="null"/> when no document was given — which is the answer that
    /// makes a relative target unresolvable rather than root-relative.</summary>
    private static List<string>? DirectorySegments(string? documentPath)
    {
        if (Normalize(documentPath) is not { } normalized) return null;

        var segments = normalized.Split('/', StringSplitOptions.RemoveEmptyEntries).ToList();

        // The document's own file name is not a folder to resolve against.
        if (segments.Count > 0) segments.RemoveAt(segments.Count - 1);

        return segments;
    }

    /// <summary>One spelling for a repository path: forward slashes, and none of
    /// the leading <c>./</c> or <c>/</c> a hand-authored path picks up. The same
    /// leniency <see cref="KnowledgeFolders.FromPath"/> already grants, because a
    /// host carrying one of those is naming the same document and should not lose
    /// every link in the file for it.</summary>
    private static string? Normalize(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return null;

        var forward = path.Replace('\\', '/').Trim();
        while (forward.StartsWith("./", StringComparison.Ordinal)) forward = forward[2..];
        forward = forward.TrimStart('/');

        return forward.Length == 0 ? null : forward;
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
