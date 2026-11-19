using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

using Backlog.Infrastructure.Devbook.Building;
using Backlog.SharedKernel.Markdown;

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
            var wanted = HeadingAnchor.Of(slug);
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

        var heading = start < lines.Length ? DevbookMarkdown.Heading().Match(lines[start]) : Match.Empty;

        return string.Join(
            '\n',
            new[] { heading.Success ? heading.Groups[2].Value : string.Empty, HashedText(lines, start + 1, end, dropMeta: true) }
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
            kept.Select(DevbookMarkdown.CollapseWhitespace).Where(entry => entry.Length > 0));
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
    private static string[] Lines(string markdown) => DevbookMarkdown.Lines(markdown);

    /// <summary>
    /// Every heading outside a fence, with its slug and the demo references in its
    /// <c>meta</c> block: <c>parseDocument</c> in <c>metadata.mjs</c>, which is
    /// <see cref="DevbookMarkdown.Parse"/> — the builder's port of the same function.
    /// </summary>
    private static List<Chapter> Parse(string markdown) =>
        [.. DevbookMarkdown.Parse(markdown).Chapters.Select(chapter => new Chapter(
            chapter.Level,
            chapter.Slug,
            chapter.Line,
            [.. DevbookMarkdown.AsList(chapter.Meta?.GetValueOrDefault("demo")), .. DevbookMarkdown.AsList(chapter.Meta?.GetValueOrDefault("playthrough"))]))];

    /// <summary>JavaScript's <c>String.prototype.trim</c>.</summary>
    private static string Trim(string value) => DevbookMarkdown.JsTrim(value);

    private sealed record Chapter(int Level, string Slug, int Line, IReadOnlyList<string> Demos);

    private const string Space = "[" + DevbookMarkdown.JsWhitespaceClass + "]";

    [GeneratedRegex("^" + Space + "*(`{3,}|~{3,})" + Space + "*([^`~" + DevbookMarkdown.JsWhitespaceClass + "]*)" + Space + @"*\z")]
    private static partial Regex FenceOpener();
    [GeneratedRegex(@"<!--\s*template:begin\b[^>]*?-->[\s\S]*?(?:<!--\s*template:end\b[^>]*?-->|\z)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex TemplateRegion();
}
