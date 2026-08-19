using System.Text;
using System.Text.RegularExpressions;

namespace Backlog.Desktop.UI.Knowledge;

/// <summary>
/// Writes an edited knowledge chapter back over its source <c>.md</c> file.
/// <para>
/// Whole-file, unlike the segment writes Backlog Management makes into a
/// <c>.backlog</c> file: a knowledge chapter is the file, and the editing surface
/// holds all of it. What carries over from that writer is the care about a file it
/// did not author — the line endings, the final newline and the byte-order mark
/// are the file's, not the app's, so a chapter someone edited one line of does not
/// arrive in the repository as a whole-file diff.
/// </para>
/// <para>
/// The write is atomic — temp sibling, then a replacing swap. A torn status line
/// is a bad afternoon; a torn chapter is lost work, and this surface saves on a
/// debounce while somebody types, so the window is open far more often than the
/// status writer's is. <see cref="KnowledgeMarkdownStatusWriter"/> deliberately
/// stays as it is: widening it is a separate change with its own tests.
/// </para>
/// </summary>
public sealed class KnowledgeChapterWriter
{
    private static readonly UTF8Encoding Utf8WithoutBom = new(encoderShouldEmitUTF8Identifier: false);
    private static readonly UTF8Encoding Utf8WithBom = new(encoderShouldEmitUTF8Identifier: true);
    private static readonly byte[] Utf8ByteOrderMark = [0xEF, 0xBB, 0xBF];

    /// <summary>
    /// Writes <paramref name="rawText"/> over the chapter, reconciling the one
    /// field a concurrent status write could have moved underneath it.
    /// </summary>
    /// <param name="chapter">Which chapter, and the root it may not leave.</param>
    /// <param name="rawText">The whole chapter as the editor holds it.</param>
    /// <param name="baseline">The status of every <c>meta</c> fence as the buffer
    /// was loaded with them. It is what makes the merge decidable: without it,
    /// "the buffer still says draft" and "the buffer was changed to draft" are the
    /// same string.</param>
    /// <returns>The text actually written and the statuses it carries, so the
    /// caller can keep its buffer and its baseline in step with the file.</returns>
    public async Task<KnowledgeChapterWriteResult> WriteAsync(
        KnowledgeChapterRef chapter,
        string rawText,
        KnowledgeChapterStatus? baseline = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(chapter);
        ArgumentNullException.ThrowIfNull(rawText);
        cancellationToken.ThrowIfCancellationRequested();

        // Checked again here regardless of what the resolver decided. A ref is a
        // plain record anybody can construct, and this is the last place before
        // a file is replaced.
        var filePath = KnowledgeChapterPaths.ResolveWithin(chapter.RootPath, chapter.RelativePath)
            ?? throw new InvalidOperationException($"Knowledge chapter path escapes the knowledge root: {chapter.RelativePath}");

        if (!File.Exists(filePath))
        {
            throw new FileNotFoundException($"Knowledge chapter file was not found: {chapter.RelativePath}", filePath);
        }

        // Bytes rather than text: a byte-order mark is not part of the decoded
        // string, so reading the file as text is reading it with one of the
        // details that has to survive the write already discarded.
        var bytes = await File.ReadAllBytesAsync(filePath, cancellationToken).ConfigureAwait(false);
        var byteOrderMark = StartsWithUtf8ByteOrderMark(bytes);
        var offset = byteOrderMark ? Utf8ByteOrderMark.Length : 0;
        var original = Encoding.UTF8.GetString(bytes, offset, bytes.Length - offset);

        var newline = DominantNewline(original);
        var originalEndedWithNewline = original.EndsWith('\n');

        // A leading U+FEFF in the buffer is the mark round-tripped as a character
        // by something upstream rather than content: whether the file carries one
        // is decided from its bytes and nowhere else.
        var body = KnowledgeChapterText.ToLineFeeds(rawText.TrimStart('\uFEFF'));
        var merged = MergeStatuses(body, original, baseline);

        // Translated and re-terminated rather than split into lines and joined
        // with the file's newline: string.Join drops the trailing newline that
        // the last empty line stands for, and it does it silently, so every
        // chapter ever saved would lose its final newline exactly once.
        var text = merged.Replace("\n", newline, StringComparison.Ordinal);
        text = WithFinalNewlineConvention(text, newline, originalEndedWithNewline);

        await WriteAtomicallyAsync(filePath, text, byteOrderMark, cancellationToken).ConfigureAwait(false);

        return new KnowledgeChapterWriteResult(text, KnowledgeChapterStatus.Read(text));
    }

    /// <summary>
    /// Three-way merge on <c>status:</c>, and on nothing else — once per
    /// <c>meta</c> fence in the chapter.
    /// <para>
    /// A pending body debounce and a status-selector change are two
    /// read-modify-writes on one file with no lock between them, and the editor's
    /// buffer holds the status lines as they were when the buffer was loaded. Left
    /// alone, whichever wrote last would win the whole file and the status change
    /// next door would be undone by the keystroke that followed it.
    /// </para>
    /// <para>
    /// Per fence rather than per chapter because that is how the field is
    /// addressed: the status writer upserts under the heading it was given, and
    /// the domain and technology panels do address headings inside the very file
    /// the editor is holding — a domain section as <c>document.md#slug</c>, a
    /// technology node as <c>.tech/layer.md#slug</c>. A merge that reconciled only
    /// the chapter's own fence would leave every one of those changes to be
    /// reverted by the next keystroke.
    /// </para>
    /// <para>
    /// So for each fence three values are compared: the baseline, what the text
    /// being written says, and what the file says now. Disk wins only when the text
    /// is still carrying the baseline — that is, when nobody edited that fence
    /// here. Otherwise the text wins, which is what keeps a status typed into the
    /// raw markdown from being quietly reverted by a blunt "disk always wins".
    /// This is per-field last-write-wins, sanctioned by
    /// <c>.arc42/08-crosscutting-concepts.md#storage-and-sync</c>.
    /// </para>
    /// <para>
    /// Fences are paired between the two texts by the heading that owns them,
    /// because a heading is what the status writer addresses and because the body
    /// around a fence may have been edited out of all recognition in the meantime.
    /// Three cases follow from that pairing, and all three resolve in favour of the
    /// buffer:
    /// </para>
    /// <list type="bullet">
    /// <item>A fence in the buffer that disk has none of is new here — a section
    /// somebody just wrote — so there is nothing to reconcile it against.</item>
    /// <item>A fence on disk whose heading exists in the buffer without one gets
    /// the disk status inserted, but only while the buffer's baseline had none
    /// either; a fence the user deleted stays deleted.</item>
    /// <item>A fence on disk whose heading is not in the buffer at all — the
    /// heading was renamed here while the status write landed on its old name — is
    /// dropped. There is no anchor left to apply it to, and applying it positionally
    /// would put somebody's status change on a different section, which is worse
    /// than losing it: the buffer's own status for that heading is written instead,
    /// and the renamed section keeps whatever it was carrying.</item>
    /// </list>
    /// </summary>
    private static string MergeStatuses(string body, string onDisk, KnowledgeChapterStatus? baseline)
    {
        var onDiskStatuses = KnowledgeChapterStatus.Read(onDisk);

        // A status missing from disk is not disk having moved on: no writer in
        // the product removes the field, so the far more likely story is a
        // chapter that never had one. Nothing to merge in.
        if (onDiskStatuses.IsEmpty) return body;

        var inText = KnowledgeChapterStatus.Read(body);
        var merged = body;

        foreach (var (heading, onDiskValue) in onDiskStatuses.ByHeading)
        {
            var textStillCarriesBaseline = Matches(inText.For(heading), baseline?.For(heading));
            var diskHasMoved = !Matches(onDiskValue, baseline?.For(heading));

            if (textStillCarriesBaseline && diskHasMoved)
            {
                merged = KnowledgeChapterText.WithStatus(merged, heading, onDiskValue);
            }
        }

        return merged;
    }

    private static bool Matches(string? left, string? right) =>
        string.Equals(left?.Trim(), right?.Trim(), StringComparison.OrdinalIgnoreCase);

    private static bool StartsWithUtf8ByteOrderMark(byte[] bytes) =>
        bytes.Length >= Utf8ByteOrderMark.Length && bytes.AsSpan(0, Utf8ByteOrderMark.Length).SequenceEqual(Utf8ByteOrderMark);

    /// <summary>
    /// The line ending the file is written in: whichever of the two it already
    /// uses for most of its lines.
    /// <para>
    /// The majority rather than "any CRLF at all", because one line pasted from a
    /// Windows editor into an otherwise line-feed chapter would otherwise decide
    /// the ending of every other line in it — a whole-file diff to spare a single
    /// line, which is the outcome this writer exists to avoid. The cost is that a
    /// genuinely mixed file is normalized to its majority rather than left as it
    /// was, and that is deliberate rather than overlooked: the editing surface
    /// hands back a buffer whose newlines are already uniform, so there is no
    /// mapping from its lines back to the endings the original lines had.
    /// Normalizing the minority is the smallest diff available, not a free one.
    /// </para>
    /// </summary>
    private static string DominantNewline(string original)
    {
        var carriageReturnLineFeeds = CountCarriageReturnLineFeeds(original);
        var bareLineFeeds = original.Count(character => character == '\n') - carriageReturnLineFeeds;

        if (carriageReturnLineFeeds > bareLineFeeds) return "\r\n";
        if (bareLineFeeds > carriageReturnLineFeeds) return "\n";

        // A tie is a file with no convention to preserve — including a file with
        // no newline in it at all. The first newline decides, because that is the
        // one an editor reports as the file's style; with none, line feeds, which
        // is what the repository stores.
        var first = original.IndexOf('\n');
        return first > 0 && original[first - 1] == '\r' ? "\r\n" : "\n";
    }

    private static int CountCarriageReturnLineFeeds(string text)
    {
        var count = 0;
        for (var i = text.IndexOf("\r\n", StringComparison.Ordinal); i >= 0; i = text.IndexOf("\r\n", i + 2, StringComparison.Ordinal))
        {
            count++;
        }

        return count;
    }

    /// <summary>
    /// The text with the file's own habit about a final newline applied to it, and
    /// nothing else about its ending touched.
    /// <para>
    /// Only the last newline is ever in question. A file that ends with one keeps
    /// ending with one and gains one if the text has none; every trailing blank
    /// line above it is the author's and is left exactly as typed. A file that ends
    /// without one loses a single trailing newline, on the grounds that one is what
    /// an editing surface adds on its own — but not two, because nobody gets two by
    /// accident, and a chapter that grows a deliberate blank line at its end is
    /// allowed to grow the newline that blank line implies.
    /// </para>
    /// </summary>
    private static string WithFinalNewlineConvention(string text, string newline, bool originalEndedWithNewline)
    {
        if (originalEndedWithNewline)
        {
            return text.EndsWith(newline, StringComparison.Ordinal) ? text : text + newline;
        }

        if (!text.EndsWith(newline, StringComparison.Ordinal)) return text;

        var withoutFinalNewline = text[..^newline.Length];
        return withoutFinalNewline.EndsWith(newline, StringComparison.Ordinal) ? text : withoutFinalNewline;
    }

    /// <summary>Written beside the chapter and swapped in, so a reader either
    /// sees the previous chapter or the new one. The temp file is a sibling rather
    /// than a temp-folder file because a move across volumes is a copy, and a copy
    /// is the non-atomic thing this is avoiding.</summary>
    private static async Task WriteAtomicallyAsync(string filePath, string text, bool byteOrderMark, CancellationToken cancellationToken)
    {
        var tempPath = filePath + ".backlog-tmp-" + Guid.NewGuid().ToString("N")[..8];

        try
        {
            await File.WriteAllTextAsync(tempPath, text, byteOrderMark ? Utf8WithBom : Utf8WithoutBom, cancellationToken).ConfigureAwait(false);
            ReplaceInPlace(tempPath, filePath);
        }
        catch
        {
            // The caller still gets the failure; what it must not also get is a
            // half-written sibling left in the knowledge folder. In the instructions
            // area, whose menu lists every file it finds rather than only *.md, it
            // would be offered as a chapter; in the four dotted areas nothing in the
            // app would ever show it, and it would simply sit in git status beside
            // the chapter, in somebody's repository, until they wondered what it was.
            if (File.Exists(tempPath))
            {
                try { File.Delete(tempPath); } catch (IOException) { } catch (UnauthorizedAccessException) { }
            }

            throw;
        }
    }

    /// <summary>
    /// Puts the temp file in the chapter's place.
    /// <para>
    /// <see cref="File.Replace(string, string, string)"/> rather than a moving
    /// overwrite, because the destination survives as itself: its access control
    /// list, its attributes and its creation time stay the chapter's own. These are
    /// checked-in repository files that something other than this app may well have
    /// opinions about, and a move would quietly give the chapter the temp file's
    /// identity instead.
    /// </para>
    /// </summary>
    private static void ReplaceInPlace(string tempPath, string filePath)
    {
        try
        {
            File.Replace(tempPath, filePath, destinationBackupFileName: null);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or PlatformNotSupportedException)
        {
            // Replace wants both files on one volume, a file system that supports
            // it, and the destination still there. When one of those stops being
            // true the atomic move is still the right write — what is lost is the
            // destination's metadata, not the chapter.
            File.Move(tempPath, filePath, overwrite: true);
        }
    }
}

/// <summary>What ended up on disk: the exact text written, and the statuses it
/// carries after the merge. Handed back so a buffer that lost a status race can
/// adopt the reconciled text instead of re-proposing the stale one on the next
/// keystroke.</summary>
public sealed record KnowledgeChapterWriteResult(string Text, KnowledgeChapterStatus Status);

/// <summary>
/// The <c>status:</c> value of every <c>meta</c> fence in a chapter, keyed by the
/// heading that owns it.
/// <para>
/// Per fence, because a chapter file holds more than the chapter's own metadata:
/// the domain panel addresses a section as <c>document.md#slug</c> and the
/// technology panel a node as <c>.tech/layer.md#slug</c>, and
/// <see cref="KnowledgeMarkdownStatusWriter"/> upserts into the fence under
/// whichever heading it was handed. A single value could not tell those apart, and
/// the merge that keeps a status change from being reverted by the next keystroke
/// needs to know which fence moved.
/// </para>
/// <para>
/// Keyed by heading slug rather than by position because position is exactly what
/// an edit destroys. The consequence is that renaming a heading in the buffer
/// unanchors the status written against its old name — see the merge in
/// <see cref="KnowledgeChapterWriter"/> for why that is the lesser loss.
/// </para>
/// </summary>
public sealed class KnowledgeChapterStatus
{
    private readonly List<KeyValuePair<string, string>> _byHeading;

    private KnowledgeChapterStatus(List<KeyValuePair<string, string>> byHeading) => _byHeading = byHeading;

    /// <summary>A chapter with no status anywhere in it, which is also what an
    /// empty buffer reads as.</summary>
    internal static KnowledgeChapterStatus None { get; } = new([]);

    /// <summary>Reads the statuses out of a chapter's markdown. This is how a
    /// caller takes the baseline it will hand back to
    /// <see cref="KnowledgeChapterWriter.WriteAsync"/>.</summary>
    public static KnowledgeChapterStatus Read(string? markdown) => KnowledgeChapterText.ReadStatuses(markdown);

    /// <summary>Built by the reader below, which is the only thing that knows
    /// how to find a fence and whose heading it is under.</summary>
    internal static KnowledgeChapterStatus Of(List<KeyValuePair<string, string>> byHeading) =>
        byHeading.Count == 0 ? None : new KnowledgeChapterStatus(byHeading);

    internal bool IsEmpty => _byHeading.Count == 0;

    /// <summary>Every status in the chapter, in the order the fences appear.</summary>
    internal IReadOnlyList<KeyValuePair<string, string>> ByHeading => _byHeading;

    /// <summary>The chapter's own status: the first fence in the file, which is the
    /// one under its title.</summary>
    internal string? Chapter => _byHeading.Count == 0 ? null : _byHeading[0].Value;

    /// <summary>The status under one heading, or null when that heading has no
    /// fence — or is not in this text at all, which for a merge is the same
    /// answer.</summary>
    internal string? For(string heading)
    {
        foreach (var (candidate, value) in _byHeading)
        {
            if (string.Equals(candidate, heading, StringComparison.OrdinalIgnoreCase)) return value;
        }

        return null;
    }
}

/// <summary>
/// Reading and rewriting the one metadata field a chapter write has to reason
/// about.
/// <para>
/// The fence format, the heading a fence belongs to and the slug that names that
/// heading are all <see cref="KnowledgeMarkdownStatusWriter"/>'s, down to the
/// duplicated <c>Slug</c>. Duplicated rather than shared because that writer
/// deliberately stays as it is, and the two have to agree exactly: an anchor these
/// two spelled differently would be a fence this merge could not find and that one
/// had already written to.
/// </para>
/// </summary>
internal static class KnowledgeChapterText
{
    private static readonly Regex Heading = new("^(#{1,6})[ \\t]+(.+?)\\s*$", RegexOptions.Compiled);

    internal static string ToLineFeeds(string text) =>
        text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');

    /// <summary>The status in every <c>meta</c> fence, keyed by the heading that
    /// owns it.</summary>
    internal static KnowledgeChapterStatus ReadStatuses(string? markdown)
    {
        if (string.IsNullOrEmpty(markdown)) return KnowledgeChapterStatus.None;

        var lines = ToLineFeeds(markdown).Split('\n');
        var statuses = new List<KeyValuePair<string, string>>();

        foreach (var fence in MetaFences(lines))
        {
            if (fence.StatusLine < 0) continue;

            var value = lines[fence.StatusLine].Trim()["status:".Length..].Trim();
            if (value.Length == 0) continue;
            if (statuses.Any(entry => string.Equals(entry.Key, fence.Heading, StringComparison.OrdinalIgnoreCase))) continue;

            statuses.Add(new KeyValuePair<string, string>(fence.Heading, value));
        }

        return KnowledgeChapterStatus.Of(statuses);
    }

    /// <summary>
    /// The same text with <paramref name="status"/> in the <c>meta</c> fence under
    /// <paramref name="heading"/>, inserting the line or the whole fence when there
    /// is none — and changing nothing when the heading itself is gone.
    /// <para>
    /// Split and joined on <c>'\n'</c> only, which is exact — the caller has
    /// already normalized to line feeds and translates back afterwards. Joining
    /// with a different newline is the operation that loses a trailing newline,
    /// and it is not this one.
    /// </para>
    /// </summary>
    internal static string WithStatus(string lineFeedText, string heading, string status)
    {
        var lines = lineFeedText.Split('\n').ToList();
        var owned = OwnedFence(lines, heading);

        if (owned is { } fence)
        {
            if (fence.StatusLine >= 0)
            {
                var indent = lines[fence.StatusLine][..(lines[fence.StatusLine].Length - lines[fence.StatusLine].TrimStart().Length)];
                lines[fence.StatusLine] = $"{indent}status: {status}";
            }
            else
            {
                lines.Insert(fence.Open + 1, $"status: {status}");
            }

            return string.Join('\n', lines);
        }

        // The heading is here but has no fence: place one exactly where the status
        // writer would, directly under the heading, so a section that gains its
        // metadata this way is indistinguishable from one that gained it there.
        var headingLine = FindHeading(lines, heading);
        if (headingLine >= 0)
        {
            lines.InsertRange(headingLine + 1, [string.Empty, "```meta", $"status: {status}", "```"]);
            return string.Join('\n', lines);
        }

        // A status that belongs to no heading at all, in a chapter that has none
        // either: the file opens with its fence.
        if (heading.Length == 0)
        {
            lines.InsertRange(0, ["```meta", $"status: {status}", "```", string.Empty]);
            return string.Join('\n', lines);
        }

        // The heading this status was written against is not in the buffer, so
        // there is nowhere to put it that would still mean what it meant.
        return lineFeedText;
    }

    /// <summary>The fence under <paramref name="heading"/>, or null when that
    /// heading has none — or is not in these lines at all.</summary>
    private static MetaFence? OwnedFence(IReadOnlyList<string> lines, string heading)
    {
        foreach (var fence in MetaFences(lines))
        {
            if (string.Equals(fence.Heading, heading, StringComparison.OrdinalIgnoreCase)) return fence;
        }

        return null;
    }

    /// <summary>
    /// Every <c>meta</c> fence in the text, with the heading that owns it.
    /// <para>
    /// Owned by the nearest heading above it, and only the first fence under a
    /// heading is ever reconciled, because that is the one the status writer
    /// addresses: it skips blank lines under the heading and upserts into what it
    /// finds there. Nothing but a <c>```meta</c> line opens a fence here either —
    /// the same blind spot as the status writer's, and agreeing with it about which
    /// fence is which matters more than being cleverer than it about a metadata
    /// block quoted inside a code sample.
    /// </para>
    /// </summary>
    private static List<MetaFence> MetaFences(IReadOnlyList<string> lines)
    {
        var fences = new List<MetaFence>();
        var heading = string.Empty;

        for (var i = 0; i < lines.Count; i++)
        {
            var match = Heading.Match(lines[i]);
            if (match.Success)
            {
                heading = Slug(match.Groups[2].Value.Trim());
                continue;
            }

            if (!string.Equals(lines[i].Trim(), "```meta", StringComparison.OrdinalIgnoreCase)) continue;

            var close = i + 1;
            var statusLine = -1;
            while (close < lines.Count && !lines[close].TrimStart().StartsWith("```", StringComparison.Ordinal))
            {
                if (statusLine < 0 && lines[close].TrimStart().StartsWith("status:", StringComparison.OrdinalIgnoreCase))
                {
                    statusLine = close;
                }

                close++;
            }

            fences.Add(new MetaFence(heading, i, statusLine));
            i = close;
        }

        return fences;
    }

    private static int FindHeading(IReadOnlyList<string> lines, string heading)
    {
        for (var i = 0; i < lines.Count; i++)
        {
            var match = Heading.Match(lines[i]);
            if (match.Success && string.Equals(Slug(match.Groups[2].Value.Trim()), heading, StringComparison.OrdinalIgnoreCase))
            {
                return i;
            }
        }

        return -1;
    }

    /// <summary>The anchor a panel would use for a heading. Character for
    /// character <see cref="KnowledgeMarkdownStatusWriter"/>'s, for the reason
    /// given on this class.</summary>
    private static string Slug(string heading)
    {
        var chars = heading
            .ToLowerInvariant()
            .Select(ch => char.IsLetterOrDigit(ch) ? ch : '-')
            .ToArray();

        return string.Join('-', new string(chars).Split('-', StringSplitOptions.RemoveEmptyEntries));
    }

    /// <summary>One <c>meta</c> fence: whose heading it is under, where it opens,
    /// and which line inside it carries the status — <c>-1</c> when none
    /// does.</summary>
    private readonly record struct MetaFence(string Heading, int Open, int StatusLine);
}
