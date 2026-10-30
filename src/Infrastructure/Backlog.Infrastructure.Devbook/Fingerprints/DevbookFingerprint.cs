using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

using Backlog.Modules.Devbook.Abstractions;

namespace Backlog.Infrastructure.Devbook.Fingerprints;

/// <summary>One file of a change's <c>devbook-delta/</c>, by the devbook file it
/// targets — its path under <c>devbook-delta/</c> re-rooted at <c>.devbook/</c> —
/// and its text.</summary>
public sealed record DevbookChangeDelta(string Target, string Markdown);

/// <summary>
/// <c>devbook.chapter.fingerprint@1</c>: the content fingerprint of a devbook
/// block and of a change proposal, the value a proposal's approval and acceptance
/// are recorded at under <c>devbook.decision@1</c>.
/// <para>
/// A port of devbook's <c>chapterFingerprint</c> and <c>changeHash</c>
/// (<c>tools/devbook-meta/metadata.mjs</c>), held to the contract's shared vector
/// by <c>ChapterFingerprintVectorTests</c>. It is a port rather than a reuse of the
/// readers elsewhere in this assembly on purpose: the value has to agree byte for
/// byte with devbook's and spec-manager's, so heading, fence and slug are read
/// exactly as <c>metadata.mjs</c> reads them — including its JavaScript regex
/// semantics, spelled out where .NET's differ — and not as the panels read a
/// chapter. Nothing here is part of the devbook database or its builder.
/// </para>
/// <para>
/// What is hashed is what a reader would say they read. <c>meta</c> and
/// <c>annotation</c> fences are dropped — the decision is recorded beside the
/// content, and a review note is not content — and whitespace is collapsed, so a
/// reflowed paragraph keeps its approval. Heading text is in and its <c>#</c>
/// markers out, so renaming a chapter lapses an approval and promoting one does
/// not. Each demo a block belongs to is folded in, without its template-managed
/// region, so editing a demo moves the value and a template refresh does not.
/// </para>
/// </summary>
public static partial class DevbookFingerprint
{
    private const string ChangesRoot = "openspec/changes/";
    private const string DeltaFolder = "devbook-delta";
    private const string DevbookPrefix = ".devbook/";

    /// <summary>The value of a canonical text: <c>sha256:</c> and the first eight
    /// lowercase hex characters of SHA-256 over its UTF-8.</summary>
    public static string Of(string canonicalText)
    {
        ArgumentNullException.ThrowIfNull(canonicalText);

        return "sha256:" + Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(canonicalText)))[..8];
    }

    /// <summary>The fingerprint of one block: <see cref="BlockText"/>'s value, or
    /// null when <paramref name="slug"/> names no chapter in the file.</summary>
    public static string? Block(string path, string markdown, string? slug = null, Func<string, string?>? demoText = null) =>
        BlockText(path, markdown, slug, demoText) is { } text ? Of(text) : null;

    /// <summary>
    /// The canonical text of one block of a file, with every demo it belongs to
    /// folded in.
    /// </summary>
    /// <param name="path">The file's repository path, which places its page demo
    /// (<c>&lt;page&gt;.demo.html</c>, or <c>demo.html</c> for <c>context.md</c>).</param>
    /// <param name="markdown">The file's text.</param>
    /// <param name="slug">The chapter's anchor; null for the file block, the
    /// file's first <c>#</c> heading.</param>
    /// <param name="demoText">A demo's text by repository path, or null when it
    /// does not exist. Null folds in no demo at all.</param>
    /// <returns>The text, or null when <paramref name="slug"/> names no
    /// chapter.</returns>
    public static string? BlockText(string path, string markdown, string? slug = null, Func<string, string?>? demoText = null)
    {
        ArgumentNullException.ThrowIfNull(path);
        ArgumentNullException.ThrowIfNull(markdown);

        var chapters = Parse(markdown);
        int line;

        if (slug is null)
        {
            line = chapters.FirstOrDefault(chapter => chapter.Level == 1)?.Line ?? 1;
        }
        else
        {
            var wanted = Slugify(slug);
            if (chapters.FirstOrDefault(chapter => chapter.Slug == wanted) is not { } chapter) return null;
            line = chapter.Line;
        }

        return FingerprintText(path, markdown, line, demoText);
    }

    /// <summary>The fingerprint of a whole change: <see cref="ChangeText"/>'s
    /// value.</summary>
    public static string Change(string proposalMarkdown, IEnumerable<DevbookChangeDelta> deltas) =>
        Of(ChangeText(proposalMarkdown, deltas));

    /// <summary>
    /// The canonical text of a change, decided as one: the value of its
    /// <c>proposal.md</c>'s file block, then each delta by target in code-unit
    /// order. A Markdown delta keeps its <c>meta</c> fences and headings — in a
    /// delta they are content — and drops only its <c>annotation</c> fences; a demo
    /// delta is read as a demo is.
    /// </summary>
    public static string ChangeText(string proposalMarkdown, IEnumerable<DevbookChangeDelta> deltas)
    {
        ArgumentNullException.ThrowIfNull(proposalMarkdown);
        ArgumentNullException.ThrowIfNull(deltas);

        var fileLine = Parse(proposalMarkdown).FirstOrDefault(chapter => chapter.Level == 1)?.Line ?? 1;
        var parts = new List<string> { Of(ChapterText(proposalMarkdown, fileLine)) };

        foreach (var delta in deltas.OrderBy(delta => delta.Target, StringComparer.Ordinal))
        {
            if (IsDemoPath(delta.Target))
            {
                parts.Add($"{delta.Target}\n{DemoText(delta.Markdown)}");
                continue;
            }

            var lines = Lines(delta.Markdown);
            parts.Add($"{delta.Target}\n{HashedText(lines, 0, lines.Length, dropMeta: false)}");
        }

        return string.Join('\n', parts);
    }

    /// <summary>The devbook file a path under a change's <c>devbook-delta/</c>
    /// targets — <c>openspec/changes/&lt;name&gt;/devbook-delta/a/b.md</c> is
    /// <c>.devbook/a/b.md</c> — or null for any other path.</summary>
    public static string? DeltaTarget(string changePath)
    {
        ArgumentNullException.ThrowIfNull(changePath);

        var normalized = changePath.Replace('\\', '/');
        if (!normalized.StartsWith(ChangesRoot, StringComparison.Ordinal)) return null;

        var segments = normalized[ChangesRoot.Length..].Split('/');
        if (segments.Length < 3 || segments[0].Length == 0 || segments[0] == "archive" || segments[1] != DeltaFolder) return null;

        return DevbookPrefix + string.Join('/', segments[2..]);
    }

    /// <summary>
    /// The fingerprint of the change folder <c>openspec/changes/&lt;name&gt;/</c> in
    /// a repository on disk: its <c>proposal.md</c> and every <c>*.md</c> and demo
    /// under <c>devbook-delta/</c>, at any depth. Null when the folder has no
    /// proposal — there is no change to decide.
    /// </summary>
    /// <exception cref="ArgumentException"><paramref name="changeName"/> is no
    /// folder name under <c>openspec/changes/</c>
    /// (<see cref="DevbookSubject.IsProposalName"/>): nothing outside the change's
    /// own folder is read.</exception>
    public static string? ReadChange(string repositoryRoot, string changeName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repositoryRoot);
        ArgumentException.ThrowIfNullOrWhiteSpace(changeName);

        if (!DevbookSubject.IsProposalName(changeName))
        {
            throw new ArgumentException(
                "A change is read by its folder name under openspec/changes/: no path, no '..', not 'archive'.",
                nameof(changeName));
        }

        var folder = Path.Combine([repositoryRoot, .. ChangesRoot.TrimEnd('/').Split('/'), changeName]);
        var proposal = Path.Combine(folder, "proposal.md");
        if (!File.Exists(proposal)) return null;

        var deltaRoot = Path.Combine(folder, DeltaFolder);
        var deltas = new List<DevbookChangeDelta>();

        if (Directory.Exists(deltaRoot))
        {
            foreach (var file in Directory.EnumerateFiles(deltaRoot, "*", SearchOption.AllDirectories))
            {
                var relative = Path.GetRelativePath(deltaRoot, file).Replace('\\', '/');
                if (!relative.EndsWith(".md", StringComparison.Ordinal) && !IsDemoPath(relative)) continue;

                deltas.Add(new DevbookChangeDelta(DevbookPrefix + relative, ReadAsDevbookReads(file)));
            }
        }

        return Change(ReadAsDevbookReads(proposal), deltas);
    }

    /// <summary>
    /// A file's text as devbook's <c>readFile(path, "utf8")</c> gives it: UTF-8,
    /// with a byte order mark kept as U+FEFF.
    /// <para>
    /// <see cref="File.ReadAllText(string)"/> strips the mark, and that is not a
    /// neutral difference: the first line then starts with <c>#</c> where devbook's
    /// starts with U+FEFF, so this side reads a heading devbook does not, hashes
    /// other text, and an approval recorded at <c>chapter-hash.mjs</c>' value reads
    /// as lapsed here.
    /// </para>
    /// </summary>
    private static string ReadAsDevbookReads(string path) => Encoding.UTF8.GetString(File.ReadAllBytes(path));

    /// <summary>The block's text, or, when it folds in a demo, its value then each
    /// demo's path and text. <c>chapterFingerprintText</c> in
    /// <c>metadata.mjs</c>.</summary>
    private static string FingerprintText(string path, string markdown, int line, Func<string, string?>? demoText)
    {
        if (demoText is null) return ChapterText(markdown, line);

        var chapters = Parse(markdown);
        var index = chapters.FindIndex(chapter => chapter.Line == line);
        var level = index == -1 ? 1 : chapters[index].Level;
        var paths = new HashSet<string>(StringComparer.Ordinal);

        if (level == 1 && PageDemoPath(path) is { } own) paths.Add(own);

        for (var i = Math.Max(index, 0); i < chapters.Count; i++)
        {
            if (i > index && index != -1 && chapters[i].Level <= level) break;

            foreach (var reference in chapters[i].Demos)
            {
                var hash = reference.IndexOf('#', StringComparison.Ordinal);
                var target = hash == -1 ? reference : reference[..hash];
                if (IsDemoPath(target)) paths.Add(target);
            }
        }

        var demos = new List<string>();
        foreach (var demoPath in paths.Order(StringComparer.Ordinal))
        {
            if (demoText(demoPath) is { } text) demos.Add($"{demoPath}\n{DemoText(text)}");
        }

        return demos.Count == 0
            ? ChapterText(markdown, line)
            : string.Join('\n', [Of(ChapterText(markdown, line)), .. demos]);
    }

    /// <summary>The block whose heading is at <paramref name="line"/> (1-based):
    /// its heading text as written, then its kept lines. <c>chapterText</c> in
    /// <c>metadata.mjs</c>.</summary>
    private static string ChapterText(string markdown, int line)
    {
        var lines = Lines(markdown);
        var chapters = Parse(markdown);

        var index = chapters.FindIndex(chapter => chapter.Line == line);
        var start = index == -1 ? Math.Max(0, line - 1) : chapters[index].Line - 1;
        var level = index == -1 ? 1 : chapters[index].Level;

        var end = lines.Length;
        if (index != -1 && chapters.Skip(index + 1).FirstOrDefault(chapter => chapter.Level <= level) is { } next)
        {
            end = next.Line - 1;
        }

        var heading = start < lines.Length ? HeadingText().Match(lines[start]) : Match.Empty;

        return string.Join(
            '\n',
            new[] { heading.Success ? heading.Groups[1].Value : string.Empty, HashedText(lines, start + 1, end, dropMeta: true) }
                .Where(part => part.Length > 0));
    }

    /// <summary>
    /// Lines <paramref name="from"/> to <paramref name="end"/> minus the dropped
    /// fences, each line's whitespace runs collapsed to one space and trimmed, the
    /// empty ones left out. <c>hashedText</c> in <c>metadata.mjs</c>, and like it,
    /// a fence's closer is looked for to the end of the file, not of the span; a
    /// kept fence is cut at the span's end.
    /// </summary>
    private static string HashedText(string[] lines, int from, int end, bool dropMeta)
    {
        var kept = new List<string>();

        for (var i = from; i < end; i++)
        {
            var fence = FenceOpener().Match(lines[i]);
            if (!fence.Success)
            {
                kept.Add(lines[i]);
                continue;
            }

            var marker = fence.Groups[1].Value;
            var label = fence.Groups[2].Value.ToLowerInvariant();

            var k = i + 1;
            while (k < lines.Length && !IsCloser(lines[k], marker[0], marker.Length)) k++;

            if (label == "annotation" || (dropMeta && label == "meta"))
            {
                i = k;
                continue;
            }

            for (var j = i; j <= k && j < end; j++) kept.Add(lines[j]);
            i = k;
        }

        return string.Join(
            '\n',
            kept.Select(entry => Trim(JsWhitespace().Replace(entry, " "))).Where(entry => entry.Length > 0));
    }

    /// <summary>A demo's text as a fingerprint reads it: CRLF and CR turned to LF,
    /// and every template-managed region — markers included — removed.</summary>
    private static string DemoText(string text) =>
        TemplateRegion().Replace(text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n'), string.Empty);

    /// <summary>The demo named for a page: <c>demo.html</c> beside
    /// <c>context.md</c>, <c>&lt;page&gt;.demo.html</c> beside
    /// <c>&lt;page&gt;.md</c>.</summary>
    private static string? PageDemoPath(string path)
    {
        var normalized = path.Replace('\\', '/');
        if (!normalized.EndsWith(".md", StringComparison.Ordinal)) return null;

        var slash = normalized.LastIndexOf('/');
        var directory = slash == -1 ? string.Empty : normalized[..(slash + 1)];
        var name = normalized[(slash + 1)..^".md".Length];

        return name == "context" ? $"{directory}demo.html" : $"{directory}{name}.demo.html";
    }

    private static bool IsDemoPath(string path)
    {
        var name = path.Replace('\\', '/').Split('/')[^1];
        return name == "demo.html" || name.EndsWith(".demo.html", StringComparison.Ordinal);
    }

    private static bool IsCloser(string line, char character, int length)
    {
        var trimmed = Trim(line);
        if (trimmed.Length < length) return false;

        foreach (var c in trimmed)
        {
            if (c != character) return false;
        }

        return true;
    }

    /// <summary>Split on <c>\r?\n</c>, as <c>metadata.mjs</c> splits: a lone
    /// <c>\r</c> is no break.</summary>
    private static string[] Lines(string markdown) => LineBreak().Split(markdown);

    /// <summary>
    /// Every heading outside a fence, with its slug and the demo references in its
    /// <c>meta</c> block. <c>parseDocument</c> in <c>metadata.mjs</c>: a line
    /// starting <c>```</c> or <c>~~~</c> opens a fence whatever its info string,
    /// and the next such line of the same character, at least as long, closes it.
    /// </summary>
    private static List<Chapter> Parse(string markdown)
    {
        var lines = Lines(markdown);
        var chapters = new List<Chapter>();
        string? fence = null;

        for (var i = 0; i < lines.Length; i++)
        {
            var marker = FenceMarker().Match(lines[i]);

            if (fence is not null)
            {
                if (marker.Success && marker.Groups[1].Value[0] == fence[0] && marker.Groups[1].Value.Length >= fence.Length) fence = null;
                continue;
            }

            if (marker.Success)
            {
                fence = marker.Groups[1].Value;
                continue;
            }

            var heading = Heading().Match(lines[i]);
            if (!heading.Success) continue;

            chapters.Add(new Chapter(
                heading.Groups[1].Value.Length,
                Slugify(Trim(heading.Groups[2].Value)),
                i + 1,
                MetaDemos(lines, i + 1)));
        }

        return chapters;
    }

    /// <summary>The <c>demo</c> and <c>playthrough</c> references of the
    /// <c>meta</c> block that is the first non-blank line under a heading, in that
    /// order; none when there is no such block.</summary>
    private static IReadOnlyList<string> MetaDemos(string[] lines, int from)
    {
        var j = from;
        while (j < lines.Length && Trim(lines[j]).Length == 0) j++;

        if (j >= lines.Length || !MetaOpener().IsMatch(Trim(lines[j]))) return [];

        var fields = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);
        for (var k = j + 1; k < lines.Length && Trim(lines[k]) != "```"; k++)
        {
            var colon = lines[k].IndexOf(':', StringComparison.Ordinal);
            if (Trim(lines[k]).Length == 0 || colon == -1) continue;

            fields[Trim(lines[k][..colon])] = Scalar(lines[k][(colon + 1)..]);
        }

        return [.. fields.GetValueOrDefault("demo", []), .. fields.GetValueOrDefault("playthrough", [])];
    }

    /// <summary>A meta value as a list: <c>[a, "b"]</c> is its entries, quotes
    /// stripped and a comma inside quotes kept; anything else is one entry; empty
    /// and <c>null</c> are none.</summary>
    private static IReadOnlyList<string> Scalar(string raw)
    {
        var value = Trim(raw);
        if (value.Length == 0 || value == "null") return [];

        if (!(value.StartsWith('[') && value.EndsWith(']'))) return [StripQuotes(value)];

        var inner = Trim(value[1..^1]);
        if (inner.Length == 0) return [];

        var entries = new List<string>();
        var current = new StringBuilder();
        char? quote = null;

        foreach (var c in inner)
        {
            if (quote is not null)
            {
                if (c == quote) quote = null;
            }
            else if (c is '"' or '\'')
            {
                if (Trim(current.ToString()).Length == 0) quote = c;
            }
            else if (c == ',')
            {
                entries.Add(current.ToString());
                current.Clear();
                continue;
            }

            current.Append(c);
        }

        entries.Add(current.ToString());

        return [.. entries.Select(entry => StripQuotes(Trim(entry))).Where(entry => entry.Length > 0)];
    }

    private static string StripQuotes(string value) =>
        value.Length >= 2 && ((value[0] == '"' && value[^1] == '"') || (value[0] == '\'' && value[^1] == '\''))
            ? value[1..^1]
            : value;

    /// <summary>JavaScript's <c>String.prototype.trim</c>: the characters of
    /// <see cref="SpaceCharacters"/>, which <see cref="string.Trim()"/> does not
    /// use.</summary>
    private static string Trim(string value) => value.Trim(JsSpace);

    private static readonly char[] JsSpace = ['\t', '\n', '\v', '\f', '\r', ' ', '\u00a0', '\u1680', '\u2000', '\u2001', '\u2002', '\u2003', '\u2004', '\u2005', '\u2006', '\u2007', '\u2008', '\u2009', '\u200a', '\u2028', '\u2029', '\u202f', '\u205f', '\u3000', '\ufeff'];

    /// <summary>GitHub's anchor: lower-cased, trimmed, everything but letters,
    /// digits, underscores, whitespace and hyphens dropped, and each whitespace
    /// character a hyphen — runs are not collapsed.</summary>
    private static string Slugify(string text) =>
        JsWhitespaceOne().Replace(SlugStrip().Replace(Trim(text.ToLowerInvariant()), string.Empty), "-");

    private sealed record Chapter(int Level, string Slug, int Line, IReadOnlyList<string> Demos);

    /// <summary>The characters of JavaScript's <c>\s</c>, which is not .NET's: it
    /// has U+FEFF and not U+0085. Every whitespace test here uses them, so a line
    /// reads as <c>metadata.mjs</c> reads it.</summary>
    private const string SpaceCharacters = @"\t\n\v\f\r \u00a0\u1680\u2000-\u200a\u2028\u2029\u202f\u205f\u3000\ufeff";

    private const string Space = "[" + SpaceCharacters + "]";

    [GeneratedRegex(@"\r?\n")]
    private static partial Regex LineBreak();

    [GeneratedRegex("^" + Space + "*(`{3,}|~{3,})")]
    private static partial Regex FenceMarker();

    // JavaScript's '.' stops at CR, LINE SEPARATOR and PARAGRAPH SEPARATOR as well
    // as LF, and its '$' without the m flag is the end of the input; .NET's '$' also
    // matches before a final LF. Spelled out so a heading reads as metadata.mjs
    // reads it.
    [GeneratedRegex("^(#{1,6})" + Space + @"+([^\n\r\u2028\u2029]*)\z")]
    private static partial Regex Heading();

    [GeneratedRegex("^#{1,6}" + Space + @"+([^\n\r\u2028\u2029]*)\z")]
    private static partial Regex HeadingText();

    [GeneratedRegex("^" + Space + "*(`{3,}|~{3,})" + Space + "*([^`~" + SpaceCharacters + "]*)" + Space + @"*\z")]
    private static partial Regex FenceOpener();

    [GeneratedRegex("^```meta" + Space + @"*\z")]
    private static partial Regex MetaOpener();

    [GeneratedRegex(Space + "+")]
    private static partial Regex JsWhitespace();

    [GeneratedRegex(Space)]
    private static partial Regex JsWhitespaceOne();

    [GeneratedRegex(@"[^\p{L}\p{N}_" + SpaceCharacters + "-]")]
    private static partial Regex SlugStrip();

    [GeneratedRegex(@"<!--\s*template:begin\b[^>]*?-->[\s\S]*?(?:<!--\s*template:end\b[^>]*?-->|\z)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex TemplateRegion();
}
