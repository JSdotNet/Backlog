using System.Text.RegularExpressions;
using Backlog.Domain;

namespace Backlog.UI.Services;

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
/// <c>- [ ]</c> checklist lines both become sub-items, and <c>###</c> and
/// deeper are just prose.
/// </para>
/// <para>
/// Deliberately independent from <c>Backlog.Storage.EnumMap</c> (internal to
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

    public sealed record ParsedSubItem(string Title, bool Done, string? Notes);

    public sealed record ParsedEntry(
        string Title,
        EntryType? Type,
        Priority? Priority,
        EntryStatus? Status,
        string? Area,
        string Body,
        IReadOnlyList<string> Tags,
        IReadOnlyList<ParsedSubItem> SubItems);

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

        EntryType? type = null;
        Priority? priority = null;
        EntryStatus? status = null;
        string? area = null;

        if (i < lines.Length && MetaLineRegex.IsMatch(lines[i].Trim()))
        {
            foreach (Match match in TokenRegex.Matches(lines[i]))
            {
                var token = match.Groups[1].Value.Trim();
                if (token.Length == 0) continue;

                // Each sigil names exactly one kind of metadata, so `!ready`
                // cannot be mistaken for anything but a status. A sigilled token
                // that isn't recognized is left unset rather than falling
                // through to another kind — the sigil already said what was
                // meant, and guessing past it would be worse than ignoring it.
                switch (token[0])
                {
                    // Free-form on purpose — the vocabulary of areas is the
                    // person's, not ours — so it is only lower-cased and
                    // trimmed, never matched against a list.
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
                }

                // No sigil: an entry written before the sigils existed, or
                // someone typing the plain word. Recognize it anyway.
                var normalized = NormalizeToken(token);
                if (TypeTokens.TryGetValue(normalized, out var t)) type = t;
                else if (PriorityTokens.TryGetValue(normalized, out var p)) priority = p;
                else if (StatusTokens.TryGetValue(normalized, out var s)) status = s;
            }

            i++;
        }

        while (i < lines.Length && string.IsNullOrWhiteSpace(lines[i])) i++;

        var body = string.Join('\n', lines.Skip(i)).TrimEnd('\n');

        var tags = TagRegex.Matches(StripFencedCode(body))
            .Select(m => m.Groups[1].Value.ToLowerInvariant())
            .Distinct()
            .ToList();

        return new ParsedEntry(title, type, priority, status, area, body, tags, ExtractSubItems(body));
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
    /// heading becomes a sub-item whose notes are the prose beneath it; a
    /// <c>- [ ]</c> line becomes a standalone checklist sub-item. Both may carry
    /// a <c>[x]</c> marker for done.</summary>
    private static List<ParsedSubItem> ExtractSubItems(string body)
    {
        var items = new List<ParsedSubItem>();
        var lines = body.Split('\n');
        var inFence = false;

        string? openTitle = null;
        var openDone = false;
        var openNotes = new List<string>();

        void CloseOpen()
        {
            if (openTitle is null) return;
            var notes = string.Join('\n', openNotes).Trim('\n', ' ', '\t');
            items.Add(new ParsedSubItem(openTitle, openDone, notes.Length == 0 ? null : notes));
            openTitle = null;
            openDone = false;
            openNotes.Clear();
        }

        foreach (var line in lines)
        {
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
                // A checklist line is always its own sub-item, never notes text
                // for the heading it happens to sit under — and closing the open
                // heading first keeps sub-items in the order they were written.
                CloseOpen();
                items.Add(new ParsedSubItem(
                    checklist.Groups[2].Value.Trim(),
                    checklist.Groups[1].Value is "x" or "X",
                    Notes: null));
                continue;
            }

            var heading = HeadingRegex.Match(trimmed);
            if (heading.Success)
            {
                CloseOpen();

                if (heading.Groups[1].Value.Length != 2) continue;

                var text = heading.Groups[2].Value.Trim();
                var box = CheckboxPrefixRegex.Match(text);
                if (box.Success)
                {
                    openDone = box.Groups[1].Value is "x" or "X";
                    text = text[box.Length..].Trim();
                }

                if (text.Length == 0) continue;
                openTitle = text;
                continue;
            }

            if (openTitle is not null) openNotes.Add(line);
        }

        CloseOpen();
        return items;
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

        var body = entry.ContentMd.TrimEnd('\n');
        return body.Length == 0
            ? $"# {entry.Title}\n{meta}\n"
            : $"# {entry.Title}\n{meta}\n\n{body}\n";
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
