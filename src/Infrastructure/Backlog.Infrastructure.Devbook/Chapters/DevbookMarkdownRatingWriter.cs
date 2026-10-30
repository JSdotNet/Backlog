using Backlog.SharedKernel.Devbook;

namespace Backlog.Infrastructure.Devbook.Chapters;

/// <summary>
/// Writes a rating into the <c>meta</c> fence belonging to one knowledge heading —
/// a chapter's, addressed by <c>&lt;path&gt;#&lt;slug&gt;</c>, or a file's own,
/// addressed by the bare path — in <c>tech/</c> and <c>ai/</c>, and nowhere else.
///
/// <para>The only <c>status</c> field the product still writes. Under devbook's
/// branch-true decision a chapter's workflow state — a decision record's, a debt record's, a proposal's
/// approval — is kept by the tool and not in git, so the product no longer writes
/// a <c>status</c> into <c>arc42/</c>, <c>domain/</c> or <c>design/</c>; an
/// authored one there still reads, as a badge. In <c>tech/</c> and <c>ai/</c>
/// the same field is a rating — where a technology stands on the adoption ladder,
/// how a team uses a practice — which is what the chapter says rather than where
/// it is in a workflow, so it stays in the Markdown. The field keeps its name,
/// <c>status</c>, until devbook contract 38 renames it <c>rating</c>.</para>
///
/// <para>Refused before the file is opened, so a refused write leaves no trace:
/// any folder but those two, and a decision rung, which is in neither folder's
/// vocabulary. Neither folder rests by omission, so every rating is written as a
/// word and none is a removal; and with no decision rungs there is no decision
/// record for a write to take with it.</para>
/// </summary>
internal static class DevbookMarkdownRatingWriter
{
    /// <summary>Set the heading's rating, inserting the field — or the whole
    /// fence — when it is not there yet.</summary>
    public static void UpdateRating(string folderRoot, string itemPath, string folderPrefix, string rating)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(folderRoot);
        ArgumentException.ThrowIfNullOrWhiteSpace(itemPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(folderPrefix);
        ArgumentException.ThrowIfNullOrWhiteSpace(rating);

        var folder = DevbookFolders.FromPath(folderPrefix);
        if (folder is not (DevbookFolder.Tech or DevbookFolder.Ai))
        {
            throw new ArgumentException(
                $"Only tech/ and ai/ keep a rating in the Markdown; a chapter's state in {folderPrefix} is the tool's: {itemPath}",
                nameof(folderPrefix));
        }

        var value = rating.Trim().ToLowerInvariant();
        if (DevbookSchema.IsDecisionRung(value))
        {
            throw new ArgumentException(
                $"'{value}' is a decision rung, and a rating is never one: {itemPath}", nameof(rating));
        }

        var document = DevbookHeadingDocument.Open(folderRoot, itemPath, folderPrefix);
        Upsert(document.Lines, document.HeadingIndex, value);
        document.Save();
    }

    private static void Upsert(List<string> lines, int headingIndex, string rating)
    {
        var fenceIndex = DevbookHeadingDocument.FindFence(lines, headingIndex);
        if (fenceIndex < 0)
        {
            lines.InsertRange(headingIndex + 1, [string.Empty, "```meta", $"status: {rating}", "```"]);
            return;
        }

        var line = FindRatingLine(lines, fenceIndex);
        if (line < 0)
        {
            lines.Insert(fenceIndex + 1, $"status: {rating}");
            return;
        }

        var indent = lines[line][..(lines[line].Length - lines[line].TrimStart().Length)];
        lines[line] = $"{indent}status: {rating}";
    }

    /// <summary>The index of the <c>status</c> line inside the fence opened at
    /// <paramref name="fenceIndex"/>, or -1 when the fence states none. The scan
    /// stops at the closing fence so a <c>status:</c> further down the document is
    /// never mistaken for this heading's.</summary>
    private static int FindRatingLine(IReadOnlyList<string> lines, int fenceIndex)
    {
        for (var i = fenceIndex + 1; i < lines.Count && !lines[i].TrimStart().StartsWith("```", StringComparison.Ordinal); i++)
        {
            if (lines[i].TrimStart().StartsWith("status:", StringComparison.OrdinalIgnoreCase)) return i;
        }

        return -1;
    }
}
