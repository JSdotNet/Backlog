using System.Text.RegularExpressions;

namespace Backlog.Modules.Devbook.Abstractions;

/// <summary>
/// One reply under a review note: who, when — an ISO 8601 date or instant — and
/// what they said.
/// </summary>
public sealed record DevbookNoteReply(string By, string At, string Body);

/// <summary>
/// How a review note's replies live in its remark's body, written and read in one
/// place.
/// <para>
/// A remark (<see cref="DevbookAnnotation"/>) has no thread of its own, so a reply
/// is folded below the note it answers: a blank line, then one
/// <c>— by, at: body</c> line per reply, <c>at</c> an ISO 8601 date or instant and
/// a reply's further lines indented by two spaces. The one-time import folds an
/// <c>annotation</c> fence's replies this way and <c>devbook_post_note</c> folds a
/// reply this way, and <see cref="Split"/> reads both back into the note's own
/// text and its replies — which is what <c>devbook.decision@1</c> answers. One
/// shape with one writer and one reader, so the two cannot drift apart.
/// </para>
/// <para>
/// <b>What is read as a reply.</b> Only a trailing block, after a blank line, in
/// which every reply line names who and an ISO 8601 when before its colon — the
/// shape <see cref="Fold"/> writes and nothing else does by accident. An
/// attribution the author ended a note with (<c>— Kent Beck</c>) or a dash
/// paragraph in the middle of a note is the note. A line an author typed in
/// exactly the folded shape at the very end of a note would still read as a reply;
/// that is the price of keeping replies in text.
/// </para>
/// </summary>
public static partial class DevbookNoteReplies
{
    /// <summary>What opens a folded reply line.</summary>
    public const string Mark = "— ";

    /// <summary>What opens a folded reply's further lines.</summary>
    private const string Continuation = "  ";

    /// <summary>
    /// <paramref name="body"/> with one more reply folded below it: after a blank
    /// line when it holds none yet, on the next line when it does. A reply with no
    /// text adds nothing.
    /// </summary>
    /// <exception cref="ArgumentException"><paramref name="by"/> is blank or holds a
    /// line break, or <paramref name="at"/> is no ISO 8601 date or instant — a fold
    /// <see cref="Split"/> could not read back.</exception>
    public static string Fold(string body, string by, string at, string said)
    {
        ArgumentNullException.ThrowIfNull(body);
        ArgumentException.ThrowIfNullOrWhiteSpace(by);

        if (by.Contains('\n') || by.Contains(": ", StringComparison.Ordinal))
        {
            throw new ArgumentException("A reply's author is one line with no ': ' in it.", nameof(by));
        }

        if (!IsTimestamp(at))
        {
            throw new ArgumentException($"'{at}' is no ISO 8601 date or instant.", nameof(at));
        }

        var held = Lines(body).TrimEnd();
        var text = Lines(said ?? string.Empty).Trim();
        if (text.Length == 0) return held;

        var line = $"{Mark}{by.Trim()}, {at.Trim()}: {text.Replace("\n", "\n" + Continuation, StringComparison.Ordinal)}";
        var separator = Split(held).Replies.Count > 0 ? "\n" : "\n\n";

        return held + separator + line;
    }

    /// <summary>
    /// A stored body read back as the note's own text and its replies, oldest
    /// first: the trailing block described on the class, or none — then the
    /// whole body is the note.
    /// </summary>
    public static (string Body, IReadOnlyList<DevbookNoteReply> Replies) Split(string? stored)
    {
        var text = Lines(stored ?? string.Empty).TrimEnd();
        var from = 0;

        while ((from = text.IndexOf("\n\n" + Mark, from, StringComparison.Ordinal)) >= 0)
        {
            if (Read(text[(from + 2)..].Split('\n')) is { } replies)
            {
                return (text[..from].TrimEnd(), replies);
            }

            from += 2;
        }

        return (text, []);
    }

    /// <summary>Whether a value is an ISO 8601 date (<c>2026-09-04</c>) or instant
    /// (<c>2026-10-30T09:15:00Z</c>, an offset or a fraction allowed).</summary>
    public static bool IsTimestamp(string? value) => value is not null && Timestamp().IsMatch(value.Trim());

    /// <summary>The block's replies, or null when any line is not one.</summary>
    private static List<DevbookNoteReply>? Read(string[] block)
    {
        var replies = new List<DevbookNoteReply>();

        foreach (var line in block)
        {
            if (line.StartsWith(Continuation, StringComparison.Ordinal) && replies.Count > 0)
            {
                replies[^1] = replies[^1] with { Body = replies[^1].Body + "\n" + line[Continuation.Length..] };
                continue;
            }

            if (Head().Match(line) is not { Success: true } head) return null;

            replies.Add(new DevbookNoteReply(head.Groups["by"].Value, head.Groups["at"].Value, head.Groups["said"].Value));
        }

        return replies;
    }

    private static string Lines(string text) => text.Replace("\r\n", "\n", StringComparison.Ordinal);

    private const string TimestampPattern =
        @"\d{4}-\d{2}-\d{2}(?:T\d{2}:\d{2}(?::\d{2}(?:\.\d+)?)?(?:Z|[+-]\d{2}:?\d{2})?)?";

    [GeneratedRegex("^" + TimestampPattern + @"\z", RegexOptions.CultureInvariant)]
    private static partial Regex Timestamp();

    // The author is the shortest run, holding no ": ", before the first ", " that
    // a timestamp and ": " follow — Fold refuses an author with ": " in it, so the
    // first such ", " is the author's end, and a reply whose own text holds
    // ", 2026-12-01: " keeps it. An author may still hold a comma.
    [GeneratedRegex("^— (?<by>(?:(?!: ).)+?), (?<at>" + TimestampPattern + "): (?<said>.*)\\z", RegexOptions.CultureInvariant)]
    private static partial Regex Head();
}
