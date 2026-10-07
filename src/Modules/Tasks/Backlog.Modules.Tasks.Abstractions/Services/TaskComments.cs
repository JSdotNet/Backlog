using System.Text.RegularExpressions;

using Backlog.Modules.Tasks.Abstractions.DataTransferObjects;
using Backlog.SharedKernel.Results;

namespace Backlog.Modules.Tasks.Abstractions.Services;

/// <summary>
/// Commenting on a backlog entry: a dated line appended to the entry's own
/// prose, its steps left where they are, and a usage event recorded beside it.
/// <para>
/// One rule for every caller. The <c>backlog</c> MCP server's <c>comment</c>
/// tool writes through it, and so does the Inbox's "merge into a task", over
/// the adapter that answers its backlog port — so a comment reads the same and
/// is refused for the same reasons whoever wrote it. Written over
/// <see cref="ITaskItems"/> rather than added to it: a comment is a save of the
/// entry's text and a usage event, both of which the port already publishes,
/// and the port is implemented by every host's test doubles.
/// </para>
/// </summary>
public static class TaskComments
{
    /// <summary>Asked when no entry has the id.</summary>
    public const string NotFoundCode = "item.not_found";

    /// <summary>Asked when the comment says nothing.</summary>
    public const string RequiredCode = "comment.required";

    /// <summary>Asked when a line of the comment would become structure.</summary>
    public const string NotProseCode = "comment.not_prose";

    /// <summary>
    /// The line shapes a comment may not contain, because in this product the
    /// text <em>is</em> the entry: a comment is spliced into the note region,
    /// which sits above the entry's own chapters, so a line the parser reads as
    /// structure becomes structure.
    /// <para>
    /// Three of them, and they fail in two different directions. A heading is the
    /// loud one — <c>##</c> and <c>###</c> become sub-items, and a bare <c>#</c>
    /// starts a whole new entry when the text is re-parsed
    /// (<see cref="EntryTextParser.SplitSegments"/>) — and a <c>- [ ]</c> line
    /// becomes a sub-item the same way. An opening fence is the quiet one: it adds
    /// nothing and instead <em>swallows</em>, because <c>LocateSubItems</c> tracks
    /// fences, so one unmatched <c>```</c> in a note turns every real chapter
    /// below it into fenced prose. Both make a note a structural edit nobody
    /// asked for.
    /// </para>
    /// <para>
    /// Refused rather than escaped, and that is the decision. Indenting the line
    /// out of the grammar would make it a code block; stripping the marker would
    /// store something other than what the caller said. A tag is not a heading
    /// and stays allowed — <c>#deploy</c> has no space after the hash.
    /// </para>
    /// </summary>
    private static readonly Regex Structure = new(
        @"^(?:#{1,6}[ \t]|[-*][ \t]+\[[ xX]\][ \t]|```)",
        RegexOptions.Compiled);

    /// <summary>
    /// Why <paramref name="text"/> cannot be a comment, or null when it can.
    /// Needs no entry, so a caller about to change something of its own can ask
    /// it before it does.
    /// </summary>
    public static Error? Refusal(string? text)
    {
        var note = Normalize(text);

        if (note.Length == 0)
        {
            return Error.Validation(RequiredCode, "A comment needs something to say.");
        }

        if (Array.Find(note.Split('\n'), line => Structure.IsMatch(line.TrimStart())) is { } structural)
        {
            return Error.Validation(
                NotProseCode,
                $"A comment is prose, and this line would become part of the entry itself: '{structural.Trim()}'. "
                + "Headings, checklist items and fences are how chapters and sub-items are written, so a note "
                + "carrying one would restructure the entry rather than annotate it. Say it without the marker.");
        }

        return null;
    }

    /// <summary>
    /// Appends <paramref name="text"/>, dated <paramref name="today"/>, to the
    /// entry <paramref name="id"/>'s notes and records <paramref name="usageAction"/>
    /// as a use of it. Answers the entry as saved.
    /// <para>
    /// <b>The text is rebuilt rather than written through a note-scoped
    /// helper.</b> The parent block is cut out with <c>GetParentText</c>, appended
    /// to, and spliced back with <c>ReplaceParentText</c> — the pair defined in
    /// terms of each other and therefore unable to disagree about where the
    /// sub-items start — so every sub-item is kept.
    /// </para>
    /// <para>
    /// <b>The usage event is recorded after the save, not instead of it.</b>
    /// Recording usage on a save that failed would claim the entry was used for
    /// something it never was.
    /// </para>
    /// </summary>
    public static async Task<Result<TaskItemDto>> CommentAsync(
        this ITaskItems entries,
        Guid id,
        string? text,
        DateOnly today,
        string usageAction,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entries);
        ArgumentException.ThrowIfNullOrWhiteSpace(usageAction);

        var all = await entries.ListAsync(cancellationToken).ConfigureAwait(false);

        // The entry first, then the text: a comment on nothing is answered as
        // nothing, whatever it said.
        if (all.FirstOrDefault(entry => entry.Id == id) is not { } entry)
        {
            return Result.Failure<TaskItemDto>(Error.NotFound(NotFoundCode, $"No backlog entry has the id '{id}'."));
        }

        if (Refusal(text) is { } refused) return Result.Failure<TaskItemDto>(refused);

        var raw = EntryTextParser.ToRawText(entry);
        var parent = EntryTextParser.GetParentText(raw).TrimEnd('\n');
        var note = Normalize(text);
        var dated = $"{EntryTextParser.DateToken(today)}: {note}";

        // A blank line between the date line and whatever precedes it, so the
        // note reads as markdown rather than running on into the last paragraph
        // - and no bullet in front of it, because `- [ ]` is a sub-item in this
        // grammar and a comment is not a step somebody has to tick.
        var appended = parent.Length == 0 ? dated : $"{parent}\n\n{dated}";

        var saved = await entries
            .SaveFromTextAsync(entry.Id, EntryTextParser.ReplaceParentText(raw, appended), entry.Order, cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        if (saved.IsFailure) return Result.Failure<TaskItemDto>(saved.Error);

        await entries.RecordUsageAsync(entry.Id, usageAction, cancellationToken).ConfigureAwait(false);

        return saved.Value.Entry;
    }

    private static string Normalize(string? text) =>
        (text ?? string.Empty).Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n').Trim();
}
