using System.Globalization;

namespace Backlog.Mobile.UI.TalkNotes;

/// <summary>
/// The title a talk note arrives in the Inbox under. A note written with one
/// thumb during a talk rarely has one, and a capture needs one — so it falls
/// back, in order, to the first line of the body, then to "Photo · date" when a
/// picture is attached, then to the first file's name.
/// </summary>
public static class TalkNoteTitle
{
    /// <summary>How much of a body line a title borrows. The rest stays in the
    /// body, which still carries the whole line.</summary>
    public const int MaximumFromBody = 200;

    /// <summary>The title, or empty when the note has nothing to be called —
    /// which is also a note with nothing in it, and so nothing to save.</summary>
    public static string For(string? title, string? body, IReadOnlyList<(string Name, string ContentType)> attachments, DateOnly today)
    {
        ArgumentNullException.ThrowIfNull(attachments);

        if (title?.Trim() is { Length: > 0 } typed) return typed;

        if (FirstLine(body) is { } line) return line;

        if (attachments.Any(attachment => AttachmentRules.IsPicture(attachment.ContentType)))
        {
            return string.Create(CultureInfo.InvariantCulture, $"Photo · {today:d MMM yyyy}");
        }

        return attachments.Count > 0 ? attachments[0].Name : string.Empty;
    }

    /// <summary>The body's first line with words on it, without the Markdown
    /// that starts it — a heading's hashes, a list's bullet, a quote's bar.</summary>
    private static string? FirstLine(string? body)
    {
        if (string.IsNullOrWhiteSpace(body)) return null;

        foreach (var raw in body.Split('\n'))
        {
            var line = raw.Trim().TrimStart('#', '>', '-', '*', '+').Trim();
            if (line.Length == 0) continue;

            return line.Length <= MaximumFromBody ? line : $"{line[..(MaximumFromBody - 1)].TrimEnd()}…";
        }

        return null;
    }
}
