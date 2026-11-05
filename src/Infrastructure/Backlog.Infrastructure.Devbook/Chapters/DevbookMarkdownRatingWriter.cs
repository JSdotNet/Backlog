using Backlog.SharedKernel.Devbook;

namespace Backlog.Infrastructure.Devbook.Chapters;

/// <summary>
/// Writes a rating into the <c>meta</c> fence belonging to one knowledge heading —
/// a chapter's, addressed by <c>&lt;path&gt;#&lt;slug&gt;</c>, or a file's own,
/// addressed by the bare path — in <c>tech/</c> and <c>ai/</c>, and nowhere else.
///
/// <para>The only metadata field the product still writes into a chapter. Under
/// devbook's branch-true decision a chapter's workflow state — a decision record's,
/// a debt record's, a proposal's approval — is kept by the tool and not in git, so
/// the product writes no <c>status</c> anywhere. In <c>tech/</c> and <c>ai/</c> a
/// block states a rating — where a technology stands on the adoption ladder, how a
/// team uses a practice — which is what the chapter says rather than where it is in
/// a workflow, so it stays in the Markdown. Devbook contract 38 spells it
/// <c>rating</c> and rejects <c>status</c> in both folders, and the generator reads
/// <c>rating</c> alone. So the writer updates a <c>rating</c> line in place and
/// drops any <c>status</c> line beside it in the same fence; renames a legacy
/// <c>status</c>-only line to <c>rating</c> in place; and gives a fence that
/// carries neither — or a heading with no fence — a <c>rating</c> line.</para>
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

    /// <summary>The field a fence states its rating in from devbook contract 38
    /// on, and the only one a write leaves behind.</summary>
    private const string RatingField = "rating";

    /// <summary>The field a fence written before contract 38 states its rating
    /// in; the contract rejects it in both folders, so a write renames it.</summary>
    private const string StatusField = "status";

    private static void Upsert(List<string> lines, int headingIndex, string rating)
    {
        var fenceIndex = DevbookHeadingDocument.FindFence(lines, headingIndex);
        if (fenceIndex < 0)
        {
            lines.InsertRange(headingIndex + 1, [string.Empty, "```meta", $"{RatingField}: {rating}", "```"]);
            return;
        }

        // `rating` first: updated in place, and a `status` line a half-done
        // migration left beside it in this fence goes. Otherwise a legacy
        // `status` line is renamed where it stands, so the fence keeps its order.
        var line = FindFieldLine(lines, fenceIndex, RatingField);
        if (line >= 0)
        {
            Set(lines, line, rating);
            DevbookHeadingDocument.RemoveFields(lines, fenceIndex, [StatusField]);
            return;
        }

        line = FindFieldLine(lines, fenceIndex, StatusField);
        if (line >= 0)
        {
            Set(lines, line, rating);
            return;
        }

        lines.Insert(fenceIndex + 1, $"{RatingField}: {rating}");
    }

    /// <summary>Write <c>rating: &lt;value&gt;</c> over the line, keeping its indent.</summary>
    private static void Set(List<string> lines, int line, string rating)
    {
        var indent = lines[line][..(lines[line].Length - lines[line].TrimStart().Length)];
        lines[line] = $"{indent}{RatingField}: {rating}";
    }

    /// <summary>The index of the <paramref name="field"/> line inside the fence
    /// opened at <paramref name="fenceIndex"/>, or -1 when the fence states none.
    /// The scan stops at the closing fence so the same field further down the
    /// document is never mistaken for this heading's.</summary>
    private static int FindFieldLine(IReadOnlyList<string> lines, int fenceIndex, string field)
    {
        var prefix = field + ":";
        for (var i = fenceIndex + 1; i < lines.Count && !lines[i].TrimStart().StartsWith("```", StringComparison.Ordinal); i++)
        {
            // Unindented only: an indented key is nested under another field.
            if (lines[i].StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return i;
        }

        return -1;
    }
}
