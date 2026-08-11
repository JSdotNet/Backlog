using System.Text.RegularExpressions;
using Backlog.Modules.Backlog;
using Backlog.Modules.Backlog.DomainModels;

namespace Backlog.Desktop.UI.Services;

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
internal static class EntryTextParser
{
    private static readonly Regex MetaLineRegex = new(@"^(\s*`[^`\n]+`\s*)+$", RegexOptions.Compiled);
    private static readonly Regex TokenRegex = new(@"`([^`]+)`", RegexOptions.Compiled);
    private static readonly Regex TagRegex = new(@"(?<!\S)#([A-Za-z][\w-]*)", RegexOptions.Compiled);
    private static readonly Regex HeadingRegex = new(@"^(#{1,6})[ \t]+(.*)$", RegexOptions.Compiled);
    private static readonly Regex CheckboxPrefixRegex = new(@"^\[( |x|X)\][ \t]+", RegexOptions.Compiled);
    private static readonly Regex HeadingCheckboxMarkerRegex = new(@"^(?<prefix>[ \t]*#{2,3}[ \t]+)\[(?<marker> |x|X)\](?<suffix>[ \t]+.*)$", RegexOptions.Compiled);
    private static readonly Regex ChecklistRegex = new(@"^[ \t]*[-*][ \t]+\[( |x|X)\][ \t]+(.+?)[ \t]*$", RegexOptions.Compiled);

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

    public sealed record ParsedEntry(
        string Title,
        EntryType? Type,
        Priority? Priority,
        EntryStatus? Status,
        string? Area,
        string Body,
        IReadOnlyList<string> Tags,
        IReadOnlyList<string> MetadataTags,
        IReadOnlyList<ParsedSubItem> SubItems);

    private sealed record Metadata(
        EntryType? Type,
        Priority? Priority,
        EntryStatus? Status,
        string? Area,
        IReadOnlyList<string> Tags)
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
            title = line.StartsWith("# ", StringComparison.Ordinal) ? line[2..].Trim() : line;
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

        var distinctMetadataTags = metadataTags.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var tags = distinctMetadataTags.Concat(bodyTags).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

        return new ParsedEntry(title, type, priority, status, area, body, tags, distinctMetadataTags, ExtractSubItems(body, area));
    }

    private static Metadata ParseMetadataLine(string line)
    {
        EntryType? type = null;
        Priority? priority = null;
        EntryStatus? status = null;
        string? area = null;
        var metadataTags = new List<string>();

        foreach (Match match in TokenRegex.Matches(line))
        {
            var token = match.Groups[1].Value.Trim();
            if (token.Length == 0) continue;

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

        return new Metadata(type, priority, status, area, metadataTags.Distinct(StringComparer.OrdinalIgnoreCase).ToList());
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

    /// <summary>
    /// Finds the line range of every <c>##</c> sub-item in a block of raw entry
    /// text. A sub-item owns everything written after its heading up to the next
    /// heading of the same level or higher, which is exactly what the read view
    /// hangs beneath it — so moving a span moves the sub-item together with its
    /// notes. Headings inside fenced code are text, not structure.
    /// </summary>
    public static IReadOnlyList<SubItemSpan> LocateSubItems(string raw)
    {
        var lines = Normalize(raw).Split('\n');
        var starts = new List<int>();
        var ends = new List<int>();
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
            if (level > 3) continue;

            if (starts.Count > ends.Count) ends.Add(i);
            if (level is 2 or 3) starts.Add(i);
        }

        if (starts.Count > ends.Count) ends.Add(lines.Length);

        var spans = new List<SubItemSpan>(starts.Count);
        for (var i = 0; i < starts.Count; i++)
        {
            // Trailing blank lines are separators between sub-items, not part of
            // one; leaving them in would multiply on every move.
            var end = ends[i];
            while (end > starts[i] + 1 && string.IsNullOrWhiteSpace(lines[end - 1])) end--;
            spans.Add(new SubItemSpan(starts[i], end));
        }

        return spans;
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
        var spans = LocateSubItems(normalized);

        if (from == to || from < 0 || to < 0 || from >= spans.Count || to >= spans.Count) return raw;

        var lines = normalized.Split('\n');
        var blocks = spans.Select(span => lines[span.Start..span.End]).ToList();
        var moved = blocks[from];
        blocks.RemoveAt(from);
        blocks.Insert(to, moved);

        var rebuilt = new List<string>(lines[..spans[0].Start]);
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

    /// <summary>Toggles the checkbox on the nth rendered level-2 sub-item heading,
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
            if (!heading.Success || heading.Groups[1].Value.Length != 2) continue;

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

    /// <summary>Syncs parsed sub-items onto the entry's structured sub-items by
    /// position — the typed text is the single source of truth; nothing outside
    /// this entry references a sub-item's id, so re-deriving identity from
    /// position on every save is safe.</summary>
    public static void SyncSubItems(BacklogEntry entry, IReadOnlyList<ParsedSubItem> parsedItems)
    {
        var existing = entry.SubItems.OrderBy(s => s.Order).ToList();

        for (var idx = existing.Count - 1; idx >= parsedItems.Count; idx--)
        {
            entry.RemoveSubItem(existing[idx].Id);
        }

        existing = entry.SubItems.OrderBy(s => s.Order).ToList();

        for (var idx = 0; idx < parsedItems.Count; idx++)
        {
            var parsed = parsedItems[idx];
            var wantStatus = parsed.Done ? SubItemStatus.Done : SubItemStatus.Pending;

            if (idx < existing.Count)
            {
                var item = existing[idx];
                if (!string.Equals(item.Title, parsed.Title, StringComparison.Ordinal)
                    || !string.Equals(item.Notes, parsed.Notes, StringComparison.Ordinal))
                {
                    entry.UpdateSubItem(item.Id, parsed.Title, parsed.Notes);
                }

                if (item.Status != wantStatus)
                {
                    entry.SetSubItemStatus(item.Id, wantStatus);
                }
            }
            else
            {
                var newItem = entry.AddSubItem(parsed.Title, parsed.Notes);
                if (parsed.Done)
                {
                    entry.SetSubItemStatus(newItem.Id, SubItemStatus.Done);
                }
            }
        }
    }

    /// <summary>Builds the canonical raw-text form of an entry — the inverse of
    /// <see cref="Parse"/> — so the editor always reflects exactly what was
    /// saved.</summary>
    public static string ToRawText(BacklogEntry entry)
    {
        var meta = $"`{TypeToken(entry.Type)}` `*{PriorityToken(entry.Priority)}` `!{StatusToken(entry.Status)}`";
        if (!string.IsNullOrWhiteSpace(entry.Area)) meta += $" `@{entry.Area}`";
        foreach (var tag in entry.Tags.Select(tag => tag.Trim().TrimStart('#')).Where(tag => tag.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            meta += $" `#{tag.ToLowerInvariant()}`";
        }

        var body = entry.ContentMd.TrimEnd('\n');
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

    public static string WithSubItemType(string raw, int subItemIndex, EntryType type) =>
        RewriteSubItemMetaLines(raw, subItemIndex, type: type);

    public static string WithSubItemPriority(string raw, int subItemIndex, Priority priority) =>
        RewriteSubItemMetaLines(raw, subItemIndex, priority: priority);

    public static string WithSubItemStatus(string raw, int subItemIndex, EntryStatus status) =>
        RewriteSubItemMetaLines(raw, subItemIndex, status: status);

    public static string WithSubItemTags(string raw, int subItemIndex, string tags) =>
        RewriteSubItemMetaLines(raw, subItemIndex, tags: ParseTagsInput(tags));

    public static string WithArea(string raw, string? area) =>
        RewriteMetaLine(raw, area: area, updateArea: true);

    public static string WithTags(string raw, IEnumerable<string> tags) =>
        RewriteMetaLine(raw, tags: NormalizeTags(tags));

    public static string WithTags(string raw, string tags) =>
        RewriteMetaLine(raw, tags: ParseTagsInput(tags));

    public static bool IsMetadataLine(string line) => MetaLineRegex.IsMatch((line ?? string.Empty).Trim());

    public static IReadOnlyList<string> ParseTagsInput(string tags) =>
        NormalizeTags(Regex.Split(tags ?? string.Empty, @"[\s,]+"));

    private static IReadOnlyList<string> NormalizeTags(IEnumerable<string> tags) =>
        tags.Select(tag => tag.Trim().TrimStart('#').ToLowerInvariant())
            .Where(tag => TagRegex.IsMatch("#" + tag))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

    private static string RewriteSubItemMetaLines(
        string raw,
        int? targetIndex,
        EntryType? type = null,
        Priority? priority = null,
        EntryStatus? status = null,
        IReadOnlyList<string>? tags = null)
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

            tokens.RemoveAll(token => token.StartsWith('@'));

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

            if (tags is not null)
            {
                tokens.RemoveAll(token => token.StartsWith('#'));
                tokens.AddRange(tags.Select(tag => "#" + tag.Trim().TrimStart('#').ToLowerInvariant()).Where(tag => tag.Length > 1));
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
    private static string RewriteMetaLine(
        string raw,
        EntryType? type = null,
        Priority? priority = null,
        EntryStatus? status = null,
        string? area = null,
        bool updateArea = false,
        IReadOnlyList<string>? tags = null)
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
            tokens.AddRange(tags.Select(tag => "#" + tag.Trim().TrimStart('#').ToLowerInvariant()).Where(tag => tag.Length > 1));
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

    private static string Normalize(string? raw) => (raw ?? string.Empty).Replace("\r\n", "\n");

    private static string NormalizeToken(string value) =>
        value.Trim().ToLowerInvariant().Replace("_", string.Empty).Replace("-", string.Empty).Replace(" ", string.Empty);
}
