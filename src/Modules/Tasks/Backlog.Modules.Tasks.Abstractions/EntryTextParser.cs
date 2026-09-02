using System.Globalization;
using System.Text.RegularExpressions;
using Backlog.Modules.Tasks.Abstractions.DataTransferObjects;

namespace Backlog.Modules.Tasks.Abstractions;

/// <summary>
/// Reads and writes the plain-markdown format the quick-edit list is built on.
/// The shape of an entry is ordinary markdown a person would write anyway:
/// <code>
/// # Title
/// `task` `*high` `!in-progress` `@repos`
///
/// Free prose with #tags anywhere.
///
/// ## A sub-item
/// Notes for that sub-item.
///
/// - [ ] A checklist sub-item
/// </code>
/// Each kind of metadata carries its own sigil so a glance is enough to tell
/// them apart: <c>!</c> status, <c>*</c> priority, <c>@</c> area, <c>#</c> tag.
/// Type is the one bare word, because it is the noun the entry already is.
/// Bare words are still read for every kind, so entries written before the
/// sigils existed keep working; the canonical form written back uses sigils.
/// <para>
/// Heading level carries structure: a second <c>#</c> heading starts a whole
/// new entry (see <see cref="SplitSegments"/>), <c>##</c> headings and
/// <c>- [ ]</c> checklist lines both become sub-items. <c>###</c>
/// headings become nested sub-items with the same metadata as <c>##</c> chapters.
/// </para>
/// <para>
/// Deliberately independent from <c>Backlog.Infrastructure.FileSystem.EnumMap</c> (internal to
/// that assembly): the tokens here are the human-typed vocabulary shown in the
/// UI (e.g. <c>follow-up</c>, <c>in-progress</c>), normalized the same way
/// (case/space/hyphen/underscore-insensitive) so any spelling is recognized.
/// </para>
/// </summary>
public static class EntryTextParser
{
    private static readonly Regex MetaLineRegex = new(@"^(\s*`[^`\n]+`\s*)+$", RegexOptions.Compiled);
    private static readonly Regex TokenRegex = new(@"`([^`]+)`", RegexOptions.Compiled);
    private static readonly Regex TagRegex = new(@"(?<!\S)#([A-Za-z][\w-]*)", RegexOptions.Compiled);
    /// <summary>The entry title's own tag grammar, which is the body's plus a
    /// second sigil: <c>#name</c> is a general tag and <c>@name</c> is a person.
    /// Deliberately a separate expression from <see cref="TagRegex"/> rather than a
    /// widening of it — body prose does not learn <c>@</c>, so the two positions
    /// have to be able to disagree.
    /// <para>
    /// The <c>(?&lt;!\S)</c> guard is the load-bearing part: a sigil only opens a tag
    /// when nothing is welded to its left, which is what keeps
    /// <c>mail bob@example.com</c> from naming a person called
    /// <c>example</c>.
    /// </para></summary>
    private static readonly Regex TitleTagRegex = new(@"(?<!\S)([@#])([A-Za-z][\w-]*)", RegexOptions.Compiled);
    private static readonly Regex HeadingRegex = new(@"^(#{1,6})[ \t]+(.*)$", RegexOptions.Compiled);
    private static readonly Regex CheckboxPrefixRegex = new(@"^\[( |x|X)\][ \t]+", RegexOptions.Compiled);
    private static readonly Regex HeadingCheckboxMarkerRegex = new(@"^(?<prefix>[ \t]*#{2,3}[ \t]+)\[(?<marker> |x|X)\](?<suffix>[ \t]+.*)$", RegexOptions.Compiled);
    // The trailing `\S` is what keeps this in step with the read view's parser
    // (MarkdownPreview.TaskItemRegex, which trims the line end first): a line of
    // `- [ ]` followed by nothing but spaces is not a checklist item there, so it
    // must not be one here either, or the nth item each side counts is a
    // different line.
    private static readonly Regex ChecklistRegex = new(@"^[ \t]*[-*][ \t]+\[( |x|X)\][ \t]+(.*\S)[ \t]*$", RegexOptions.Compiled);
    // The interval form of a repeat: `2w`, `3m`. Deliberately anchored and
    // deliberately narrow — a `repeat:` value that is not one of the named
    // shapes or this is unrecognized, and unrecognized means the field stays
    // unset rather than the line failing.
    private static readonly Regex RepeatIntervalRegex = new(@"^(\d{1,4})([dwmy])$", RegexOptions.Compiled);

    /// <summary>Monday to Friday, which is what <c>repeat:weekdays</c> means. Held
    /// once because both the parser and the formatter have to agree on it, and
    /// "the working week" is a decision rather than a detail.</summary>
    private static readonly DayOfWeek[] WorkingWeek =
    [
        DayOfWeek.Monday,
        DayOfWeek.Tuesday,
        DayOfWeek.Wednesday,
        DayOfWeek.Thursday,
        DayOfWeek.Friday
    ];

    private static readonly Dictionary<string, EntryType> TypeTokens = new()
    {
        ["prompt"] = EntryType.Prompt,
        ["task"] = EntryType.Task,
        ["idea"] = EntryType.Idea,
        ["followup"] = EntryType.FollowUp
    };

    private static readonly Dictionary<string, Priority> PriorityTokens = new()
    {
        ["low"] = Priority.Low,
        ["medium"] = Priority.Medium,
        ["high"] = Priority.High,
        ["critical"] = Priority.Critical
    };

    private static readonly Dictionary<string, EntryStatus> StatusTokens = new()
    {
        ["draft"] = EntryStatus.Draft,
        ["ready"] = EntryStatus.Ready,
        ["inprogress"] = EntryStatus.InProgress,
        ["done"] = EntryStatus.Done,
        ["archived"] = EntryStatus.Archived
    };

    public sealed record ParsedSubItem(
        string Title,
        bool Done,
        string? Notes,
        int Level = 2,
        EntryType? Type = null,
        Priority? Priority = null,
        EntryStatus? Status = null,
        string? Area = null,
        IReadOnlyList<string>? MetadataTags = null);

    /// <summary>
    /// A named token whose kind was understood but whose value was not:
    /// <c>due:friday</c>, <c>repeat:fortnightly</c>. The field it named stays
    /// unset — parsing is tolerant, and refusing the whole line would lose the
    /// fields around it — but the words the reader typed are kept here so a
    /// surface showing what the text will be saved as can say the value was
    /// refused instead of showing nothing where a due date used to be.
    /// <para>
    /// Only the names this grammar knows are collected. A token whose
    /// <em>name</em> is unknown is unrecognized rather than refused, which is a
    /// different fact with a different rule behind it.
    /// </para>
    /// </summary>
    public sealed record UnreadableToken(string Name, string Value);

    /// <summary>
    /// What one segment of entry text says. The scheduling and dependency members
    /// carry defaults so that a caller only interested in the older fields still
    /// compiles — but a caller that <em>writes</em> an entry back must pass them,
    /// because <see cref="ToRawText"/> rebuilds the metadata line from fields
    /// alone and silently loses whatever it was not handed.
    /// </summary>
    public sealed record ParsedEntry(
        string Title,
        EntryType? Type,
        Priority? Priority,
        EntryStatus? Status,
        string? Area,
        string Body,
        IReadOnlyList<string> Tags,
        IReadOnlyList<string> MetadataTags,
        IReadOnlyList<ParsedSubItem> SubItems,
        DateOnly? DueOn = null,
        DateTime? RemindAt = null,
        Recurrence? Recurrence = null,
        DateOnly? InMyDayOn = null,
        IReadOnlyList<string>? DependsOn = null,
        IReadOnlyList<UnreadableToken>? Unreadable = null,
        EntryView? View = null,
        Attachment? Attachment = null,
        int? Effort = null,
        string? ImportItemId = null,
        IReadOnlyList<string>? RepoIds = null);

    private sealed record Metadata(
        EntryType? Type,
        Priority? Priority,
        EntryStatus? Status,
        string? Area,
        IReadOnlyList<string> Tags,
        DateOnly? DueOn = null,
        DateTime? RemindAt = null,
        Recurrence? Recurrence = null,
        DateOnly? InMyDayOn = null,
        IReadOnlyList<string>? DependsOn = null,
        IReadOnlyList<UnreadableToken>? Unreadable = null,
        EntryView? View = null,
        Attachment? Attachment = null,
        int? Effort = null,
        string? ImportItemId = null,
        IReadOnlyList<string>? RepoIds = null)
    {
        public static Metadata Empty { get; } = new(null, null, null, null, []);
    }

    /// <summary>
    /// Splits a block of text wherever a top-level <c>#</c> heading starts a new
    /// entry. The first heading is the current entry's own title, so only the
    /// second and later ones split. Headings inside fenced code are ignored.
    /// Returns a single segment when there is nothing to split.
    /// </summary>
    public static IReadOnlyList<string> SplitSegments(string raw)
    {
        var lines = Normalize(raw).Split('\n');
        var boundaries = new List<int>();
        var seenFirstContent = false;
        var inFence = false;

        for (var i = 0; i < lines.Length; i++)
        {
            var trimmed = lines[i].TrimStart();

            if (trimmed.StartsWith("```", StringComparison.Ordinal))
            {
                inFence = !inFence;
                seenFirstContent = true;
                continue;
            }

            if (inFence || trimmed.Length == 0) continue;

            var isTopHeading = trimmed.StartsWith("# ", StringComparison.Ordinal);

            // The very first line of content is the title, whether or not it is
            // written as a heading — it never splits.
            if (!seenFirstContent)
            {
                seenFirstContent = true;
                continue;
            }

            if (isTopHeading) boundaries.Add(i);
        }

        if (boundaries.Count == 0) return [raw ?? string.Empty];

        var segments = new List<string>();
        var start = 0;
        foreach (var boundary in boundaries)
        {
            segments.Add(string.Join('\n', lines[start..boundary]).Trim('\n'));
            start = boundary;
        }

        segments.Add(string.Join('\n', lines[start..]).Trim('\n'));
        return [.. segments.Where(s => s.Trim().Length > 0)];
    }

    /// <summary>Parses one segment. Tolerant by design: an unrecognized or
    /// missing meta token simply leaves that field unspecified (the caller keeps
    /// the previous value) rather than blocking or corrupting the rest of the
    /// edit.</summary>
    public static ParsedEntry Parse(string raw)
    {
        var lines = Normalize(raw).Split('\n');
        var i = 0;

        while (i < lines.Length && string.IsNullOrWhiteSpace(lines[i])) i++;

        var title = string.Empty;
        if (i < lines.Length)
        {
            var line = lines[i].Trim();

            // "# " with nothing typed after it yet trims down to a bare "#" —
            // still a heading marker carrying no title, not a one-character
            // title. Read literally, this line would come out as "#", which is
            // exactly the raw marker <see cref="EnsureTitleHeading"/> exists to
            // hide from a reader who never chose to type it.
            title = line.StartsWith("# ", StringComparison.Ordinal)
                ? line[2..].Trim()
                : line == "#" ? string.Empty : line;
            i++;
        }

        while (i < lines.Length && string.IsNullOrWhiteSpace(lines[i])) i++;

        var metadata = Metadata.Empty;

        if (i < lines.Length && MetaLineRegex.IsMatch(lines[i].Trim()))
        {
            metadata = ParseMetadataLine(lines[i]);
            i++;
        }

        var type = metadata.Type;
        var priority = metadata.Priority;
        var status = metadata.Status;
        var area = metadata.Area;
        var metadataTags = metadata.Tags.ToList();

        while (i < lines.Length && string.IsNullOrWhiteSpace(lines[i])) i++;

        var body = string.Join('\n', lines.Skip(i)).TrimEnd('\n');

        var bodyTags = TagRegex.Matches(StripFencedCode(body))
            .Select(m => m.Groups[1].Value.ToLowerInvariant())
            .ToList();

        // Tags typed into the title. Derived, exactly like the body's: the title
        // text stays verbatim and nothing here is ever written back on to the
        // metadata line, so `MetadataTags` still means "authored on the backtick
        // line" and remains the only thing ToRawText round-trips.
        //
        // Scanned off `title`, which has already had its own `# ` marker taken
        // off, so the heading marker can never read as a general tag. Sub-item
        // `##`/`###` headings live in the body and are not scanned by this at all.
        var titleTags = TitleTagRegex.Matches(title)
            .Select(m => (m.Groups[1].Value == "@" ? "@" : string.Empty) + m.Groups[2].Value.ToLowerInvariant())
            .ToList();

        var distinctMetadataTags = metadataTags.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var tags = distinctMetadataTags
            .Concat(titleTags)
            .Concat(bodyTags)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        return new ParsedEntry(
            title,
            type,
            priority,
            status,
            area,
            body,
            tags,
            distinctMetadataTags,
            ExtractSubItems(body, area),
            metadata.DueOn,
            metadata.RemindAt,
            metadata.Recurrence,
            metadata.InMyDayOn,
            metadata.DependsOn ?? [],
            metadata.Unreadable ?? [],
            metadata.View,
            metadata.Attachment,
            metadata.Effort,
            metadata.ImportItemId,
            metadata.RepoIds ?? []);
    }

    private static Metadata ParseMetadataLine(string line)
    {
        EntryType? type = null;
        Priority? priority = null;
        EntryStatus? status = null;
        string? area = null;
        var metadataTags = new List<string>();
        DateOnly? dueOn = null;
        DateTime? remindAt = null;
        Recurrence? recurrence = null;
        DateOnly? inMyDayOn = null;
        EntryView? view = null;
        Attachment? attachment = null;
        int? effort = null;
        var dependsOn = new List<string>();
        var unreadable = new List<UnreadableToken>();
        string? importItemId = null;
        var repoIds = new List<string>();

        foreach (Match match in TokenRegex.Matches(line))
        {
            var token = match.Groups[1].Value.Trim();
            if (token.Length == 0) continue;

            // Named `name:value` tokens are read before the sigils, because a
            // colon is a discriminator no bare type, priority or status word
            // contains. A token that already declared its kind with a sigil is
            // left to the switch below even if it holds a colon: the sigil said
            // what it is, and an area is free-form enough to contain one.
            if (token[0] is not ('!' or '*' or '@' or '#') && token.IndexOf(':', StringComparison.Ordinal) > 0)
            {
                var separator = token.IndexOf(':', StringComparison.Ordinal);
                var name = NormalizeToken(token[..separator]);
                var value = token[(separator + 1)..].Trim();

                switch (name)
                {
                    case "due":
                        if (TryParseDateToken(value, out var due)) dueOn = due;
                        else unreadable.Add(new UnreadableToken("due", value));
                        break;

                    case "remind":
                        if (TryParseReminderToken(value, out var remind)) remindAt = remind;
                        else unreadable.Add(new UnreadableToken("remind", value));
                        break;

                    case "repeat":
                        if (TryParseRepeatToken(value, out var repeat)) recurrence = repeat;
                        else unreadable.Add(new UnreadableToken("repeat", value));
                        break;

                    case "myday":
                        if (TryParseDateToken(value, out var myDay)) inMyDayOn = myDay;
                        else unreadable.Add(new UnreadableToken("myday", value));
                        break;

                    case "effort":
                        // A story-point size, kept tolerant like every other value
                        // on this line: NumberStyles.None rejects a sign and a
                        // decimal point, so `effort:-2` and `effort:abc` alike fall
                        // through and leave the field unset rather than throwing.
                        // A negative is refused here because the aggregate refuses
                        // it too — writing one down would only fail on the next
                        // save. Unlisted in the refused-token hint on purpose: the
                        // number is a convenience the picker fills, not a promise
                        // about the work a reader is waiting to see acknowledged.
                        if (int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var points))
                        {
                            effort = points;
                        }

                        break;

                    case "view":
                        // The one token on this line that is about the reader
                        // rather than about the work. It rides here anyway because
                        // the markdown is canonical — a preference kept anywhere
                        // else would not survive the file being shared — and it is
                        // refused the same way every other value is, so
                        // `view:kanban` leaves the field unset and says so.
                        if (TryParseViewToken(value, out var chosenView)) view = chosenView;
                        else unreadable.Add(new UnreadableToken("view", value));
                        break;

                    case "files":
                        // The one named token whose value is not a shape this
                        // grammar can check. A path is whatever the file system
                        // will take, and a parser that refused the ones it did not
                        // like would be a parser with an opinion about operating
                        // systems. Empty is the only malformed one there is:
                        // absent means absent, so `files:` with nothing after it
                        // is a reader asking for something rather than for
                        // nothing.
                        if (Attachment.From(value) is { } attached) attachment = attached;
                        else unreadable.Add(new UnreadableToken("files", value));
                        break;

                    case "after":
                        // Ids are opaque strings, so an id naming nothing still
                        // blocks and a malformed one round-trips. Only an exact
                        // repeat is dropped, because two mentions of the same
                        // predecessor are one dependency.
                        // An `after:` with nothing after the colon is the one
                        // malformed dependency there is: absent means absent, so
                        // an empty token is a reader asking for something rather
                        // than a reader asking for nothing.
                        if (value.Length == 0) unreadable.Add(new UnreadableToken("after", value));
                        else if (!dependsOn.Contains(value, StringComparer.Ordinal))
                        {
                            dependsOn.Add(value);
                        }

                        break;

                    case "id":
                        // A local id, last-one-wins like `due`: an entry names
                        // itself once, so a second `id:` on the same line replaces
                        // the first rather than being a second identity.
                        if (value.Length == 0) unreadable.Add(new UnreadableToken("id", value));
                        else importItemId = value;
                        break;

                    case "repo":
                        // Repeats like `after`, for the same reason: an entry can
                        // target more than one repository at once, and order
                        // carries no meaning.
                        if (value.Length == 0) unreadable.Add(new UnreadableToken("repo", value));
                        else if (!repoIds.Contains(value, StringComparer.Ordinal))
                        {
                            repoIds.Add(value);
                        }

                        break;
                }

                // An unrecognized name, and a recognized name whose value does
                // not parse, both fall through to here and leave that field
                // unset — exactly what an unknown sigil already does. Refusing
                // the whole line would lose the fields around it.
                continue;
            }

            switch (token[0])
            {
                case '@':
                {
                    var value = token[1..].Trim();
                    if (value.Length > 0) area = value.ToLowerInvariant();
                    continue;
                }

                case '!':
                    if (StatusTokens.TryGetValue(NormalizeToken(token[1..]), out var explicitStatus))
                    {
                        status = explicitStatus;
                    }
                    continue;

                case '*':
                    if (PriorityTokens.TryGetValue(NormalizeToken(token[1..]), out var explicitPriority))
                    {
                        priority = explicitPriority;
                    }
                    continue;

                case '#':
                {
                    var value = token[1..].Trim();
                    if (value.Length > 0) metadataTags.Add(value.ToLowerInvariant());
                    continue;
                }
            }

            var normalized = NormalizeToken(token);
            if (TypeTokens.TryGetValue(normalized, out var t)) type = t;
            else if (PriorityTokens.TryGetValue(normalized, out var p)) priority = p;
            else if (StatusTokens.TryGetValue(normalized, out var s)) status = s;
        }

        return new Metadata(
            type,
            priority,
            status,
            area,
            metadataTags.Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
            dueOn,
            remindAt,
            recurrence,
            inMyDayOn,
            dependsOn,
            unreadable,
            view,
            attachment,
            effort,
            importItemId,
            repoIds);
    }

    /// <summary>Blanks out fenced code so it cannot contribute tags. Structure
    /// already ignores fences; a <c>#tag</c> written inside a code sample is
    /// text for the same reason a <c>#</c> heading there is.</summary>
    private static string StripFencedCode(string body)
    {
        if (!body.Contains("```", StringComparison.Ordinal)) return body;

        var lines = body.Split('\n');
        var inFence = false;

        for (var i = 0; i < lines.Length; i++)
        {
            if (lines[i].TrimStart().StartsWith("```", StringComparison.Ordinal))
            {
                inFence = !inFence;
                lines[i] = string.Empty;
                continue;
            }

            if (inFence) lines[i] = string.Empty;
        }

        return string.Join('\n', lines);
    }

    /// <summary>Collects sub-items from a body, in document order. A <c>##</c>
    /// or <c>###</c> heading becomes a sub-item whose notes are the prose beneath
    /// it; a <c>- [ ]</c> line becomes a standalone checklist sub-item. Heading
    /// metadata is read like the parent entry metadata, except repository/area
    /// always inherits from the parent.</summary>
    private static List<ParsedSubItem> ExtractSubItems(string body, string? parentArea)
    {
        var items = new List<ParsedSubItem>();
        var lines = body.Split('\n');
        var inFence = false;

        string? openTitle = null;
        var openDone = false;
        var openLevel = 2;
        var openMetadata = Metadata.Empty;
        var openNotes = new List<string>();

        void CloseOpen()
        {
            if (openTitle is null) return;
            var notes = string.Join('\n', openNotes).Trim('\n', ' ', '\t');
            var status = openMetadata.Status ?? (openDone ? EntryStatus.Done : null);
            items.Add(new ParsedSubItem(
                openTitle,
                status is EntryStatus.Done || openDone,
                notes.Length == 0 ? null : notes,
                openLevel,
                openMetadata.Type,
                openMetadata.Priority,
                status,
                parentArea,
                openMetadata.Tags));
            openTitle = null;
            openDone = false;
            openLevel = 2;
            openMetadata = Metadata.Empty;
            openNotes.Clear();
        }

        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i];
            var trimmed = line.TrimStart();

            if (trimmed.StartsWith("```", StringComparison.Ordinal))
            {
                inFence = !inFence;
                if (openTitle is not null) openNotes.Add(line);
                continue;
            }

            if (inFence)
            {
                if (openTitle is not null) openNotes.Add(line);
                continue;
            }

            var checklist = ChecklistRegex.Match(line);
            if (checklist.Success)
            {
                CloseOpen();
                items.Add(new ParsedSubItem(
                    checklist.Groups[2].Value.Trim(),
                    checklist.Groups[1].Value is "x" or "X",
                    Notes: null,
                    Area: parentArea,
                    MetadataTags: []));
                continue;
            }

            var heading = HeadingRegex.Match(trimmed);
            if (heading.Success)
            {
                var level = heading.Groups[1].Value.Length;
                if (level is 2 or 3)
                {
                    CloseOpen();

                    var text = heading.Groups[2].Value.Trim();
                    var box = CheckboxPrefixRegex.Match(text);
                    if (box.Success)
                    {
                        openDone = box.Groups[1].Value is "x" or "X";
                        text = text[box.Length..].Trim();
                    }

                    if (text.Length == 0) continue;

                    openTitle = text;
                    openLevel = level;

                    var metaIndex = i + 1;
                    if (metaIndex < lines.Length && MetaLineRegex.IsMatch(lines[metaIndex].Trim()))
                    {
                        openMetadata = ParseMetadataLine(lines[metaIndex]) with { Area = parentArea };
                        i = metaIndex;
                    }
                    continue;
                }

                if (level <= 1)
                {
                    CloseOpen();
                    continue;
                }
            }

            if (openTitle is not null) openNotes.Add(line);
        }

        CloseOpen();
        return items;
    }

    /// <summary>The lines one sub-item occupies in an entry's raw text —
    /// <paramref name="End"/> is exclusive.</summary>
    public sealed record SubItemSpan(int Start, int End);

    private sealed record LocatedSubItem(SubItemSpan Span, int Level);

    /// <summary>
    /// Finds the line range of every <c>##</c> and <c>###</c> sub-item in a block
    /// of raw entry text. A sub-item owns everything written after its heading up
    /// to the next heading of the same level or higher. Headings inside fenced
    /// code are text, not structure.
    /// </summary>
    public static IReadOnlyList<SubItemSpan> LocateSubItems(string raw) =>
        LocateSubItemDetails(raw).Select(item => item.Span).ToList();

    private static List<LocatedSubItem> LocateSubItemDetails(string raw)
    {
        var lines = Normalize(raw).Split('\n');
        var starts = new List<int>();
        var ends = new List<int>();
        var levels = new List<int>();
        var inFence = false;

        for (var i = 0; i < lines.Length; i++)
        {
            var trimmed = lines[i].TrimStart();

            if (trimmed.StartsWith("```", StringComparison.Ordinal))
            {
                inFence = !inFence;
                continue;
            }

            if (inFence) continue;

            var heading = HeadingRegex.Match(trimmed);
            if (!heading.Success) continue;

            var level = heading.Groups[1].Value.Length;
            if (level is not (2 or 3))
            {
                if (level <= 1 && starts.Count > ends.Count) ends.Add(i);
                continue;
            }

            if (starts.Count > ends.Count) ends.Add(i);
            starts.Add(i);
            levels.Add(level);
        }

        if (starts.Count > ends.Count) ends.Add(lines.Length);

        var spans = new List<LocatedSubItem>(starts.Count);
        for (var i = 0; i < starts.Count; i++)
        {
            var end = ends[i];
            while (end > starts[i] + 1 && string.IsNullOrWhiteSpace(lines[end - 1])) end--;
            spans.Add(new LocatedSubItem(new SubItemSpan(starts[i], end), levels[i]));
        }

        return spans;
    }

    /// <summary>
    /// The part of an entry that is the entry itself rather than one of its
    /// sub-items — what the entry's own editor shows.
    /// <para>
    /// <paramref name="subItemCount"/> is how many sub-items the entry had when
    /// that editor was opened, and it is what keeps typing a <c>##</c> heading
    /// into the editor from being read straight back as a sub-item: the entry
    /// keeps its last <paramref name="subItemCount"/> chapters and everything
    /// written above them stays in hand. Pass <c>-1</c> when there is no editor
    /// open and the first sub-item is the honest boundary.
    /// </para>
    /// </summary>
    public static string GetParentText(string raw, int subItemCount = -1)
    {
        var normalizedRaw = Normalize(raw);
        var lines = normalizedRaw.Split('\n');
        var start = ChildStartLine(LocateSubItems(normalizedRaw), subItemCount);
        if (start < 0) return raw ?? string.Empty;

        var end = start;
        while (end > 0 && string.IsNullOrWhiteSpace(lines[end - 1])) end--;
        return string.Join('\n', lines[..end]);
    }

    /// <summary>Writes <paramref name="value"/> back as the entry's own text,
    /// leaving its sub-items where they are. See <see cref="GetParentText"/> for
    /// what <paramref name="subItemCount"/> is for — the two have to agree, or
    /// the text an editor hands back is not the text it was given.</summary>
    public static string ReplaceParentText(string raw, string value, int subItemCount = -1)
    {
        var normalizedRaw = Normalize(raw);
        var lines = normalizedRaw.Split('\n');
        var start = ChildStartLine(LocateSubItems(normalizedRaw), subItemCount);
        if (start < 0) return value ?? string.Empty;

        var parent = Normalize(value).TrimEnd('\n');
        var children = string.Join('\n', lines[start..]).TrimStart('\n');
        if (parent.Length == 0) return children;
        if (children.Length == 0) return parent;

        return parent + "\n\n" + children;
    }

    /// <summary>How many sub-item chapters a block of entry text holds, counting
    /// nested ones. This is the number <see cref="GetParentText"/> and
    /// <see cref="ReplaceParentText"/> want.</summary>
    public static int CountSubItems(string raw) => LocateSubItems(raw).Count;

    // The detail pane edits an entry a field at a time — a title, a note, one
    // step's name, one step's notes — where the raw editor used to hand over the
    // whole document. Every one of these is still a text rewrite, and the text is
    // still the entry: a field-shaped control whose write did not end up here
    // would be a second source of truth, which is the one thing this format
    // exists to rule out.

    /// <summary>
    /// Writes a new title on to the entry and leaves the rest of it alone.
    /// <para>
    /// Always as a <c>#</c> heading, because that is what the first line becomes
    /// on save anyway (see <see cref="Parse"/>): writing it bare would only mean
    /// the next save rewrote the line the reader had just typed.
    /// </para>
    /// <para>
    /// An empty title is written as an empty heading rather than refused.
    /// Refusing belongs to whatever is listening — the shared task row never
    /// raises an empty rename — and a rewrite that silently kept the old title
    /// would be one whose output does not match its input.
    /// </para>
    /// </summary>
    public static string WithTitle(string raw, string title)
    {
        var heading = "# " + (title ?? string.Empty).Trim();
        var lines = Normalize(raw).Split('\n').ToList();
        var titleIndex = FirstContentLine(lines);

        if (titleIndex < 0) return heading;

        lines[titleIndex] = heading;
        return string.Join('\n', lines);
    }

    /// <summary>
    /// The entry's own prose: everything below the title and the metadata line and
    /// above the first sub-item.
    /// <para>
    /// The note, in other words — what a reader wrote about the entry itself
    /// rather than about one of its steps. Deliberately not
    /// <see cref="ParsedEntry.Body"/>, which starts in the same place but runs to
    /// the end of the document and so takes every sub-item chapter with it.
    /// </para>
    /// </summary>
    public static string GetNote(string raw)
    {
        var lines = Normalize(GetParentText(raw)).Split('\n').ToList();
        var start = NoteStartLine(lines);

        return start < 0 || start >= lines.Count
            ? string.Empty
            : string.Join('\n', lines.Skip(start)).Trim('\n');
    }

    // WithNote was here, the writer for that same region. It went with its only
    // caller: the surface that wrote a note is now a block over the whole body, and
    // a note-scoped writer left standing would be a supported-looking way to discard
    // half of one. NoteStartLine below is still shared with GetNote and with the
    // sub-item note writer.

    /// <summary>Where a note begins inside a block of chapter text, or -1 when
    /// there is nothing but a heading. The heading comes first, then an optional
    /// metadata line; everything after those is the note. One function for the
    /// entry and for a sub-item, because both chapters have that same shape.</summary>
    private static int NoteStartLine(IReadOnlyList<string> lines)
    {
        var index = FirstContentLine(lines);
        if (index < 0) return -1;

        index++;
        while (index < lines.Count && string.IsNullOrWhiteSpace(lines[index])) index++;

        if (index < lines.Count && MetaLineRegex.IsMatch(lines[index].Trim())) index++;
        while (index < lines.Count && string.IsNullOrWhiteSpace(lines[index])) index++;

        return index;
    }

    /// <summary>One sub-item's heading text, without its hashes and without the
    /// checkbox marker a done one carries. Indexed exactly as
    /// <see cref="LocateSubItems"/> and <see cref="ToggleSubItem"/> index it — the
    /// nth chapter, and a checklist line is not one.</summary>
    public static string GetSubItemTitle(string raw, int subItemIndex)
    {
        var text = GetSubItemText(raw, subItemIndex);
        if (text.Length == 0) return string.Empty;

        var heading = HeadingRegex.Match(Normalize(text).Split('\n')[0].TrimStart());
        if (!heading.Success) return string.Empty;

        var title = heading.Groups[2].Value.Trim();
        var box = CheckboxPrefixRegex.Match(title);

        return box.Success ? title[box.Length..].Trim() : title;
    }

    /// <summary>Renames one sub-item, keeping its heading level and its checkbox
    /// marker. Neither is anything the rename asked about: a step does not change
    /// depth or come undone because somebody fixed a typo in its name.</summary>
    public static string WithSubItemTitle(string raw, int subItemIndex, string title)
    {
        var text = GetSubItemText(raw, subItemIndex);
        if (text.Length == 0) return raw;

        var lines = Normalize(text).Split('\n').ToList();
        var heading = HeadingRegex.Match(lines[0].TrimStart());
        if (!heading.Success) return raw;

        var marker = HeadingCheckboxMarkerRegex.Match(lines[0]);
        var hashes = heading.Groups[1].Value;
        var renamed = (title ?? string.Empty).Trim();

        lines[0] = marker.Success
            ? $"{hashes} [{marker.Groups["marker"].Value}] {renamed}"
            : $"{hashes} {renamed}";

        return ReplaceSubItemText(raw, subItemIndex, string.Join('\n', lines));
    }

    /// <summary>One sub-item's notes: what is written under its heading, past its
    /// own metadata line when it has one.</summary>
    public static string GetSubItemNote(string raw, int subItemIndex)
    {
        var text = GetSubItemText(raw, subItemIndex);
        if (text.Length == 0) return string.Empty;

        var lines = Normalize(text).Split('\n').ToList();
        var start = NoteStartLine(lines);

        return start < 0 || start >= lines.Count
            ? string.Empty
            : string.Join('\n', lines.Skip(start)).Trim('\n');
    }

    /// <summary>Writes one sub-item's notes back, keeping its heading and its own
    /// metadata line. The bargain <see cref="WithNote"/> makes about the entry,
    /// one level down.</summary>
    public static string WithSubItemNote(string raw, int subItemIndex, string note)
    {
        var text = GetSubItemText(raw, subItemIndex);
        if (text.Length == 0) return raw;

        var lines = Normalize(text).Split('\n').ToList();
        var start = NoteStartLine(lines);

        var head = start < 0 ? lines : lines.Take(start).ToList();
        while (head.Count > 0 && string.IsNullOrWhiteSpace(head[^1])) head.RemoveAt(head.Count - 1);

        var body = Normalize(note ?? string.Empty).Trim('\n');

        // One newline, not the blank line the entry's note gets. A sub-item chapter
        // is written `## Heading` then its notes on the next line — that is what
        // MoveSubItem rebuilds and what every entry in the store already holds — and
        // inserting a blank line here would be exactly the "gratuitous whitespace
        // churn" .design/content-editing.md#round-trip-fidelity rules out, on every
        // step of every entry somebody edited the notes of.
        var replacement = body.Length == 0
            ? string.Join('\n', head)
            : string.Join('\n', head) + "\n" + body;

        return ReplaceSubItemText(raw, subItemIndex, replacement);
    }

    /// <summary>
    /// Adds a step at the end of the entry, as a <c>##</c> chapter.
    /// <para>
    /// At the end because that is where the reader just looked: the add row sits
    /// under the last step, and a step that appeared above the ones already there
    /// would be a step nobody saw arrive. At level two because nesting one under
    /// another is a move, and moving it is a gesture the list already offers.
    /// </para>
    /// <para>
    /// An empty title adds nothing. There is no such thing as a step with no name,
    /// and a bare <c>##</c> would leave a chapter the reader cannot see, select or
    /// delete.
    /// </para>
    /// </summary>
    public static string AppendSubItem(string raw, string title)
    {
        var trimmed = (title ?? string.Empty).Trim();
        if (trimmed.Length == 0) return raw;

        var normalized = Normalize(raw).TrimEnd('\n');
        var heading = "## " + trimmed;

        return normalized.Length == 0 ? heading : normalized + "\n\n" + heading;
    }

    /// <summary>Where an entry's sub-items begin, or -1 when it has none to
    /// keep. Sub-items typed into the entry's own editor arrive above the ones
    /// that were already there, so holding on to the last
    /// <paramref name="subItemCount"/> of them is what tells the two apart.</summary>
    private static int ChildStartLine(IReadOnlyList<SubItemSpan> spans, int subItemCount)
    {
        if (spans.Count == 0 || subItemCount == 0) return -1;
        if (subItemCount < 0 || spans.Count <= subItemCount) return spans[0].Start;

        return spans[spans.Count - subItemCount].Start;
    }

    /// <summary>
    /// Rewrites raw entry text with the sub-item at <paramref name="from"/> moved
    /// to index <paramref name="to"/>. Reordering is done on the text itself
    /// rather than on the structured sub-items, because the text is the source of
    /// truth — anything else would be undone by the next save.
    /// </summary>
    public static string MoveSubItem(string raw, int from, int to)
    {
        var normalized = Normalize(raw);
        var items = LocateSubItemDetails(normalized);

        if (from == to || from < 0 || to < 0 || from >= items.Count || to >= items.Count) return raw;

        var lines = normalized.Split('\n');
        var blocks = items.Select(item => lines[item.Span.Start..item.Span.End]).ToList();

        if (items[from].Level == 2)
        {
            var groupEnd = from + 1;
            while (groupEnd < items.Count && items[groupEnd].Level > 2)
            {
                groupEnd++;
            }

            if (to >= from && to < groupEnd) return raw;

            var groupSize = groupEnd - from;
            var movedGroup = blocks.GetRange(from, groupSize);
            blocks.RemoveRange(from, groupSize);

            var insertAt = to > from ? to - groupSize + 1 : to;
            blocks.InsertRange(insertAt, movedGroup);
            return RebuildSubItemText(lines, items[0].Span.Start, blocks);
        }

        var parentIndex = from - 1;
        while (parentIndex >= 0 && items[parentIndex].Level != 2)
        {
            parentIndex--;
        }

        if (parentIndex < 0) return raw;

        var maxIndex = parentIndex;
        while (maxIndex + 1 < items.Count && items[maxIndex + 1].Level > 2)
        {
            maxIndex++;
        }

        var minChildIndex = parentIndex + 1;
        if (to < minChildIndex || to > maxIndex) return raw;

        var moved = blocks[from];
        blocks.RemoveAt(from);
        blocks.Insert(to, moved);
        return RebuildSubItemText(lines, items[0].Span.Start, blocks);
    }

    /// <summary>
    /// Rewrites raw entry text with the sub-item at <paramref name="subItemIndex"/>
    /// taken out of it.
    /// <para>
    /// The whole chapter goes: its heading, its own metadata line, and its notes.
    /// Those are not things the step was written above — they are the step, and a
    /// removal that left the notes behind would leave them under whichever chapter
    /// now precedes them, which is a re-parenting nobody asked for.
    /// </para>
    /// <para>
    /// A <c>##</c> chapter takes its <c>###</c> children with it, on exactly the
    /// terms <see cref="MoveSubItem"/> moves them: a nested step is written under
    /// the one it belongs to, so orphaning it would either re-parent it or promote
    /// it to the top of the list. A <c>###</c> step owns nothing below it and so
    /// goes alone.
    /// </para>
    /// <para>
    /// An index that names no chapter leaves the text exactly as it was, which is
    /// the answer <see cref="MoveSubItem"/> and <see cref="ToggleSubItem"/> both
    /// give: a rewrite that cannot find its subject has nothing to say about the
    /// document.
    /// </para>
    /// <para>
    /// The prose in front of the first chapter is untouched, and the chapters that
    /// remain keep their order and their own notes, because the rebuild is the one
    /// <see cref="MoveSubItem"/> uses — same head, same single blank line between
    /// blocks, same trailing newline.
    /// </para>
    /// </summary>
    public static string RemoveSubItem(string raw, int subItemIndex)
    {
        var normalized = Normalize(raw);
        var items = LocateSubItemDetails(normalized);

        if (subItemIndex < 0 || subItemIndex >= items.Count) return raw;

        var lines = normalized.Split('\n');
        var blocks = items.Select(item => lines[item.Span.Start..item.Span.End]).ToList();

        var groupEnd = subItemIndex + 1;
        if (items[subItemIndex].Level == 2)
        {
            while (groupEnd < items.Count && items[groupEnd].Level > 2)
            {
                groupEnd++;
            }
        }

        blocks.RemoveRange(subItemIndex, groupEnd - subItemIndex);

        return RebuildSubItemText(lines, items[0].Span.Start, blocks);
    }

    /// <summary>Toggles the nth markdown checklist item in an entry, ignoring
    /// fenced code blocks. The raw markdown remains the source of truth; the read
    /// view only asks for this text rewrite.</summary>
    public static string ToggleChecklistItem(string raw, int taskIndex)
    {
        if (taskIndex < 0) return raw;

        var normalized = Normalize(raw);
        var lines = normalized.Split('\n');
        var inFence = false;
        var seen = 0;

        for (var i = 0; i < lines.Length; i++)
        {
            var trimmed = lines[i].TrimStart();
            if (trimmed.StartsWith("```", StringComparison.Ordinal))
            {
                inFence = !inFence;
                continue;
            }

            if (inFence) continue;

            var checklist = ChecklistRegex.Match(lines[i]);
            if (!checklist.Success) continue;

            if (seen++ != taskIndex) continue;

            var marker = checklist.Groups[1];
            var replacement = marker.Value is "x" or "X" ? " " : "x";
            lines[i] = lines[i][..marker.Index] + replacement + lines[i][(marker.Index + marker.Length)..];
            return string.Join('\n', lines);
        }

        return raw;
    }

    /// <summary>Toggles the checkbox on the nth rendered sub-item heading,
    /// ignoring fenced code blocks. Sub-items without a checkbox marker are left
    /// untouched rather than inventing state the markdown did not carry.</summary>
    public static string ToggleSubItem(string raw, int subItemIndex)
    {
        if (subItemIndex < 0) return raw;

        var normalized = Normalize(raw);
        var lines = normalized.Split('\n');
        var inFence = false;
        var seen = 0;

        for (var i = 0; i < lines.Length; i++)
        {
            var trimmed = lines[i].TrimStart();
            if (trimmed.StartsWith("```", StringComparison.Ordinal))
            {
                inFence = !inFence;
                continue;
            }

            if (inFence) continue;

            var heading = HeadingRegex.Match(trimmed);
            if (!heading.Success || heading.Groups[1].Value.Length is not (2 or 3)) continue;

            if (seen++ != subItemIndex) continue;

            var checkbox = HeadingCheckboxMarkerRegex.Match(lines[i]);
            if (!checkbox.Success) return raw;

            var marker = checkbox.Groups["marker"];
            var replacement = marker.Value is "x" or "X" ? " " : "x";
            lines[i] = lines[i][..marker.Index] + replacement + lines[i][(marker.Index + marker.Length)..];
            return string.Join('\n', lines);
        }

        return raw;
    }

    private static string RebuildSubItemText(string[] lines, int firstSubItemStart, List<string[]> blocks)
    {
        var rebuilt = new List<string>(lines[..firstSubItemStart]);
        while (rebuilt.Count > 0 && string.IsNullOrWhiteSpace(rebuilt[^1]))
        {
            rebuilt.RemoveAt(rebuilt.Count - 1);
        }

        foreach (var block in blocks)
        {
            if (rebuilt.Count > 0) rebuilt.Add(string.Empty);
            rebuilt.AddRange(block);
        }

        return string.Join('\n', rebuilt) + "\n";
    }

    /// <summary>Builds the canonical raw-text form of an entry — the inverse of
    /// <see cref="Parse"/> — so the editor always reflects exactly what was
    /// saved. Sub-items need no special handling: they are already written as
    /// <c>##</c> headings inside the body.</summary>
    public static string ToRawText(TaskItemDto entry)
    {
        ArgumentNullException.ThrowIfNull(entry);

        var meta = $"`{TypeToken(entry.Type)}` `*{PriorityToken(entry.Priority)}` `!{StatusToken(entry.Status)}`";
        if (!string.IsNullOrWhiteSpace(entry.Area)) meta += $" `@{entry.Area}`";
        // Person tags are skipped, not written. This loop composes `#{tag}`, so an
        // `@bob` arriving here would be emitted as the token `#@bob` — which is
        // not a tag in any grammar, and sits on the one line where a bare `@`
        // already means Area. Nothing is lost by leaving them off: a person tag is
        // derived from the title, the title is preserved verbatim, and the next
        // parse reads it straight back off the text that is still there.
        foreach (var tag in entry.Tags
            .Select(tag => tag.Trim())
            .Where(tag => !IsPersonTag(tag))
            .Select(tag => tag.TrimStart('#'))
            .Where(tag => tag.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase))
        {
            meta += $" `#{tag.ToLowerInvariant()}`";
        }

        // The sigils first, then the named tokens in their canonical order. This
        // is the destructive half of the round trip: the line is composed from
        // the DTO and nothing else, so a field the DTO does not carry is gone
        // after the next flush save with no error anywhere to notice it by.
        if (entry.DueOn is { } dueOn) meta += $" `due:{DateToken(dueOn)}`";
        if (entry.RemindAt is { } remindAt) meta += $" `remind:{ReminderToken(remindAt)}`";
        if (entry.Recurrence is { } recurrence) meta += $" `repeat:{RepeatToken(recurrence)}`";
        if (entry.InMyDayOn is { } inMyDayOn) meta += $" `myday:{DateToken(inMyDayOn)}`";
        foreach (var id in (entry.DependsOn ?? []).Where(id => !string.IsNullOrWhiteSpace(id)))
        {
            meta += $" `after:{id.Trim()}`";
        }

        // A local id names this entry the way `after:` names one it waits on —
        // both are read the same document round, so they sit together.
        if (!string.IsNullOrWhiteSpace(entry.ImportItemId)) meta += $" `id:{entry.ImportItemId}`";
        foreach (var repo in (entry.RepoIds ?? []).Where(r => !string.IsNullOrWhiteSpace(r)))
        {
            meta += $" `repo:{repo}`";
        }

        // After the scheduling and dependency tokens and before the two that are
        // not about the work itself. A size sits with the facts about the task —
        // when it is due, what it waits on — rather than with what is attached to
        // it or how it is read, and pinning it to one fixed slot is what makes a
        // parse→write→parse round trip land the line back exactly as it was.
        if (entry.Effort is { } effort) meta += $" `effort:{effort.ToString(CultureInfo.InvariantCulture)}`";

        // Before the view token, because what is attached is a fact about the
        // work and the view is not: every token about the task comes first, and
        // the one about the reader comes last.
        if (entry.Attachment is { } attachment) meta += $" `files:{attachment.Path}`";

        // Last, after the dependencies, because it is the only token here that is
        // about how the entry is read rather than about the work — and an entry
        // nobody expressed a preference about acquires no token by being saved.
        if (entry.View is { } view) meta += $" `view:{ViewToken(view)}`";

        var body = entry.Body.TrimEnd('\n');
        return body.Length == 0
            ? $"# {entry.Title}\n{meta}\n"
            : $"# {entry.Title}\n{meta}\n\n{body}\n";
    }

    public static string WithType(string raw, EntryType type) =>
        RewriteMetaLine(raw, type: type);

    public static string WithPriority(string raw, Priority priority) =>
        RewriteMetaLine(raw, priority: priority);

    public static string WithStatus(string raw, EntryStatus status, bool cascadeSubItems = false)
    {
        var rewritten = RewriteMetaLine(raw, status: status);
        return cascadeSubItems ? RewriteSubItemMetaLines(rewritten, targetIndex: null, status: status) : rewritten;
    }

    /// <summary>Marks one sub-item chapter's status. Status is the only piece of a
    /// sub-item's metadata line this writes: a sub-item carries a title, a status,
    /// notes and an order, and a type or priority written on to one was discarded
    /// by the aggregate on the next save rather than kept.</summary>
    public static string WithSubItemStatus(string raw, int subItemIndex, EntryStatus status) =>
        RewriteSubItemMetaLines(raw, subItemIndex, status: status);

    public static string GetSubItemText(string raw, int subItemIndex)
    {
        if (subItemIndex < 0) return string.Empty;

        var normalized = Normalize(raw);
        var lines = normalized.Split('\n');
        var spans = LocateSubItems(normalized);
        if (subItemIndex >= spans.Count) return string.Empty;

        var span = spans[subItemIndex];
        return string.Join('\n', lines.Skip(span.Start).Take(span.End - span.Start));
    }

    public static string ReplaceSubItemText(string raw, int subItemIndex, string replacement)
    {
        if (subItemIndex < 0) return raw;

        var normalized = Normalize(raw);
        var lines = normalized.Split('\n').ToList();
        var spans = LocateSubItems(normalized);
        if (subItemIndex >= spans.Count) return raw;

        var span = spans[subItemIndex];
        var replacementLines = Normalize(replacement).Split('\n').ToList();
        lines.RemoveRange(span.Start, span.End - span.Start);
        lines.InsertRange(span.Start, replacementLines);
        return string.Join('\n', lines);
    }
    public static string WithArea(string raw, string? area) =>
        RewriteMetaLine(raw, area: area, updateArea: true);

    public static string WithTags(string raw, IEnumerable<string> tags) =>
        RewriteMetaLine(raw, tags: NormalizeTags(tags));

    public static string WithTags(string raw, string tags) =>
        RewriteMetaLine(raw, tags: ParseTagsInput(tags));

    /// <summary>Writes a due date on to the metadata line, or clears it when
    /// <paramref name="dueOn"/> is null. Clearing has to be expressible: an
    /// unset field carries no token rather than an empty one, so removing the
    /// date and removing the token are the same gesture.</summary>
    public static string WithDue(string raw, DateOnly? dueOn) =>
        RewriteMetaLine(raw, dueOn: dueOn, updateDue: true);

    /// <summary>Writes a reminder on to the metadata line, or clears it. The
    /// value is wall-clock intent, so whatever <see cref="DateTime.Kind"/> it
    /// arrives with is dropped on the way to the token.</summary>
    public static string WithReminder(string raw, DateTime? remindAt) =>
        RewriteMetaLine(raw, remindAt: remindAt, updateReminder: true);

    /// <summary>Writes a repeat on to the metadata line, or clears it.</summary>
    public static string WithRepeat(string raw, Recurrence? recurrence) =>
        RewriteMetaLine(raw, recurrence: recurrence, updateRepeat: true);

    /// <summary>Stamps the entry for a particular day's My Day, or clears the
    /// stamp. Taking it out of My Day is clearing the date, not writing
    /// today's.</summary>
    public static string WithMyDay(string raw, DateOnly? inMyDayOn) =>
        RewriteMetaLine(raw, inMyDayOn: inMyDayOn, updateMyDay: true);

    /// <summary>Rewrites the whole set of <c>after:</c> tokens. An empty list
    /// clears them: the ids are the dependency, so there is nothing left to say
    /// once they are gone.</summary>
    public static string WithDependsOn(string raw, IEnumerable<string>? dependsOn) =>
        RewriteMetaLine(raw, dependsOn: NormalizeDependsOn(dependsOn), updateDependsOn: true);

    /// <summary>Rewrites the whole set of <c>repo:</c> tokens. A set rather than a
    /// value because an entry may target several repositories at once
    /// (<c>.domain/backlog/features.md#multi-repo-targeting</c>), and an empty list
    /// clears them: an entry that targets nothing carries no token rather than an
    /// empty one, the rule every other named field here follows. Deliberately
    /// unrelated to <c>@area</c> — the area is the person's own pile and says
    /// nothing about repositories (<c>.domain/backlog/naming.md#area</c>).</summary>
    public static string WithRepoIds(string raw, IEnumerable<string>? repoIds) =>
        RewriteMetaLine(raw, repoIds: NormalizeRepoIds(repoIds), updateRepoIds: true);

    /// <summary>Writes a story-point estimate on to the metadata line, or clears
    /// it when <paramref name="effort"/> is null. Clearing has to be expressible
    /// for the reason it is on every other named token: "not estimated" and "zero
    /// points" are different facts, and only one of them should survive into an
    /// entry nobody has sized — so removing the estimate removes the token rather
    /// than writing a zero.</summary>
    public static string WithEffort(string raw, int? effort) =>
        RewriteMetaLine(raw, effort: effort, updateEffort: true);

    /// <summary>Records which reading of the body the reader asked for, or clears
    /// the token. Clearing is expressible for the same reason it is on every other
    /// named token: "no preference" and "prefers the steps" are different facts,
    /// and only one of them should survive into an entry nobody chose for.</summary>
    public static string WithView(string raw, EntryView? view) =>
        RewriteMetaLine(raw, view: view, updateView: true);

    /// <summary>Writes the attached place on to the metadata line, or clears it.
    /// Detaching and attaching nothing are the same gesture, for the reason every
    /// other field here gives: an unset field carries no token rather than an
    /// empty one.</summary>
    public static string WithAttachment(string raw, Attachment? attachment) =>
        RewriteMetaLine(raw, attachment: attachment, updateAttachment: true);

    /// <summary>
    /// Replaces the whole body, keeping the title and the metadata line exactly as
    /// written.
    /// <para>
    /// A note used to be written separately, scoped to the prose before the first
    /// sub-item; this is everything after the metadata line —
    /// prose and <c>##</c> chapters together. It exists because the markdown block
    /// in the detail pane is a view of the body rather than of a slice of it, and a
    /// block that could only write the prose half would silently discard the steps
    /// somebody typed into it.
    /// </para>
    /// </summary>
    public static string WithBody(string raw, string body)
    {
        var normalized = Normalize(raw);
        var lines = normalized.Split('\n').ToList();
        var titleIndex = FirstContentLine(lines);
        if (titleIndex < 0) return raw;

        var metaIndex = titleIndex + 1;
        while (metaIndex < lines.Count && string.IsNullOrWhiteSpace(lines[metaIndex])) metaIndex++;

        var headEnd = metaIndex < lines.Count && MetaLineRegex.IsMatch(lines[metaIndex].Trim())
            ? metaIndex + 1
            : titleIndex + 1;

        var head = string.Join('\n', lines.Take(headEnd));
        var replacement = Normalize(body ?? string.Empty).Trim('\n');

        return replacement.Length == 0 ? head + "\n" : head + "\n\n" + replacement + "\n";
    }

    public static bool IsMetadataLine(string line) => MetaLineRegex.IsMatch((line ?? string.Empty).Trim());

    public static IReadOnlyList<string> ParseTagsInput(string tags) =>
        NormalizeTags(Regex.Split(tags ?? string.Empty, @"[\s,]+"));

    public static string FormatTagsInput(IEnumerable<string> tags) => string.Join(" ", NormalizeTags(tags));

    /// <summary>Whether a stored tag names a person. The sigil is kept in the
    /// stored value — <c>["@bob", "deploy"]</c> — precisely so that an existing
    /// unsigilled tag keeps meaning "general" with no migration behind it, which
    /// makes the leading <c>@</c> the whole of the test.</summary>
    private static bool IsPersonTag(string? tag) =>
        tag is not null && tag.AsSpan().TrimStart().StartsWith("@");

    /// <summary>
    /// Cleans up hand-typed or stored tags: trimmed, lower-cased, de-duplicated,
    /// and dropped when they are not a tag shape at all.
    /// <para>
    /// Person-aware, because it validates against <see cref="TagRegex"/> — the
    /// general-tag expression, which does not accept a leading <c>@</c>. Testing
    /// <c>"@bob"</c> against it directly fails, so a person tag would be silently
    /// dropped here and the tag editor would lose one every time it saved. The
    /// sigil is lifted off, the name is checked, and the sigil goes back on.
    /// </para></summary>
    private static IReadOnlyList<string> NormalizeTags(IEnumerable<string> tags) =>
        tags.Select(NormalizeTag)
            .Where(tag => tag.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

    private static string NormalizeTag(string? tag)
    {
        var trimmed = (tag ?? string.Empty).Trim();
        var person = IsPersonTag(trimmed);
        var name = (person ? trimmed[1..] : trimmed).TrimStart('#').ToLowerInvariant();

        return TagRegex.IsMatch("#" + name) ? (person ? "@" + name : name) : string.Empty;
    }

    /// <summary>
    /// Writes a status on to one sub-item chapter's metadata line, or on to every
    /// one of them when <paramref name="targetIndex"/> is null — which is what a
    /// cascading parent status change is.
    /// <para>
    /// Status is all it writes. It used to take a type, a priority and tags as
    /// well, and those parameters went when the sub-item metadata editor did: the
    /// aggregate keeps a title, a status, notes and an order for a sub-item, so
    /// the other three were written into the text and dropped again by the next
    /// save.
    /// </para>
    /// </summary>
    private static string RewriteSubItemMetaLines(
        string raw,
        int? targetIndex,
        EntryStatus? status = null)
    {
        if (targetIndex is < 0) return raw;

        var normalized = Normalize(raw);
        var lines = normalized.Split('\n').ToList();
        var inFence = false;
        var seen = 0;

        for (var i = 0; i < lines.Count; i++)
        {
            var trimmed = lines[i].TrimStart();
            if (trimmed.StartsWith("```", StringComparison.Ordinal))
            {
                inFence = !inFence;
                continue;
            }

            if (inFence) continue;

            var heading = HeadingRegex.Match(trimmed);
            if (!heading.Success || heading.Groups[1].Value.Length is not (2 or 3)) continue;

            var current = seen++;
            if (targetIndex is not null && current != targetIndex.Value) continue;

            var metaIndex = i + 1;
            while (metaIndex < lines.Count && string.IsNullOrWhiteSpace(lines[metaIndex])) metaIndex++;

            var hasMetaLine = metaIndex < lines.Count && MetaLineRegex.IsMatch(lines[metaIndex].Trim());
            var tokens = hasMetaLine
                ? TokenRegex.Matches(lines[metaIndex]).Select(match => match.Groups[1].Value.Trim()).Where(token => token.Length > 0).ToList()
                : new List<string> { TypeToken(EntryType.Task), "*" + PriorityToken(Priority.Medium) };

            // Area always inherits from the parent entry, so a sub-item never
            // keeps one of its own.
            tokens.RemoveAll(token => token.StartsWith('@'));

            if (status is not null)
            {
                tokens.RemoveAll(token => token.StartsWith('!'));
                tokens.Insert(Math.Min(2, tokens.Count), "!" + StatusToken(status.Value));
            }

            var metaLine = string.Join(' ', tokens.Select(token => $"`{token}`"));
            if (hasMetaLine)
            {
                lines[metaIndex] = metaLine;
            }
            else
            {
                lines.Insert(i + 1, metaLine);
                i++;
            }

            if (targetIndex is not null) return string.Join('\n', lines);
        }

        return targetIndex is null ? string.Join('\n', lines) : raw;
    }
    /// <summary>
    /// Rewrites one field of an entry's metadata line and leaves the rest of the
    /// line exactly as written — including tokens this parser does not recognize.
    /// <para>
    /// The clearable fields each take two parameters rather than one, the way
    /// <c>area</c>/<c>updateArea</c> already did. A nullable value alone cannot
    /// tell "clear this" from "do not touch this", and both are things a caller
    /// genuinely needs to say: deleting a due date is as ordinary an edit as
    /// setting one.
    /// </para>
    /// </summary>
    private static string RewriteMetaLine(
        string raw,
        EntryType? type = null,
        Priority? priority = null,
        EntryStatus? status = null,
        string? area = null,
        bool updateArea = false,
        IReadOnlyList<string>? tags = null,
        DateOnly? dueOn = null,
        bool updateDue = false,
        DateTime? remindAt = null,
        bool updateReminder = false,
        Recurrence? recurrence = null,
        bool updateRepeat = false,
        DateOnly? inMyDayOn = null,
        bool updateMyDay = false,
        IReadOnlyList<string>? dependsOn = null,
        bool updateDependsOn = false,
        IReadOnlyList<string>? repoIds = null,
        bool updateRepoIds = false,
        int? effort = null,
        bool updateEffort = false,
        EntryView? view = null,
        bool updateView = false,
        Attachment? attachment = null,
        bool updateAttachment = false)
    {
        var normalized = Normalize(raw);
        var lines = normalized.Split('\n').ToList();
        var titleIndex = FirstContentLine(lines);
        if (titleIndex < 0) return raw;

        var metaIndex = titleIndex + 1;
        while (metaIndex < lines.Count && string.IsNullOrWhiteSpace(lines[metaIndex])) metaIndex++;

        var hasMetaLine = metaIndex < lines.Count && MetaLineRegex.IsMatch(lines[metaIndex].Trim());
        var tokens = hasMetaLine
            ? TokenRegex.Matches(lines[metaIndex]).Select(match => match.Groups[1].Value.Trim()).Where(token => token.Length > 0).ToList()
            : DefaultMetaTokens(Parse(normalized));

        if (type is not null)
        {
            tokens.RemoveAll(IsTypeToken);
            tokens.Insert(0, TypeToken(type.Value));
        }

        if (priority is not null)
        {
            tokens.RemoveAll(token => token.StartsWith('*'));
            tokens.Insert(Math.Min(1, tokens.Count), "*" + PriorityToken(priority.Value));
        }

        if (status is not null)
        {
            tokens.RemoveAll(token => token.StartsWith('!'));
            tokens.Insert(Math.Min(2, tokens.Count), "!" + StatusToken(status.Value));
        }

        if (updateArea)
        {
            tokens.RemoveAll(token => token.StartsWith('@'));
            var normalizedArea = (area ?? string.Empty).Trim().TrimStart('@').ToLowerInvariant();
            if (normalizedArea.Length > 0)
            {
                tokens.Insert(Math.Min(3, tokens.Count), "@" + normalizedArea);
            }
        }

        if (tags is not null)
        {
            tokens.RemoveAll(token => token.StartsWith('#'));

            // The same rule ToRawText's tag loop follows, and for the same two
            // reasons: this composes `#{tag}`, so a person tag would land as the
            // corrupt token `#@bob`, and `@` on this line already means Area.
            // A person tag is carried by the preserved title text instead.
            tokens.AddRange(tags
                .Where(tag => !IsPersonTag(tag))
                .Select(tag => "#" + tag.Trim().TrimStart('#').ToLowerInvariant())
                .Where(tag => tag.Length > 1));
        }

        // The named tokens, each removed by name prefix and re-appended after the
        // tags, in the canonical order. Removal is by name rather than by
        // position because the line is hand-edited and the token may be anywhere
        // on it — and because only the field being written may move.
        if (updateDue)
        {
            RemoveNamedToken(tokens, "due");
            if (dueOn is { } due) tokens.Add($"due:{DateToken(due)}");
        }

        if (updateReminder)
        {
            RemoveNamedToken(tokens, "remind");
            if (remindAt is { } remind) tokens.Add($"remind:{ReminderToken(remind)}");
        }

        if (updateRepeat)
        {
            RemoveNamedToken(tokens, "repeat");
            if (recurrence is { } repeat) tokens.Add($"repeat:{RepeatToken(repeat)}");
        }

        if (updateMyDay)
        {
            RemoveNamedToken(tokens, "myday");
            if (inMyDayOn is { } myDay) tokens.Add($"myday:{DateToken(myDay)}");
        }

        if (updateDependsOn)
        {
            RemoveNamedToken(tokens, "after");
            tokens.AddRange((dependsOn ?? []).Select(id => $"after:{id}"));
        }

        // Written as a whole set for the reason `after:` is: the tokens are the
        // targeting, so there is nothing left to say once they are gone, and a
        // write that merged would make the first target permanent.
        if (updateRepoIds)
        {
            RemoveNamedToken(tokens, "repo");
            tokens.AddRange((repoIds ?? []).Select(repo => $"repo:{repo}"));
        }

        // Slotted with the scheduling and dependency tokens above rather than with
        // the two below it, for the reason the same token has that place in
        // ToRawText: a size is a fact about the task, and files and view are not.
        // Null removes it rather than writing `effort:` with nothing after the
        // colon — clearing an estimate and having no token are the same gesture,
        // the way they are for every other named field here.
        if (updateEffort)
        {
            RemoveNamedToken(tokens, "effort");
            if (effort is { } points) tokens.Add($"effort:{points.ToString(CultureInfo.InvariantCulture)}");
        }

        if (updateAttachment)
        {
            RemoveNamedToken(tokens, "files");
            if (attachment is { } attached) tokens.Add($"files:{attached.Path}");
        }

        if (updateView)
        {
            RemoveNamedToken(tokens, "view");
            if (view is { } chosenView) tokens.Add($"view:{ViewToken(chosenView)}");
        }

        var metaLine = string.Join(' ', tokens.Select(token => $"`{token}`"));
        if (hasMetaLine)
        {
            lines[metaIndex] = metaLine;
        }
        else
        {
            lines.Insert(titleIndex + 1, metaLine);
        }

        return string.Join('\n', lines);
    }

    private static void RemoveNamedToken(List<string> tokens, string name) =>
        tokens.RemoveAll(token => token.StartsWith(name + ":", StringComparison.OrdinalIgnoreCase));

    /// <summary>The metadata line an entry that has none would have. It is
    /// reconstructed from the parse rather than from defaults alone, so a field
    /// that was only ever written in the frontmatter — or in a metadata line
    /// somebody has just deleted — is not lost by the act of editing another
    /// one.</summary>
    private static List<string> DefaultMetaTokens(ParsedEntry parsed)
    {
        var tokens = new List<string>
        {
            TypeToken(parsed.Type ?? EntryType.Task),
            "*" + PriorityToken(parsed.Priority ?? Priority.Medium),
            "!" + StatusToken(parsed.Status ?? EntryStatus.Draft)
        };

        if (!string.IsNullOrWhiteSpace(parsed.Area)) tokens.Add("@" + parsed.Area);
        tokens.AddRange(parsed.MetadataTags.Select(tag => "#" + tag));

        if (parsed.DueOn is { } dueOn) tokens.Add($"due:{DateToken(dueOn)}");
        if (parsed.RemindAt is { } remindAt) tokens.Add($"remind:{ReminderToken(remindAt)}");
        if (parsed.Recurrence is { } recurrence) tokens.Add($"repeat:{RepeatToken(recurrence)}");
        if (parsed.InMyDayOn is { } inMyDayOn) tokens.Add($"myday:{DateToken(inMyDayOn)}");
        tokens.AddRange((parsed.DependsOn ?? []).Select(id => $"after:{id}"));
        if (!string.IsNullOrWhiteSpace(parsed.ImportItemId)) tokens.Add($"id:{parsed.ImportItemId}");
        tokens.AddRange((parsed.RepoIds ?? []).Select(repo => $"repo:{repo}"));
        if (parsed.Effort is { } effort) tokens.Add($"effort:{effort.ToString(CultureInfo.InvariantCulture)}");
        if (parsed.View is { } view) tokens.Add($"view:{ViewToken(view)}");

        return tokens;
    }

    private static bool IsTypeToken(string token) =>
        token.Length > 0
        && token[0] is not ('!' or '*' or '@' or '#')
        && TypeTokens.ContainsKey(NormalizeToken(token));

    private static int FirstContentLine(IReadOnlyList<string> lines)
    {
        for (var i = 0; i < lines.Count; i++)
        {
            if (!string.IsNullOrWhiteSpace(lines[i])) return i;
        }

        return -1;
    }

    public static string TypeToken(EntryType type) => type switch
    {
        EntryType.Prompt => "prompt",
        EntryType.Task => "task",
        EntryType.Idea => "idea",
        EntryType.FollowUp => "follow-up",
        _ => type.ToString().ToLowerInvariant()
    };

    public static string PriorityToken(Priority priority) => priority.ToString().ToLowerInvariant();

    public static string StatusToken(EntryStatus status) => status switch
    {
        EntryStatus.Draft => "draft",
        EntryStatus.Ready => "ready",
        EntryStatus.InProgress => "in-progress",
        EntryStatus.Done => "done",
        EntryStatus.Archived => "archived",
        _ => status.ToString().ToLowerInvariant()
    };

    /// <summary>The value half of a <c>due:</c> or <c>myday:</c> token. Formatted
    /// invariantly on purpose: a metadata line with a culture in it is a metadata
    /// line that stops round-tripping when the machine changes, and these files
    /// travel between devices.</summary>
    public static string DateToken(DateOnly date) => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    /// <summary>The value half of a <c>remind:</c> token — minutes, no seconds,
    /// and deliberately no zone or offset. The offset is not omitted for brevity;
    /// carrying one would turn "09:00 wherever I am" into "the instant 09:00 once
    /// meant somewhere else".</summary>
    public static string ReminderToken(DateTime remindAt) =>
        remindAt.ToString("yyyy-MM-dd'T'HH:mm", CultureInfo.InvariantCulture);

    /// <summary>The value half of a <c>repeat:</c> token. The named forms are
    /// preferred over the interval form for the shapes that have a name, because
    /// <c>weekly</c> is what a person would have typed and <c>1w</c> is what a
    /// serializer would have.</summary>
    public static string RepeatToken(Recurrence recurrence)
    {
        ArgumentNullException.ThrowIfNull(recurrence);

        // A weekday-restricted repeat has exactly one spelling in this grammar,
        // and it is Monday-to-Friday. Any other set is unwritable rather than
        // wrong — the grammar has no syntax for it, and inventing one here would
        // put a token in the file that the parser cannot read back.
        if (recurrence.Weekdays is { Count: > 0 } weekdays)
        {
            return IsMondayToFriday(weekdays) && recurrence is { Interval: 1, Unit: RecurrenceUnit.Week }
                ? "weekdays"
                : IntervalToken(recurrence);
        }

        return recurrence switch
        {
            { Interval: 1, Unit: RecurrenceUnit.Day } => "daily",
            { Interval: 1, Unit: RecurrenceUnit.Week } => "weekly",
            { Interval: 1, Unit: RecurrenceUnit.Month } => "monthly",
            { Interval: 1, Unit: RecurrenceUnit.Year } => "yearly",
            _ => IntervalToken(recurrence)
        };
    }

    /// <summary>Reads a <c>repeat:</c> value, or null when it says nothing this
    /// grammar knows. Public because the stored form of a recurrence is this same
    /// token string: a value object with structure serializes to one line here
    /// rather than to a nested map storage would have to normalize back.</summary>
    public static Recurrence? ParseRepeat(string? value) =>
        TryParseRepeatToken((value ?? string.Empty).Trim(), out var recurrence) ? recurrence : null;

    /// <summary>The value half of a <c>view:</c> token. Named after what the reader
    /// sees rather than after the component that draws it: "steps" and "notes" are
    /// the two things the pane offers, and a token spelled after a class name would
    /// be a token nobody could hand-edit.</summary>
    public static string ViewToken(EntryView view) => view switch
    {
        EntryView.Notes => "notes",
        _ => "steps"
    };

    /// <summary>Reads a <c>view:</c> value, or null when the words name no view this
    /// pane has. Public for the same reason <see cref="ParseRepeat"/> is: the file
    /// store keeps the token rather than the enum, so the assembly that
    /// deserializes frontmatter needs the same vocabulary the metadata line uses.</summary>
    public static EntryView? ParseView(string? value) =>
        TryParseViewToken((value ?? string.Empty).Trim(), out var view) ? view : null;

    private static bool TryParseViewToken(string value, out EntryView view)
    {
        switch (NormalizeToken(value))
        {
            case "steps":
                view = EntryView.Steps;
                return true;

            case "notes":
            // The block is markdown, and "markdown" is what the toggle in the pane
            // is labelled — so somebody hand-editing the line will type it. Read,
            // never written: one canonical spelling per value, or the token stops
            // being comparable to itself.
            case "markdown":
                view = EntryView.Notes;
                return true;

            default:
                view = default;
                return false;
        }
    }

    private static string IntervalToken(Recurrence recurrence) =>
        recurrence.Interval.ToString(CultureInfo.InvariantCulture) + recurrence.Unit switch
        {
            RecurrenceUnit.Day => "d",
            RecurrenceUnit.Week => "w",
            RecurrenceUnit.Month => "m",
            RecurrenceUnit.Year => "y",
            _ => "d"
        };

    private static bool IsMondayToFriday(IReadOnlyList<DayOfWeek> weekdays) =>
        weekdays.Distinct().OrderBy(day => day).SequenceEqual(WorkingWeek);

    private static bool TryParseDateToken(string value, out DateOnly date) =>
        DateOnly.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out date);

    // No `DateTimeStyles.AssumeLocal` and no `AdjustToUniversal`: parsing exactly
    // this format with no style leaves the result Unspecified, which is the whole
    // point of a wall-clock reminder.
    private static bool TryParseReminderToken(string value, out DateTime remindAt) =>
        DateTime.TryParseExact(
            value,
            ["yyyy-MM-dd'T'HH:mm", "yyyy-MM-dd'T'HH:mm:ss"],
            CultureInfo.InvariantCulture,
            DateTimeStyles.None,
            out remindAt);

    private static bool TryParseRepeatToken(string value, out Recurrence? recurrence)
    {
        recurrence = NormalizeToken(value) switch
        {
            "daily" => new Recurrence(1, RecurrenceUnit.Day),
            "weekly" => new Recurrence(1, RecurrenceUnit.Week),
            "monthly" => new Recurrence(1, RecurrenceUnit.Month),
            "yearly" => new Recurrence(1, RecurrenceUnit.Year),
            // "Every weekday" is a week-shaped repeat restricted to the working
            // week, not a fifth unit. Naming it here is what keeps
            // RecurrenceUnit down to the four periods a calendar has.
            "weekdays" => new Recurrence(1, RecurrenceUnit.Week, WorkingWeek),
            var normalized => ParseIntervalToken(normalized)
        };

        return recurrence is not null;
    }

    private static Recurrence? ParseIntervalToken(string normalized)
    {
        var match = RepeatIntervalRegex.Match(normalized);
        if (!match.Success) return null;

        if (!int.TryParse(match.Groups[1].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var interval)
            || interval < 1)
        {
            return null;
        }

        return match.Groups[2].Value switch
        {
            "d" => new Recurrence(interval, RecurrenceUnit.Day),
            "w" => new Recurrence(interval, RecurrenceUnit.Week),
            "m" => new Recurrence(interval, RecurrenceUnit.Month),
            "y" => new Recurrence(interval, RecurrenceUnit.Year),
            _ => null
        };
    }

    /// <summary>Trims and de-duplicates dependency ids while keeping the order
    /// they were written in. The order carries no meaning, but reshuffling it
    /// would churn the file for nothing.</summary>
    private static IReadOnlyList<string> NormalizeDependsOn(IEnumerable<string>? dependsOn) =>
        (dependsOn ?? [])
        .Select(id => (id ?? string.Empty).Trim())
        .Where(id => id.Length > 0)
        .Distinct(StringComparer.Ordinal)
        .ToList();

    /// <summary>The same shaping <see cref="NormalizeDependsOn"/> does, and for the
    /// same reasons: a blank is not a target, and two mentions of one repository
    /// are one target — which is exactly what the parser does reading them back.</summary>
    private static IReadOnlyList<string> NormalizeRepoIds(IEnumerable<string>? repoIds) =>
        (repoIds ?? [])
        .Select(repo => (repo ?? string.Empty).Trim())
        .Where(repo => repo.Length > 0)
        .Distinct(StringComparer.Ordinal)
        .ToList();

    private static string Normalize(string? raw) => (raw ?? string.Empty).Replace("\r\n", "\n");

    private static string NormalizeToken(string value) =>
        value.Trim().ToLowerInvariant().Replace("_", string.Empty).Replace("-", string.Empty).Replace(" ", string.Empty);
}
