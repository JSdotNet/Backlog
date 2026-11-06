using System.Buffers;
using System.Globalization;
using System.Text;

namespace Backlog.Modules.Notifications.Abstractions;

/// <summary>
/// The text rules a source builds a <see cref="NotificationDraft"/> and its
/// <see cref="NotificationOrigin"/> to, so every source shortens and flattens its words the same
/// way (<c>.devbook/domain/notifications/domain.invariants.md</c>).
/// </summary>
public static class NotificationText
{
    /// <summary>What marks a cut.</summary>
    public const string Ellipsis = "…";

    /// <summary>The least room worth showing a change in; see <see cref="Glance"/>.</summary>
    private const int MinChangeRoom = 12;

    private static readonly SearchValues<char> LineBreaks = SearchValues.Create("\n\r\u0085\u2028\u2029");

    /// <summary>
    /// <paramref name="text"/> unchanged when it fits <paramref name="length"/>; otherwise cut so
    /// that, with <see cref="Ellipsis"/> after it, it is at most <paramref name="length"/> UTF-16
    /// units — the owner's bounds count <see cref="string.Length"/>.
    /// <para>
    /// The cut keeps whole text elements, so it never leaves half a surrogate pair or splits an
    /// emoji joined with zero-width joiners or a flag. A first element longer than the room on its
    /// own is cut between code points instead.
    /// </para>
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="length"/> is below one.</exception>
    public static string Cut(string text, int length)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentOutOfRangeException.ThrowIfLessThan(length, 1);

        if (text.Length <= length) return text;

        var room = length - Ellipsis.Length;
        var taken = 0;
        while (taken < text.Length)
        {
            var next = StringInfo.GetNextTextElementLength(text, taken);
            if (taken + next > room) break;
            taken += next;
        }

        if (taken == 0) taken = WholeRunesWithin(text, room);

        return text[..taken] + Ellipsis;
    }

    /// <summary>
    /// A title that reads at a glance: <c>repository · change · what the person must do</c>, each
    /// part made one line and a blank part dropped. The repository is named without its owner.
    /// <para>
    /// The repository and the ask stay whole, and only the change is cut to the room they leave;
    /// one that does not fit, with less than <see cref="MinChangeRoom"/> left, is dropped, since a
    /// stub names nothing. A change that only repeats the repository's name is dropped too. The
    /// whole is then cut to <paramref name="length"/>, so a long ask still fits.
    /// </para>
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="length"/> is below one.</exception>
    public static string Glance(string? repository, string? change, string ask, int length)
    {
        ArgumentNullException.ThrowIfNull(ask);
        ArgumentOutOfRangeException.ThrowIfLessThan(length, 1);

        var name = RepositoryName(repository);
        var todo = OneLine(ask);
        var around = Joined(name, todo);
        var room = around.Length == 0 ? length : length - around.Length - NotificationOrigin.Separator.Length;
        var told = OneLine(change);
        var repeats = name.Length > 0 && string.Equals(RepositoryName(told), name, StringComparison.OrdinalIgnoreCase);
        var what = repeats
            ? string.Empty
            : told.Length <= room ? told : room < MinChangeRoom ? string.Empty : Cut(told, room);

        return Cut(Joined(name, what, todo), length);
    }

    /// <summary>The text as one line: every run of white space, line breaks included, made one
    /// space, and the ends trimmed. Empty for null.</summary>
    public static string OneLine(string? text) =>
        string.Join(' ', (text ?? string.Empty).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

    /// <summary>Whether <paramref name="text"/> holds no line break of any kind: no line feed,
    /// carriage return, next line (U+0085), line separator (U+2028) or paragraph separator
    /// (U+2029). <see cref="OneLine"/> text always is.</summary>
    public static bool IsOneLine(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        return text.AsSpan().IndexOfAny(LineBreaks) < 0;
    }

    /// <summary>The name after the owner in <c>owner/name</c>, as one line.</summary>
    private static string RepositoryName(string? repository)
    {
        var line = OneLine(repository);

        return line[(line.LastIndexOf('/') + 1)..].TrimStart();
    }

    private static string Joined(params string[] parts) =>
        string.Join(NotificationOrigin.Separator, parts.Where(part => part.Length > 0));

    private static int WholeRunesWithin(string text, int room)
    {
        var taken = 0;
        while (taken < text.Length)
        {
            // A lone surrogate decodes as one unit of invalid data, so the walk always moves on.
            _ = Rune.DecodeFromUtf16(text.AsSpan(taken), out _, out var next);
            if (taken + next > room) break;
            taken += next;
        }

        return taken;
    }
}
