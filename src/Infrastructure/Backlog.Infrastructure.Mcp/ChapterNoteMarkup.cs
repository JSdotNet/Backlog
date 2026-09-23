using System.Text;

using Backlog.Modules.Devbook.Abstractions;

namespace Backlog.Infrastructure.Mcp;

/// <summary>
/// A private reading note, written into a review read of a chapter.
/// <para>
/// A review read is the file unchanged with the notes put beside the prose they
/// are about, so the one thing this markup has to guarantee is that a session can
/// tell the two apart without guessing. Hence a pair of HTML comments: markdown
/// renders nothing for them, no devbook chapter in this repository writes one
/// that begins <c>backlog:private-note</c>, and a comment cannot be mistaken for
/// the chapter's own <c>annotation</c> fence — which is a different thing
/// entirely, a repository artefact the devbook plugin writes, and which a review
/// read leaves exactly where the author left it.
/// </para>
/// <para>
/// The note's id is in both markers. A note whose own body happened to contain
/// this closing line would be the only ambiguity left, and it would have to
/// contain the note's own guid to be one.
/// </para>
/// </summary>
internal static class ChapterNoteMarkup
{
    /// <summary>What every marker line starts with. A reader looking for the
    /// notes in a review read looks for this and nothing else.</summary>
    internal const string Prefix = "<!-- backlog:private-note";

    /// <summary>The closing marker's own opening, which is the prefix with the
    /// slash a reader expects on a closer.</summary>
    internal const string ClosingPrefix = "<!-- /backlog:private-note";

    /// <summary>
    /// One note as it goes into the document: a blank line, the opening marker,
    /// the body as it was typed, and the closing marker — each on its own line,
    /// each ended with the document's own newline.
    /// </summary>
    internal static void Append(StringBuilder builder, DevbookAnnotation note, string newline)
    {
        builder.Append(newline)
            .Append(Prefix)
            .Append(" id=\"").Append(note.Id)
            .Append("\" block=\"").Append(note.BlockIndex)
            .Append("\" author=\"").Append(Attribute(note.Author))
            .Append("\" created=\"").Append(note.CreatedAt.ToString("O"))
            .Append("\" resolved=\"").Append(note.Resolved ? "true" : "false")
            .Append("\" -->")
            .Append(newline);

        // The body verbatim apart from its line endings, which follow the
        // document's so a CRLF chapter does not come back half Unix.
        foreach (var line in note.Body.Replace("\r\n", "\n").Split('\n'))
        {
            builder.Append(line).Append(newline);
        }

        builder.Append(ClosingPrefix)
            .Append(" id=\"").Append(note.Id)
            .Append("\" -->")
            .Append(newline);
    }

    /// <summary>A value safe inside the double quotes of a marker attribute. An
    /// author's name is the only one that could carry a quote, and a broken
    /// marker is worse than an escaped name.</summary>
    private static string Attribute(string value) =>
        value.Replace("\"", "&quot;", StringComparison.Ordinal)
            .Replace("\r", " ", StringComparison.Ordinal)
            .Replace("\n", " ", StringComparison.Ordinal);
}
