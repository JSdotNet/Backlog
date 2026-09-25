using Backlog.Modules.Devbook.Abstractions;
using Backlog.SharedKernel.Ai;
using Backlog.UI.Components.Devbook;
using Backlog.UI.Components.Markdown;
using Backlog.UI.Components.Metadata;

namespace Backlog.Desktop.UI.Devbook;

/// <summary>
/// Devbook's answer to <see cref="IAiContentSource"/>: the chapters of the
/// scoped repository's devbook that the question is about, and the one the
/// reader has open.
/// </summary>
/// <remarks>
/// <para>
/// A devbook is too big to send and too structured to sample, so this is the
/// one source that does not start from "every record". It asks the search
/// index — <see cref="IDevbookSearch"/>, over the generated database of local
/// ADR 0004 — which chapters mention the question's words, loads those, and
/// lets the budget choose among them. The open chapter is pinned on top,
/// because a reader who asks with a chapter in front of them is usually asking
/// about it.
/// </para>
/// <para>
/// The index is asked one word at a time rather than with the question. Its
/// expression joins terms with AND, which is right for a reader typing a
/// phrase into the search box and wrong for a sentence: "what does the sync
/// service do when the device is offline" would demand a chapter containing
/// all six content words. One query per term, merged by chapter with the
/// scores added, is a handful of cheap SQLite reads and answers the sentence.
/// </para>
/// <para>
/// Without an index the source still answers, with less: the body opens with a
/// line saying search is unavailable and carries only the open chapter.
/// Nothing scans the folders instead — ADR 0004 is explicit that search is the
/// one capability that does not degrade to a corpus walk, and Ask AI is not
/// the place to start.
/// </para>
/// <para>
/// A chapter longer than the whole budget is cut to fit rather than skipped.
/// The selection skips what does not fit, and for every other source that is
/// right — a task is a task — but a reader with a twelve-thousand-character
/// chapter open who asks about it would otherwise get an answer from every
/// chapter except that one. A body that carries a cut chapter reports itself
/// trimmed, because the assistant did not see that chapter whole.
/// </para>
/// </remarks>
internal sealed class DevbookAiContentSource(
    IDevbookSearch search,
    IDevbookFolderSource folders,
    DevbookOpenChapter openChapter) : IAiContentSource
{
    /// <summary>How many chapters the index is asked for, over every term
    /// together. The budget then chooses among them; eight is more than fit in
    /// it and few enough that loading them is not felt.</summary>
    internal const int MaximumHits = 8;

    /// <summary>How many of the question's words are put to the index. A long
    /// question's later words are rarely its subject, and each one is a query.</summary>
    private const int MaximumTerms = 6;

    /// <summary>How many index rows are read for each chapter wanted. The index
    /// answers per heading and a chapter has several, so a read of exactly
    /// <see cref="MaximumHits"/> rows could all be one chapter.</summary>
    private const int RowsPerHit = 4;

    /// <summary>What a chapter that will not fit is cut down to end with, so
    /// the assistant can see it was cut.</summary>
    private const string Ellipsis = "…";

    public string AreaKey => "devbook";

    public string AreaTitle => "Devbook";

    public async Task<AiContent> ComposeAsync(AiContentRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var repositoryAlias = openChapter.RepositoryAlias;
        var pinned = await LoadOpenChapterAsync(repositoryAlias, cancellationToken).ConfigureAwait(false);

        var (hits, unavailable) = Find(request.Question, repositoryAlias);

        var records = new List<ChapterRecord>();
        if (pinned is not null) records.Add(pinned);

        foreach (var hit in hits)
        {
            cancellationToken.ThrowIfCancellationRequested();

            // A hit that names a section this build does not read, or a file
            // that has gone since the index was built, is left out rather than
            // written as a heading with nothing under it.
            if (DevbookChapterLink.From(hit.Chapter.Reference) is not { IsC4View: false } link) continue;
            if (pinned is not null && pinned.SameChapter(link.AreaKey, link.RelativePath)) continue;

            var record = await LoadAsync(repositoryAlias, link.AreaKey, link.RelativePath, cancellationToken).ConfigureAwait(false);
            if (record is not null) records.Add(record);
        }

        // The line that says the index could not be asked goes above the header,
        // and is paid for out of the same budget, so the body still fits.
        var preface = unavailable is null
            ? null
            : pinned is not null
                ? $"The devbook search index is unavailable: {unavailable} Only the open chapter is included."
                : $"The devbook search index is unavailable and no chapter is open. {unavailable}";
        var budget = request.BudgetCharacters - (preface is null ? 0 : preface.Length + 1);

        // Fitted before selection, so a chapter the budget would skip whole is
        // offered as much of itself as the budget holds — which is the budget
        // less the header the composition writes above it.
        var room = budget - AiContentBudget.HeaderReserve(AreaTitle, records.Count);
        var fitted = records.Select(record => record.FitTo(room)).ToList();

        var content = AiContentBudget.Compose(
            AreaKey,
            AreaTitle,
            fitted,
            record => record.Text,
            request.Question,
            budget,
            pinned: record => record.Pinned);

        // A chapter that went in cut is a chapter the assistant did not see
        // whole, which is what Trimmed means to whoever reads the answer.
        var cut = fitted.Any(record => record.Cut && content.Body.Contains(record.Text, StringComparison.Ordinal));

        return content with
        {
            Body = preface is null ? content.Body : preface + "\n" + content.Body,
            Trimmed = content.Trimmed || cut
        };
    }

    /// <summary>The chapters the index says mention the question, best first,
    /// or the index's reason for not answering.</summary>
    private (IReadOnlyList<DevbookSearchHit> Hits, string? Unavailable) Find(string? question, string? repositoryAlias)
    {
        var terms = AiContentBudget.Terms(question).Take(MaximumTerms).ToList();

        // Asked once with nothing, so a question made of stop words still finds
        // out whether there is an index — and says so rather than pretending
        // there was nothing to find.
        if (terms.Count == 0)
        {
            var probe = search.Search(string.Empty, repositoryAlias, limit: MaximumHits);
            return ([], probe.UnavailableMessage);
        }

        // One entry per chapter file, however many of its headings the index
        // returned: a hit is a heading's worth of text and a chapter has many, so
        // the rows are asked for generously and collapsed by path before the cap
        // is applied — otherwise eight rows could be one chapter eight times.
        // A chapter's score is its best row's, because the port's score is
        // "higher is better" for one row and adding rows would reward length;
        // the number of the question's words it answered to breaks ties.
        var chapters = new Dictionary<string, (DevbookSearchHit Hit, double Best, int Terms)>(StringComparer.OrdinalIgnoreCase);

        foreach (var term in terms)
        {
            var answer = search.Search(term, repositoryAlias, limit: MaximumHits * RowsPerHit);
            if (answer.IsUnavailable) return ([], answer.UnavailableMessage);

            foreach (var group in answer.Hits.GroupBy(hit => hit.Chapter.Path, StringComparer.OrdinalIgnoreCase))
            {
                var best = group.MaxBy(hit => hit.Score)!;

                chapters[group.Key] = chapters.TryGetValue(group.Key, out var known)
                    ? (best.Score > known.Best ? best : known.Hit, Math.Max(known.Best, best.Score), known.Terms + 1)
                    : (best, best.Score, 1);
            }
        }

        return ([
            .. chapters.Values
                .OrderByDescending(entry => entry.Best)
                .ThenByDescending(entry => entry.Terms)
                .Select(entry => entry.Hit)
                .Take(MaximumHits)
        ], null);
    }

    private async Task<ChapterRecord?> LoadOpenChapterAsync(string? repositoryAlias, CancellationToken cancellationToken)
    {
        if (!openChapter.HasChapter) return null;

        var record = await LoadAsync(repositoryAlias, openChapter.AreaKey!, openChapter.ChapterPath!, cancellationToken).ConfigureAwait(false);
        return record is null ? null : record with { Pinned = true };
    }

    private async Task<ChapterRecord?> LoadAsync(string? repositoryAlias, string areaKey, string relativePath, CancellationToken cancellationToken)
    {
        var location = folders.Resolve(DevbookAreaCatalog.FolderKey(areaKey), repositoryAlias);
        var content = await DevbookChapterContent.LoadAsync(areaKey, location, relativePath, cancellationToken).ConfigureAwait(false);

        if (content.Chapter is null || content.Text is null) return null;

        // Headed by the path as a devbook reference is written — the folder with
        // its leading dot, then the file, the form DevbookChapterLink reads — so
        // the assistant can cite the chapter the way the devbook does.
        var heading = $"### {location.Key}/{content.Chapter.RelativePath.Replace('\\', '/')}";

        return new ChapterRecord(content.Chapter.AreaKey, content.Chapter.RelativePath, heading + "\n" + ForContext(content.Text).Trim(), Pinned: false);
    }

    /// <summary>
    /// A chapter as a model may be handed it for context: the file, less every
    /// devbook <c>annotation</c> fence and less the <c>review</c>,
    /// <c>reviewer</c> and <c>review-at</c> lines of its <c>meta</c> fences.
    /// <para>
    /// This is the one reader in the product that loads a chapter <em>for
    /// context</em>, and <c>devbook-annotations.md</c> is explicit about what such
    /// a reader does: it "skips every annotation fence, and the <c>review</c>,
    /// <c>reviewer</c>, and <c>review-at</c> fields beside it". Notes live in the
    /// canonical file, so a reviewer's open question — "is this still true?" —
    /// ingested as context becomes the rule, and a chapter somebody has asked a
    /// review of has not thereby stopped saying what it says. The MCP server's
    /// ordinary read makes the same cut (<c>ChapterReading</c>); this is the Ask AI
    /// half of it.
    /// </para>
    /// <para>
    /// Only those. The rest of the record — the status, the relations — is
    /// what a model citing the chapter needs, and a code listing or a diagram is
    /// the chapter. Nothing is written back: the cut is of the text in hand, and
    /// the file keeps its notes for the review they belong to.
    /// </para>
    /// <para>
    /// Fences are found by CommonMark's rule (<see cref="MarkdownFence"/>), so a
    /// note quoting a code sample is cut whole rather than at the sample.
    /// </para>
    /// </summary>
    internal static string ForContext(string markdown)
    {
        var lines = markdown.Replace("\r\n", "\n").Split('\n');
        var kept = new List<string>(lines.Length);

        for (var index = 0; index < lines.Length; index++)
        {
            if (MarkdownFence.Open(lines[index]) is not { } fence)
            {
                kept.Add(lines[index]);
                continue;
            }

            var note = DevbookAnnotationFence.IsAnnotationBlock(fence.Language);
            var meta = MetadataReader.IsMetaBlock(fence.Language);
            var inReviewField = false;

            if (!note) kept.Add(lines[index]);

            for (index++; index < lines.Length && !fence.IsClosedBy(lines[index]); index++)
            {
                if (note) continue;
                if (meta && IsReviewLine(lines[index], ref inReviewField)) continue;

                kept.Add(lines[index]);
            }

            // The closing line, when the fence had one. An unterminated fence ran
            // to the end of the file, and so does the cut.
            if (index < lines.Length && !note) kept.Add(lines[index]);
        }

        return string.Join('\n', kept);
    }

    /// <summary>Whether a line inside a <c>meta</c> fence belongs to one of the
    /// review fields — its own <c>key:</c> line, or an indented or list line
    /// continuing it — tracking which field the reading is in across calls.</summary>
    private static bool IsReviewLine(string line, ref bool inReviewField)
    {
        if (line.Trim().Length == 0) return inReviewField = false;

        var continues = char.IsWhiteSpace(line[0]) || line.StartsWith('-');
        if (continues) return inReviewField;

        var separator = line.IndexOf(':');
        inReviewField = separator > 0
            && DevbookSchema.ReviewFields.Contains(line[..separator].Trim(), StringComparer.OrdinalIgnoreCase);

        return inReviewField;
    }

    /// <summary>One chapter as the body carries it.</summary>
    /// <param name="Cut">Whether <see cref="FitTo"/> took the end off it.</param>
    private sealed record ChapterRecord(string AreaKey, string RelativePath, string Text, bool Pinned, bool Cut = false)
    {
        public bool SameChapter(string areaKey, string relativePath) =>
            string.Equals(AreaKey, areaKey, StringComparison.Ordinal)
            && string.Equals(RelativePath.Replace('\\', '/'), relativePath.Replace('\\', '/'), StringComparison.OrdinalIgnoreCase);

        /// <summary>This record, or as much of it as fits in <paramref name="characters"/>
        /// with the mark that says it was cut.</summary>
        public ChapterRecord FitTo(int characters)
        {
            if (Text.Length <= characters) return this;

            var keep = Math.Max(0, characters - Ellipsis.Length);
            return this with { Text = Text[..keep] + Ellipsis, Cut = true };
        }
    }
}
