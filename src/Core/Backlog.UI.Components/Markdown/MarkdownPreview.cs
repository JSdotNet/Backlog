using System.Text.RegularExpressions;

namespace Backlog.UI.Components.Markdown;

/// <summary>
/// Turns a markdown body into a small block/inline tree for the read view — what
/// you see when an entry is not focused, and what a file looks like in FileView.
/// <para>
/// Hand-written and still partial, but partial on purpose rather than by
/// accident: it covers the markdown people actually write in this product —
/// headings, lists (nested), checklists, quotes, code, tables, footnotes,
/// emphasis, strikethrough, links, images and <c>#tags</c> — and renders
/// anything else as plain text, so an unfinished line in a half-typed entry
/// degrades into readable prose instead of disappearing or erroring.
/// </para>
/// <para>
/// What it still does not do, deliberately: setext headings, reference-style
/// links, raw HTML blocks, definition lists and nested block containers (a list
/// item holding a quote or a fence). Each of those wants a real block/inline
/// state machine, and every one of them degrades to readable prose today.
/// </para>
/// <para>
/// Every block it emits carries the lines it was read from —
/// <see cref="MdBlock.Source"/>. That is what lets a caller hand back the
/// author's own bytes for a block instead of writing the model out again: the
/// parse is lossy by design above, so a round trip through it reformats a table,
/// re-indents a list and turns the markdown it does not model into prose. A span
/// costs nothing to carry and makes the lossiness survivable.
/// </para>
/// </summary>
public static class MarkdownPreview
{
    private static readonly Regex HeadingRegex = new(@"^(#{1,6})[ \t]+(.*)$", RegexOptions.Compiled);
    private static readonly Regex CheckboxPrefixRegex = new(@"^\[( |x|X)\][ \t]+", RegexOptions.Compiled);
    private static readonly Regex TaskItemRegex = new(@"^[ \t]*[-*][ \t]+\[(?<marker> |x|X)\][ \t]+(?<text>.*)$", RegexOptions.Compiled);
    private static readonly Regex BulletRegex = new(@"^[ \t]*[-*][ \t]+(.*)$", RegexOptions.Compiled);
    private static readonly Regex OrderedRegex = new(@"^[ \t]*\d+[.)][ \t]+(.*)$", RegexOptions.Compiled);

    /// <summary>A footnote definition: <c>[^label]: the note</c>, on its own line.</summary>
    private static readonly Regex FootnoteDefinitionRegex = new(@"^[ \t]*\[\^(?<label>[^\]\s]+)\]:[ \t]*(?<text>.*)$", RegexOptions.Compiled);

    /// <summary>The row of dashes under a table's header. It is what makes the
    /// pipes above it a table rather than a paragraph that happens to contain
    /// them — a line of prose with a pipe in it is still prose.</summary>
    private static readonly Regex TableDelimiterRegex = new(@"^[ \t]*\|?[ \t]*:?-+:?[ \t]*(\|[ \t]*:?-+:?[ \t]*)*\|?[ \t]*$", RegexOptions.Compiled);

    private static readonly Regex InlineRegex = new(
        @"(?<code>`[^`\n]+`)" +
        // Image before link: both start with a bracket run, and the link
        // alternative would otherwise match from the `[` and leave the `!`
        // stranded as text.
        @"|(?<image>!\[[^\]\n]*\]\((?:[^()\s]|\([^()\s]*\))+\))" +
        @"|(?<bold>\*\*[^*\n]+\*\*)" +
        @"|(?<strike>~~[^~\n]+~~)" +
        @"|(?<em>(?<!\*)\*[^*\n]+\*(?!\*))" +
        // Before the link alternative for the same reason as the image, though a
        // footnote reference has no `(...)` after it to be confused with one.
        @"|(?<footnote>\[\^[^\]\s]+\])" +
        // The URL may contain one level of balanced brackets — Wikipedia article
        // titles do — so stopping at the first `)` would truncate the link and
        // leave a stray bracket in the text.
        @"|(?<link>\[[^\]\n]+\]\((?:[^()\s]|\([^()\s]*\))+\))" +
        @"|(?<tag>(?<!\S)#[A-Za-z][\w-]*)",
        RegexOptions.Compiled);

    /// <summary>
    /// Reads a body as an <em>entry</em>: a <c>##</c> heading is a sub-item, and
    /// it and everything under it is folded into an <see cref="MdSubItem"/> for
    /// the host to lay out as its own card.
    /// </summary>
    public static IReadOnlyList<MdBlock> Parse(
        string? body,
        string? inheritedArea = null,
        IMarkdownMetadataReader? metadataReader = null) =>
        Parse(body, inheritedArea, metadataReader, asEntry: true);

    /// <summary>
    /// Reads a body as a plain markdown <em>document</em>: a <c>##</c> is a
    /// heading and nothing more.
    /// <para>
    /// This is the reading a file wants. <see cref="Parse"/> hands sub-items to
    /// the host and renders nothing for them itself, which is right for an entry
    /// and silently swallows most of a real file — where <c>##</c> is just how
    /// people write sections.
    /// </para>
    /// </summary>
    public static IReadOnlyList<MdBlock> ParseDocument(string? body) =>
        Parse(body, inheritedArea: null, metadataReader: null, asEntry: false);

    private static IReadOnlyList<MdBlock> Parse(
        string? body,
        string? inheritedArea,
        IMarkdownMetadataReader? metadataReader,
        bool asEntry)
    {
        var lines = (body ?? string.Empty).Replace("\r\n", "\n").Split('\n');

        // Footnote definitions are lifted out before anything else reads the
        // lines. A definition may sit anywhere — people write them at the bottom
        // — and a reference has to know whether it points at one, so the whole
        // set has to be known before the first reference is parsed.
        var footnotes = new Footnotes(CollectDefinitions(lines));

        var blocks = new List<MdBlock>();
        var paragraph = new List<string>();
        var items = new List<RawItem>();
        var quote = new List<string>();
        var taskIndex = 0;

        // Where the run being accumulated started, and one past the last line it
        // has taken. Two numbers rather than a list of indices because a run is
        // always contiguous: a blank line, a heading, a fence — anything that is
        // not another line of the same run — flushes it.
        var paragraphSpan = MdSourceSpan.None;
        var quoteSpan = MdSourceSpan.None;

        void FlushParagraph()
        {
            if (paragraph.Count == 0) return;
            blocks.Add(new MdParagraph(ParseInlines(string.Join(" ", paragraph), footnotes)) { Source = paragraphSpan });
            paragraph.Clear();
        }

        void FlushList()
        {
            if (items.Count == 0) return;

            // One list per run of the same kind at the same depth. A numbered
            // line under a bullet is a nested list and BuildList takes it; a
            // numbered line *beside* one is a second list, and this loop is what
            // emits it rather than dropping everything after the change.
            var cursor = 0;
            while (cursor < items.Count)
            {
                blocks.Add(BuildList(items, ref cursor, items[cursor].Indent));
            }

            items.Clear();
        }

        void FlushQuote()
        {
            if (quote.Count == 0) return;
            blocks.Add(new MdQuote(ParseInlines(string.Join(" ", quote), footnotes)) { Source = quoteSpan });
            quote.Clear();
        }

        void FlushAll()
        {
            FlushParagraph();
            FlushList();
            FlushQuote();
        }

        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i].TrimEnd();
            var trimmed = line.TrimStart();

            if (OpeningFence(trimmed) is { } fence)
            {
                FlushAll();
                var opened = i;
                var language = trimmed[fence.Length..].Trim();
                var code = new List<string>();
                i++;
                while (i < lines.Length && !ClosesFence(lines[i], fence))
                {
                    code.Add(lines[i]);
                    i++;
                }

                // The closing fence belongs to the block. An unterminated fence
                // ran to the end of the body, and its span says so rather than
                // naming a line that is not there.
                blocks.Add(new MdCode(string.Join('\n', code), language)
                {
                    Source = new MdSourceSpan(opened, i < lines.Length ? i + 1 : lines.Length)
                });

                continue;
            }

            if (trimmed.Length == 0)
            {
                FlushAll();
                continue;
            }

            // A definition was already collected; it is not content in the flow.
            if (FootnoteDefinitionRegex.IsMatch(line))
            {
                FlushAll();
                continue;
            }

            if (trimmed is "---" or "***" or "___")
            {
                FlushAll();
                blocks.Add(new MdDivider { Source = new MdSourceSpan(i, i + 1) });
                continue;
            }

            // A table is only a table when the row under the header says so, so
            // this has to look one line ahead before committing.
            if (trimmed.Contains('|', StringComparison.Ordinal)
                && i + 1 < lines.Length
                && lines[i + 1].Contains('|', StringComparison.Ordinal)
                && TableDelimiterRegex.IsMatch(lines[i + 1].TrimEnd()))
            {
                FlushAll();
                var opened = i;

                // ReadTable leaves the index on the table's last line, so the
                // span closes one past it — the same place the loop's own i++ is
                // about to take the reading.
                blocks.Add(ReadTable(lines, ref i, footnotes) with { Source = new MdSourceSpan(opened, i + 1) });
                continue;
            }

            var heading = HeadingRegex.Match(trimmed);
            if (heading.Success)
            {
                FlushAll();
                var opened = i;
                var level = heading.Groups[1].Value.Length;
                var text = heading.Groups[2].Value.Trim();
                bool? done = null;
                MarkdownMetadata? metadata = null;

                // Only an entry has sub-item state on a heading. In a document a
                // leading `[x]` is text the author typed, and stripping it would
                // drop content the view has nowhere else to show.
                if (asEntry && level is 2 or 3)
                {
                    var box = CheckboxPrefixRegex.Match(text);
                    if (box.Success)
                    {
                        done = box.Groups[1].Value is "x" or "X";
                        text = text[box.Length..].Trim();
                    }
                }

                if (asEntry
                    && level is 2 or 3
                    && metadataReader is not null
                    && i + 1 < lines.Length
                    && metadataReader.IsMetadataLine(lines[i + 1]))
                {
                    metadata = metadataReader.Read(lines[i + 1]);
                    i++;
                }

                // One line, or two when a metadata line was taken with it: i has
                // already moved past that line, so the span closes on the
                // reading rather than on a count of its own.
                blocks.Add(new MdHeading(level, ParseInlines(text, footnotes), done, metadata, inheritedArea)
                {
                    Source = new MdSourceSpan(opened, i + 1)
                });

                continue;
            }

            if (trimmed.StartsWith(">", StringComparison.Ordinal))
            {
                FlushParagraph();
                FlushList();

                // Consecutive `>` lines are one quote. They were one quote per
                // line before, which turned a two-line quotation into two
                // stacked bars with a gap down the middle of the sentence.
                var text = trimmed[1..];
                quoteSpan = quote.Count == 0 ? new MdSourceSpan(i, i + 1) : quoteSpan.To(i + 1);
                quote.Add(text.StartsWith(' ') ? text[1..] : text);
                continue;
            }

            FlushQuote();

            var task = TaskItemRegex.Match(line);
            if (task.Success)
            {
                FlushParagraph();
                var taskText = task.Groups["text"].Value;
                items.Add(new RawItem(
                    IndentOf(line),
                    Ordered: false,
                    Done: task.Groups["marker"].Value is "x" or "X",
                    taskText,
                    ParseInlines(taskText, footnotes),
                    taskIndex++,
                    new MdSourceSpan(i, i + 1)));
                continue;
            }

            var bullet = BulletRegex.Match(line);
            if (bullet.Success)
            {
                FlushParagraph();
                var bulletText = bullet.Groups[1].Value;
                items.Add(new RawItem(IndentOf(line), Ordered: false, Done: null, bulletText, ParseInlines(bulletText, footnotes), null, new MdSourceSpan(i, i + 1)));
                continue;
            }

            var numbered = OrderedRegex.Match(line);
            if (numbered.Success)
            {
                FlushParagraph();
                var numberedText = numbered.Groups[1].Value;
                items.Add(new RawItem(IndentOf(line), Ordered: true, Done: null, numberedText, ParseInlines(numberedText, footnotes), null, new MdSourceSpan(i, i + 1)));
                continue;
            }

            // A line with no marker of its own, written further in than the item
            // above it, is that item continued — someone wrapped a bullet rather
            // than starting a new one. Without this the list ended here and the
            // rest of the sentence became a paragraph outside it.
            if (items.Count > 0 && IndentOf(line) > items[^1].Indent)
            {
                var continued = items[^1];
                var text = continued.Text + " " + trimmed;

                // The joined text is re-read whole, so emphasis opened on the
                // first line and closed on the second is still emphasis. The
                // item's span grows to cover the line it swallowed.
                items[^1] = continued with
                {
                    Text = text,
                    Content = ParseInlines(text, footnotes),
                    Source = continued.Source.To(i + 1)
                };

                continue;
            }

            FlushList();
            paragraphSpan = paragraph.Count == 0 ? new MdSourceSpan(i, i + 1) : paragraphSpan.To(i + 1);
            paragraph.Add(trimmed);
        }

        FlushAll();

        // The notes go last, in the order they were first referenced, which is
        // the order a reader meets them.
        if (footnotes.Notes.Count > 0) blocks.Add(new MdFootnotes(footnotes.Notes));

        return asEntry ? GroupSubItems(blocks) : blocks;
    }

    /// <summary>
    /// The fence a line opens, as the marker it is written with and how many of
    /// them there are, or null when the line opens none.
    /// <para>
    /// Both markers, because CommonMark has both and a <c>~~~</c> fence is how an
    /// author writes a block whose body contains backtick fences. Reading only
    /// <c>```</c> meant a <c>~~~</c> block was not a block at all: its body was
    /// parsed as prose, and every fence inside it as a block of its own.
    /// </para>
    /// </summary>
    internal static (char Marker, int Length)? OpeningFence(string trimmed)
    {
        if (trimmed.Length < 3 || trimmed[0] is not ('`' or '~')) return null;

        var marker = trimmed[0];
        var length = 0;

        while (length < trimmed.Length && trimmed[length] == marker) length++;

        return length >= 3 ? (marker, length) : null;
    }

    /// <summary>
    /// Whether a line closes the fence that is open.
    /// <para>
    /// CommonMark's rule, and the reason this is not "starts with three
    /// backticks": a fence is closed by a run of <em>its own</em> marker, at
    /// least as long as the one that opened it, with nothing after it. That is
    /// what lets a fence hold a fence — a <c>````annotation</c> block whose body
    /// quotes a <c>```</c> code sample, which is how the devbook convention's own
    /// notes are written. Ending such a block at the first inner <c>```</c> split
    /// one note into a code block and a run of loose prose, and any caller cutting
    /// the note out by line range cut out only the first half of it, leaving the
    /// rest of somebody's private annotation in text that says it carries none.
    /// </para>
    /// </summary>
    internal static bool ClosesFence(string line, (char Marker, int Length) fence)
    {
        var trimmed = line.TrimStart();

        return OpeningFence(trimmed) is { } run
            && run.Marker == fence.Marker
            && run.Length >= fence.Length
            // An info string on a closing fence is not a closing fence. Only the
            // run and whatever whitespace follows it.
            && trimmed[run.Length..].Trim().Length == 0;
    }

    /// <summary>
    /// One line's effect on whether the reading is inside a fence, for the two
    /// walks that have to agree with <see cref="Parse"/> about where code is
    /// without building its blocks — the footnote collector and
    /// <see cref="ToggleTask"/>. True when the line is a fence's own or sits
    /// inside one, so the caller skips it.
    /// <para>
    /// Both used to flip a flag on every line starting with three backticks. That
    /// is the same wrong rule <see cref="ClosesFence"/> exists to replace, and it
    /// disagreed with the parse exactly where a fence holds a fence: inside a
    /// <c>````annotation</c> note quoting a code sample, the flag read the sample
    /// as the prose between two blocks, so a <c>- [ ]</c> in it got a task index
    /// the parse never handed out and every real checkbox after it toggled the
    /// line above its own.
    /// </para>
    /// </summary>
    private static bool StepFence(string line, ref (char Marker, int Length)? open)
    {
        if (open is { } fence)
        {
            if (ClosesFence(line, fence)) open = null;
            return true;
        }

        open = OpeningFence(line.Trim());
        return open is not null;
    }

    /// <summary>How deep a list line is indented, with a tab counting as four
    /// columns. Nesting is decided by this and nothing else — the marker a line
    /// uses says what kind of item it is, never what level it sits at.</summary>
    private static int IndentOf(string line)
    {
        var width = 0;
        foreach (var ch in line)
        {
            if (ch == ' ') width++;
            else if (ch == '\t') width += 4;
            else break;
        }

        return width;
    }

    /// <summary>One list line before nesting: what it said, how far in it was
    /// written, the source text behind it — kept so a wrapped line can be joined
    /// on and the whole item re-read as one — and the lines it came from.</summary>
    private sealed record RawItem(
        int Indent,
        bool Ordered,
        bool? Done,
        string Text,
        IReadOnlyList<MdInline> Content,
        int? TaskIndex,
        MdSourceSpan Source);

    /// <summary>
    /// Folds a flat run of list lines into the nesting their indentation
    /// describes. A deeper line belongs to the item above it; a line at the same
    /// depth but a different kind — a number under a bullet — starts a list of
    /// its own, because those are two lists that happen to touch.
    /// </summary>
    private static MdList BuildList(IReadOnlyList<RawItem> raw, ref int index, int level)
    {
        var ordered = raw[index].Ordered;
        var from = index;
        var items = new List<MdListItem>();

        while (index < raw.Count && raw[index].Indent >= level && raw[index].Ordered == ordered)
        {
            var item = raw[index++];

            // Everything deeper than this item belongs to it. Usually that is
            // one run, but a bullet with numbers under it changes kind halfway
            // down, and each kind is a list of its own — so the item holds the
            // runs, not a run. Consuming them in a loop is also what stops the
            // second one from escaping to the top level, which is where it went
            // when an item could only hold one.
            List<MdList>? children = null;
            while (index < raw.Count && raw[index].Indent > item.Indent)
            {
                (children ??= []).Add(BuildList(raw, ref index, raw[index].Indent));
            }

            items.Add(new MdListItem(item.Done, item.Content, item.TaskIndex, children));
        }

        // The items this call took, nested ones included: they were read in line
        // order, so the first one's opening line and the last one's closing line
        // are the run's own. A nested list's span therefore sits inside its
        // parent's, which is the one place spans are allowed to contain one
        // another — they never straddle.
        return new MdList(ordered, items)
        {
            Source = new MdSourceSpan(raw[from].Source.StartLine, raw[index - 1].Source.EndLineExclusive)
        };
    }

    /// <summary>Every <c>[^label]: …</c> line in the body, by label. A label
    /// defined twice keeps the first definition, the same way a duplicated key
    /// does everywhere else.</summary>
    private static Dictionary<string, string> CollectDefinitions(IReadOnlyList<string> lines)
    {
        var definitions = new Dictionary<string, string>(StringComparer.Ordinal);

        (char Marker, int Length)? open = null;
        foreach (var line in lines)
        {
            // A `[^1]:` inside a fence is a code sample, exactly as a `- [ ]` is.
            if (StepFence(line, ref open)) continue;

            var match = FootnoteDefinitionRegex.Match(line.TrimEnd());
            if (match.Success) definitions.TryAdd(match.Groups["label"].Value, match.Groups["text"].Value.Trim());
        }

        return definitions;
    }

    /// <summary>
    /// The definitions found in the body, and the numbers handed out to
    /// references as they are met.
    /// <para>
    /// Numbering by first reference rather than by where the definition was
    /// written is what makes the marks read 1, 2, 3 down the page. A reference
    /// with no definition behind it never gets a number — there would be nothing
    /// at the bottom for it to point at — and stays the text the author typed.
    /// </para>
    /// </summary>
    private sealed class Footnotes(Dictionary<string, string> definitions)
    {
        private readonly Dictionary<string, int> _numbers = new(StringComparer.Ordinal);
        private readonly List<MdFootnote> _notes = [];

        public IReadOnlyList<MdFootnote> Notes => _notes;

        public int? NumberFor(string label)
        {
            if (_numbers.TryGetValue(label, out var existing)) return existing;
            if (!definitions.TryGetValue(label, out var text)) return null;

            var number = _notes.Count + 1;
            _numbers[label] = number;

            // The note's own text is parsed without this context: a footnote
            // inside a footnote is a rabbit hole the read view has no way to
            // show, so a `[^x]` in a note stays text.
            _notes.Add(new MdFootnote(label, number, ParseInlines(text)));

            return number;
        }
    }

    /// <summary>
    /// Reads a table starting at <paramref name="index"/>, leaving the index on
    /// its last line. A short row is padded and a long one is kept: dropping the
    /// overflow would lose what the author wrote, and every row having the
    /// header's width is what lets the columns line up.
    /// </summary>
    private static MdTable ReadTable(IReadOnlyList<string> lines, ref int index, Footnotes footnotes)
    {
        var header = SplitRow(lines[index]);
        var alignment = SplitRow(lines[index + 1]).Select(ReadAlignment).ToArray();
        index++;

        var rows = new List<MdTableRow>();
        while (index + 1 < lines.Count)
        {
            var next = lines[index + 1].TrimEnd();
            if (!next.Contains('|', StringComparison.Ordinal) || next.TrimStart().Length == 0) break;

            rows.Add(new MdTableRow([.. SplitRow(next).Select(cell => new MdTableCell(ParseInlines(cell, footnotes)))]));
            index++;
        }

        return new MdTable(
            new MdTableRow([.. header.Select(cell => new MdTableCell(ParseInlines(cell, footnotes)))]),
            rows,
            alignment);
    }

    /// <summary>The cells of one table line. The outer pipes are optional in the
    /// markdown people write, so they are stripped when present rather than
    /// required.</summary>
    private static IReadOnlyList<string> SplitRow(string line)
    {
        var text = line.Trim();
        if (text.StartsWith('|')) text = text[1..];
        if (text.EndsWith('|')) text = text[..^1];

        return [.. text.Split('|').Select(cell => cell.Trim())];
    }

    private static MdAlign ReadAlignment(string cell)
    {
        var text = cell.Trim();
        var left = text.StartsWith(':');
        var right = text.EndsWith(':');

        return (left, right) switch
        {
            (true, true) => MdAlign.Center,
            (true, false) => MdAlign.Left,
            (false, true) => MdAlign.Right,
            _ => MdAlign.Default
        };
    }

    /// <summary>
    /// True when the line is one <see cref="Parse"/> would turn into a checklist
    /// item — the same test, so anyone counting checkboxes counts the same ones
    /// the read view rendered.
    /// </summary>
    public static bool IsTaskLine(string? line) => line is not null && TaskItemRegex.IsMatch(line.TrimEnd());

    /// <summary>
    /// Flips the nth checklist item in <paramref name="source"/>, where n is the
    /// <see cref="MdListItem.TaskIndex"/> the read view reported.
    /// <para>
    /// This lives beside <see cref="Parse"/> on purpose. The index only means
    /// anything if the rewriter walks the text exactly the way the parser did —
    /// same idea of what a task line is, and the same blindness to fenced code,
    /// where a <c>- [ ]</c> is a code sample and never got an index. Counting
    /// those would shift every real task by one and rewrite the wrong line.
    /// </para>
    /// <para>
    /// Nesting does not change the count: the parser walks the lines in the
    /// order they were written and so does this, so a nested task has the index
    /// its position in the file gives it.
    /// </para>
    /// <para>
    /// Returns the source untouched when the index names no task, rather than
    /// normalizing line endings on a rewrite that did not happen.
    /// </para>
    /// </summary>
    public static string ToggleTask(string? source, int taskIndex)
    {
        if (source is null) return string.Empty;
        if (taskIndex < 0) return source;

        var lines = source.Replace("\r\n", "\n").Split('\n');
        (char Marker, int Length)? open = null;
        var seen = 0;

        for (var i = 0; i < lines.Length; i++)
        {
            if (StepFence(lines[i], ref open)) continue;

            // Matched against the trimmed line so the test is IsTaskLine's, but
            // spliced into the original: the marker sits before any trailing
            // whitespace, so its index carries over unchanged.
            var task = TaskItemRegex.Match(lines[i].TrimEnd());
            if (!task.Success) continue;
            if (seen++ != taskIndex) continue;

            var marker = task.Groups["marker"];
            var flipped = marker.Value is "x" or "X" ? " " : "x";
            lines[i] = lines[i][..marker.Index] + flipped + lines[i][(marker.Index + marker.Length)..];
            return string.Join('\n', lines);
        }

        return source;
    }

    /// <summary>
    /// Folds each level-2 heading and everything under it into a single
    /// <see cref="MdSubItem"/>. A <c>##</c> heading is not really a heading in
    /// this editor — it is a sub-item — so the read view should show it as one
    /// thing with its notes attached, not as a heading followed by some
    /// unrelated paragraphs that happen to sit below it.
    /// </summary>
    private static IReadOnlyList<MdBlock> GroupSubItems(List<MdBlock> blocks)
    {
        if (!blocks.Any(b => b is MdHeading { Level: 2 or 3 })) return blocks;

        var grouped = new List<MdBlock>();
        var index = 0;

        while (index < blocks.Count)
        {
            if (blocks[index] is not MdHeading { Level: 2 or 3 } heading)
            {
                grouped.Add(blocks[index++]);
                continue;
            }

            index++;
            var children = new List<MdBlock>();
            while (index < blocks.Count && blocks[index] is not MdHeading { Level: <= 3 })
            {
                children.Add(blocks[index++]);
            }

            // The heading and everything folded under it. A child keeps its own
            // span, so a sub-item's span contains its children's rather than
            // replacing them.
            var last = children.LastOrDefault(child => child.Source.IsKnown)?.Source;

            grouped.Add(new MdSubItem(
                heading.Content,
                heading.Metadata?.Done is true || heading.Done is true,
                heading.Done is not null,
                children,
                heading.Level,
                heading.Metadata,
                heading.Area)
            {
                Source = last is { } end
                    ? new MdSourceSpan(heading.Source.StartLine, end.EndLineExclusive)
                    : heading.Source
            });
        }

        return grouped;
    }

    /// <summary>
    /// The inline parts of one line.
    /// <para>
    /// A <c>[^label]</c> only becomes a footnote reference when the body it came
    /// from defined that label, which only <see cref="Parse"/> and
    /// <see cref="ParseDocument"/> can know. Called directly, as here, a
    /// footnote marker stays the text the author typed.
    /// </para>
    /// </summary>
    public static IReadOnlyList<MdInline> ParseInlines(string text) => ParseInlines(text, footnotes: null);

    private static IReadOnlyList<MdInline> ParseInlines(string text, Footnotes? footnotes)
    {
        var parts = new List<MdInline>();
        var cursor = 0;

        foreach (Match match in InlineRegex.Matches(text))
        {
            if (match.Index > cursor)
            {
                parts.Add(new MdText(text[cursor..match.Index]));
            }

            var value = match.Value;
            if (match.Groups["code"].Success) parts.Add(new MdCodeSpan(value[1..^1]));
            else if (match.Groups["bold"].Success) parts.Add(new MdStrong(value[2..^2]));
            else if (match.Groups["strike"].Success) parts.Add(new MdStrike(value[2..^2]));
            else if (match.Groups["em"].Success) parts.Add(new MdEm(value[1..^1]));
            else if (match.Groups["tag"].Success) parts.Add(new MdTag(value[1..]));
            else if (match.Groups["image"].Success)
            {
                var split = value.IndexOf("](", StringComparison.Ordinal);
                parts.Add(new MdImage(value[2..split], value[(split + 2)..^1]));
            }
            else if (match.Groups["footnote"].Success)
            {
                var label = value[2..^1];
                var number = footnotes?.NumberFor(label);

                // No definition behind it: keep what was typed rather than
                // leaving a mark that points nowhere.
                if (number is { } n) parts.Add(new MdFootnoteRef(label, n));
                else parts.Add(new MdText(value));
            }
            else if (match.Groups["link"].Success)
            {
                var split = value.IndexOf("](", StringComparison.Ordinal);
                parts.Add(new MdLink(value[1..split], value[(split + 2)..^1]));
            }

            cursor = match.Index + match.Length;
        }

        if (cursor < text.Length) parts.Add(new MdText(text[cursor..]));
        return parts;
    }
}

/// <summary>
/// Reads the metadata line that may follow a sub-item heading. The shape of that
/// metadata belongs to whoever is editing — this library only knows that a line
/// can carry some, whether it means "done", and which tags it names — so the
/// caller supplies the reader and gets its own value back in
/// <see cref="MarkdownMetadata.Value"/>.
/// </summary>
public interface IMarkdownMetadataReader
{
    bool IsMetadataLine(string line);

    MarkdownMetadata Read(string line);
}

/// <summary>What the parser keeps from a metadata line: an opaque caller-owned
/// value, whether the sub-item counts as done, and the tags it named.</summary>
public sealed record MarkdownMetadata(object? Value, bool Done, IReadOnlyList<string> Tags)
{
    public static MarkdownMetadata None { get; } = new(null, false, []);
}

/// <summary>
/// A fence as a line opens it: the marker, how many of it, and the info string —
/// which <see cref="MarkdownPreview"/> reads whole as the language, and so does
/// this.
/// <para>
/// Public so the hand-rolled block readers in the Devbook panels can ask the
/// question the way <see cref="MarkdownPreview"/> does instead of each keeping a
/// "starts with three backticks" of its own. That shortcut ends a fence at the
/// first inner <c>```</c>, and the devbook convention's <c>annotation</c> note is
/// exactly the fence that holds one: a note quoting a code sample is opened with
/// four backticks so the sample can sit inside it, and a reader that closed it at
/// three turned the rest of somebody's review note into the chapter's prose.
/// </para>
/// </summary>
/// <param name="Marker">The fence character, <c>`</c> or <c>~</c>.</param>
/// <param name="Length">How many of it opened the fence; a closing run must be at
/// least this long.</param>
/// <param name="Language">The info string, trimmed — empty when the fence named
/// none.</param>
public readonly record struct MarkdownFence(char Marker, int Length, string Language)
{
    /// <summary>The fence <paramref name="line"/> opens, or null when it opens
    /// none. Leading whitespace is allowed, as <see cref="MarkdownPreview"/> allows
    /// it.</summary>
    public static MarkdownFence? Open(string? line)
    {
        if (line is null) return null;

        var trimmed = line.Trim();
        return MarkdownPreview.OpeningFence(trimmed) is { } run
            ? new MarkdownFence(run.Marker, run.Length, trimmed[run.Length..].Trim())
            : null;
    }

    /// <summary>Whether <paramref name="line"/> closes this fence by CommonMark's
    /// rule: its own marker, at least as many, and nothing after the run but
    /// whitespace.</summary>
    public bool IsClosedBy(string? line) =>
        line is not null && MarkdownPreview.ClosesFence(line, (Marker, Length));
}

/// <summary>
/// The lines of the body a block was read from: <paramref name="StartLine"/>
/// inclusive, <paramref name="EndLineExclusive"/> exclusive, both zero-based
/// indices into the body split on <c>\n</c> after <c>\r\n</c> has been
/// normalized — the same split <see cref="MarkdownPreview.Parse(string?, string?, IMarkdownMetadataReader?)"/>
/// makes, so a caller that splits the same way indexes the same lines.
/// </summary>
/// <remarks>
/// A span covers the block's own lines and nothing else. Between two blocks
/// there may be lines no block claims — blank lines, and the <c>[^label]:</c>
/// definitions the parser lifts out of the flow — and those are gaps rather than
/// content anybody dropped.
/// </remarks>
public readonly record struct MdSourceSpan(int StartLine, int EndLineExclusive)
{
    /// <summary>No span. What a block synthesized rather than read carries —
    /// <see cref="MdFootnotes"/>, whose definitions were collected from wherever
    /// in the body they were written, and any block a caller built by hand.</summary>
    public static MdSourceSpan None => default;

    /// <summary>Whether this names any lines at all.</summary>
    public bool IsKnown => EndLineExclusive > StartLine;

    /// <summary>How many lines it covers.</summary>
    public int LineCount => IsKnown ? EndLineExclusive - StartLine : 0;

    /// <summary>The same span, grown to end at <paramref name="endLineExclusive"/>
    /// — what a run that has just taken another line becomes.</summary>
    public MdSourceSpan To(int endLineExclusive) => this with { EndLineExclusive = endLineExclusive };
}

/// <summary>
/// One block of the read view's model.
/// </summary>
/// <remarks>
/// <see cref="Source"/> is set by the parser and defaults to
/// <see cref="MdSourceSpan.None"/>, so a block built by hand — a test fixture, a
/// storybook sample — is unchanged by its presence.
/// </remarks>
public abstract record MdBlock
{
    /// <summary>The lines this block was read from, or
    /// <see cref="MdSourceSpan.None"/> when it was not read from any.</summary>
    public MdSourceSpan Source { get; init; }
}

/// <summary>A heading. <see cref="Done"/> is non-null only for the level-2
/// headings that carry sub-item state.</summary>
public sealed record MdHeading(
    int Level,
    IReadOnlyList<MdInline> Content,
    bool? Done,
    MarkdownMetadata? Metadata = null,
    string? Area = null) : MdBlock
{
    public IReadOnlyList<string> MetadataTags => Metadata?.Tags ?? [];
}

/// <summary>A level-2 heading and everything written beneath it — the read
/// view's rendering of a sub-item.</summary>
public sealed record MdSubItem(
    IReadOnlyList<MdInline> Title,
    bool Done,
    bool HasCheckbox,
    IReadOnlyList<MdBlock> Children,
    int Level = 2,
    MarkdownMetadata? Metadata = null,
    string? Area = null) : MdBlock
{
    public IReadOnlyList<string> MetadataTags => Metadata?.Tags ?? [];
}

public sealed record MdParagraph(IReadOnlyList<MdInline> Content) : MdBlock;

public sealed record MdList(bool Ordered, IReadOnlyList<MdListItem> Items) : MdBlock;

/// <summary><see cref="Done"/> is null for a plain bullet, non-null for a
/// checklist item. <see cref="Children"/> holds the lists written underneath
/// this item, indented — normally one, but two when the nested run changes kind
/// partway down.</summary>
public sealed record MdListItem(
    bool? Done,
    IReadOnlyList<MdInline> Content,
    int? TaskIndex,
    IReadOnlyList<MdList>? Children = null)
{
    /// <summary>The nested lists, never null — so a renderer can loop without
    /// asking first.</summary>
    public IReadOnlyList<MdList> Nested => Children ?? [];
}

public sealed record MdQuote(IReadOnlyList<MdInline> Content) : MdBlock;

public sealed record MdCode(string Text, string Language = "") : MdBlock;

public sealed record MdDivider : MdBlock;

/// <summary>Which way a table column's text is set, from the <c>:</c> markers on
/// its delimiter cell.</summary>
public enum MdAlign
{
    Default,
    Left,
    Center,
    Right
}

public sealed record MdTableCell(IReadOnlyList<MdInline> Content);

public sealed record MdTableRow(IReadOnlyList<MdTableCell> Cells);

/// <summary>A table. <see cref="Alignment"/> has one entry per column of the
/// delimiter row, which is not necessarily one per cell of every row — a body
/// row is allowed to be short or long, and the renderer reads alignment
/// defensively rather than assuming they match.</summary>
public sealed record MdTable(
    MdTableRow Header,
    IReadOnlyList<MdTableRow> Rows,
    IReadOnlyList<MdAlign> Alignment) : MdBlock;

/// <summary>One note, with the number handed to it by the first reference that
/// pointed at it.</summary>
public sealed record MdFootnote(string Label, int Number, IReadOnlyList<MdInline> Content);

/// <summary>Every note in the body, collected at the end where a reader expects
/// to find them. Only produced when at least one reference actually resolved.</summary>
public sealed record MdFootnotes(IReadOnlyList<MdFootnote> Notes) : MdBlock;

public abstract record MdInline;

public sealed record MdText(string Text) : MdInline;

public sealed record MdStrong(string Text) : MdInline;

public sealed record MdEm(string Text) : MdInline;

public sealed record MdStrike(string Text) : MdInline;

public sealed record MdCodeSpan(string Text) : MdInline;

public sealed record MdTag(string Tag) : MdInline;

public sealed record MdLink(string Text, string Url) : MdInline;

public sealed record MdImage(string Alt, string Url) : MdInline;

/// <summary>A <c>[^label]</c> in the prose, and the number it was given.</summary>
public sealed record MdFootnoteRef(string Label, int Number) : MdInline;
