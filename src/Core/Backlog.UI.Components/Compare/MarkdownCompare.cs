using System.Text.RegularExpressions;

using Backlog.UI.Components.Markdown;

namespace Backlog.UI.Components.Compare;

/// <summary>
/// Aligns two versions of a markdown document by their heading structure and
/// says, section by section and block by block, what moved.
///
/// <para>
/// <strong>Section granularity, and only section granularity.</strong> There is
/// no line-level text diff here and there never will be: this reads both sides
/// with <see cref="MarkdownPreview.ParseDocument"/> and compares the blocks that
/// come back. Point it at a <c>.cs</c> file and it will parse the file as prose
/// and tell you almost nothing, which is why the type is called
/// <em>Compare</em> rather than <em>Diff</em> — see <c>ChangeModel.cs</c>.
/// </para>
///
/// <para>
/// <strong>A renamed heading is one changed section, not a removal plus an
/// addition.</strong> A rename is one edit, and a reader looking at a comparison
/// is asking what changed — not what the alignment algorithm found hard. Keying
/// sections on heading text alone answers a <c>## Setup</c> renamed to
/// <c>## Installation</c> with the whole section removed and the whole section
/// added: every untouched paragraph beneath it is reported twice, once in red
/// and once in green, and the one word that actually changed is buried in a
/// hundred lines that did not. So a heading whose position among its siblings
/// and whose body still line up is read as a <em>changed heading</em>, and its
/// body is then aligned block by block underneath it — one changed row, and
/// below it only what really moved.
/// </para>
/// <para>
/// The cost is real and is accepted: a genuine delete-plus-insert landing in the
/// same slot with a similar body will be described as a rename. Nothing is
/// hidden by that — the reader still sees both heading texts and every body
/// difference — it is only labelled differently, and the labelling error is the
/// cheaper of the two. The opposite error, splitting one edit into two
/// whole-section reports, hides the change inside the noise it creates.
/// </para>
///
/// <para>
/// Pure and static. No Razor, no state, no clock, no I/O: the whole judgement
/// surface of this feature is one function of two strings, so it can be tested
/// as one — see <c>MarkdownCompareTests</c>.
/// </para>
/// </summary>
public static class MarkdownCompare
{
    /// <summary>
    /// How alike two headings, or two bodies, have to be before an unmatched
    /// pair in the same residual slot is read as a rename.
    /// </summary>
    /// <remarks>
    /// A starting point, tunable only against real documents — and deliberately
    /// <em>not</em> a <c>[Parameter]</c>. A threshold a host can set is a
    /// threshold that will differ between the desktop and the storybook, and
    /// then the rename rule is no longer one rule and two people reading the
    /// same comparison in two places disagree about what happened.
    /// </remarks>
    private const double SimilarityThreshold = 0.5;

    private static readonly Regex WhitespaceRuns = new(@"\s+", RegexOptions.Compiled);

    /// <summary>
    /// Compares two markdown bodies and hands back the synthetic level-0 root
    /// section: any preamble above the first heading is its own body, and the
    /// document's top-level headings are its children.
    /// </summary>
    public static ComparedSection Compare(string? before, string? after) =>
        Compare(MarkdownPreview.ParseDocument(before), MarkdownPreview.ParseDocument(after));

    /// <summary>
    /// The same comparison over blocks a caller has already parsed, for a host
    /// that is rendering the same document elsewhere and should not pay for a
    /// second parse.
    /// </summary>
    /// <remarks>
    /// Parse with <see cref="MarkdownPreview.ParseDocument"/> and not
    /// <see cref="MarkdownPreview.Parse"/>: the entry reading folds everything
    /// from the first <c>##</c> into an <c>MdSubItem</c>, which is most of any
    /// real file and would leave this comparing two documents of one section.
    /// </remarks>
    public static ComparedSection Compare(IReadOnlyList<MdBlock> before, IReadOnlyList<MdBlock> after) =>
        Align(BuildTree(before), BuildTree(after), []);

    /// <summary>
    /// A heading, the prose written directly under it, and the sections written
    /// under that. The parsed shape both sides are read into before anything is
    /// compared — the block list is flat, and the whole algorithm is about the
    /// tree it implies.
    /// </summary>
    private sealed class Section
    {
        /// <summary>1-6, or 0 for the synthetic root that holds a document's
        /// preamble and owns its top-level headings.</summary>
        public required int Level { get; init; }

        /// <summary>Null on the root, which is the only section nobody wrote.</summary>
        public MdHeading? Heading { get; init; }

        /// <summary>The heading as flat, normalised text. Doubles as the display
        /// string, so what is compared and what is shown cannot drift.</summary>
        public string Text { get; init; } = string.Empty;

        /// <summary>The blocks between this heading and the first heading under
        /// it — the section's <em>own</em> body, descendants excluded.</summary>
        public List<MdBlock> Body { get; } = [];

        public List<Section> Children { get; } = [];
    }

    /// <summary>
    /// Reads a flat block list into the section tree the headings describe: a
    /// section is a heading plus every block until the next heading of the same
    /// or higher level.
    /// </summary>
    private static Section BuildTree(IReadOnlyList<MdBlock> blocks)
    {
        var root = new Section { Level = 0 };
        var open = new Stack<Section>();
        open.Push(root);

        foreach (var block in blocks)
        {
            if (block is not MdHeading heading)
            {
                open.Peek().Body.Add(block);
                continue;
            }

            // A heading closes every open section at its own level or deeper.
            // The root is never popped: a document may legally start at `###`,
            // and something has to own it.
            while (open.Count > 1 && open.Peek().Level >= heading.Level) open.Pop();

            var section = new Section
            {
                Level = heading.Level,
                Heading = heading,
                Text = Normalize(MarkdownRender.PlainText(heading.Content))
            };

            open.Peek().Children.Add(section);
            open.Push(section);
        }

        return root;
    }

    /// <summary>Two sections already established to be counterparts: their own
    /// bodies are aligned block by block, and their children are matched as
    /// siblings.</summary>
    private static ComparedSection Align(Section before, Section after, IReadOnlyList<string> parentPath)
    {
        var isRoot = before.Heading is null && after.Heading is null;

        IReadOnlyList<string> path = isRoot ? [] : [.. parentPath, after.Text];

        // The heading's own kind, not the subtree's. A section reading Unchanged
        // here is only saying nobody touched its heading; what happened
        // underneath is in Blocks and Children.
        var kind = isRoot || string.Equals(before.Text, after.Text, StringComparison.Ordinal)
            ? ChangeKind.Unchanged
            : ChangeKind.Changed;

        return new ComparedSection(
            path,
            after.Level,
            before.Heading is null ? null : before.Text,
            after.Heading is null ? null : after.Text,
            kind,
            AlignBlocks(before.Body, after.Body),
            AlignChildren(before.Children, after.Children, path));
    }

    /// <summary>
    /// Matches one pair of sibling lists, in three passes, and emits them in one
    /// merged order.
    /// </summary>
    /// <remarks>
    /// Matching is scoped to siblings under an already-matched parent — root
    /// against root, then children only against the children of their matched
    /// counterpart. That stops a rename in one chapter stealing a heading from
    /// another, and it is what makes the whole thing a single recursive descent
    /// rather than a global search over every heading in the document.
    /// </remarks>
    private static IReadOnlyList<ComparedSection> AlignChildren(
        IReadOnlyList<Section> before,
        IReadOnlyList<Section> after,
        IReadOnlyList<string> parentPath)
    {
        var pairs = new Dictionary<int, int>();
        var beforeTaken = new bool[before.Count];
        var afterTaken = new bool[after.Count];

        MatchExactText(before, after, pairs, beforeTaken, afterTaken);
        MatchRenames(before, after, pairs, beforeTaken, afterTaken);

        return Merge(before, after, pairs, beforeTaken, parentPath);
    }

    /// <summary>
    /// Pass 1 — same level, same heading text, compared ordinal and
    /// case-sensitive because a case change is a real edit and should show as
    /// one.
    /// </summary>
    /// <remarks>
    /// Duplicate heading texts pair the <em>n</em>-th occurrence in before with
    /// the <em>n</em>-th in after, in document order. No similarity scoring for
    /// duplicates: position is the only tiebreaker and it is the stable one. If
    /// before has three <c>## Notes</c> and after has two, the third falls
    /// through to pass 2 as an orphan.
    /// </remarks>
    private static void MatchExactText(
        IReadOnlyList<Section> before,
        IReadOnlyList<Section> after,
        Dictionary<int, int> pairs,
        bool[] beforeTaken,
        bool[] afterTaken)
    {
        var byText = new Dictionary<(string Text, int Level), Queue<int>>();

        for (var index = 0; index < after.Count; index++)
        {
            var key = (after[index].Text, after[index].Level);
            if (!byText.TryGetValue(key, out var queue)) byText[key] = queue = new Queue<int>();
            queue.Enqueue(index);
        }

        for (var index = 0; index < before.Count; index++)
        {
            var key = (before[index].Text, before[index].Level);
            if (!byText.TryGetValue(key, out var queue) || queue.Count == 0) continue;

            var counterpart = queue.Dequeue();
            pairs[counterpart] = index;
            beforeTaken[index] = true;
            afterTaken[counterpart] = true;
        }
    }

    /// <summary>
    /// Pass 2 — rename detection over what pass 1 left behind. The orphans are
    /// taken in document order and only the <em>k</em>-th before-orphan is ever
    /// considered against the <em>k</em>-th after-orphan: residual position,
    /// which is the closest thing to "the same slot" that survives an edit.
    /// </summary>
    /// <remarks>
    /// A pair becomes a rename when the levels match and either the heading
    /// texts or the own bodies clear <see cref="SimilarityThreshold"/>. Either
    /// half is enough because the two failure modes are opposite: a heading
    /// reworded over an untouched body scores nothing on text and everything on
    /// body, and a body rewritten under a lightly-edited heading scores the
    /// reverse.
    /// <para>
    /// <strong>Level changes are never renames.</strong> A <c>##</c> becoming a
    /// <c>###</c> moves the section in the outline and changes the heading path
    /// of every descendant, so calling it "changed" would leave the subtree
    /// aligned across two different paths and break the parent-scoped invariant
    /// the whole descent rests on. Remove-plus-add is the honest report; a
    /// heading that moved level really has moved.
    /// </para>
    /// </remarks>
    private static void MatchRenames(
        IReadOnlyList<Section> before,
        IReadOnlyList<Section> after,
        Dictionary<int, int> pairs,
        bool[] beforeTaken,
        bool[] afterTaken)
    {
        var beforeOrphans = Orphans(before.Count, beforeTaken);
        var afterOrphans = Orphans(after.Count, afterTaken);

        for (var slot = 0; slot < Math.Min(beforeOrphans.Count, afterOrphans.Count); slot++)
        {
            var candidateBefore = before[beforeOrphans[slot]];
            var candidateAfter = after[afterOrphans[slot]];

            if (candidateBefore.Level != candidateAfter.Level) continue;

            var alike = TextSimilarity(candidateBefore.Text, candidateAfter.Text) >= SimilarityThreshold
                || BodySimilarity(candidateBefore.Body, candidateAfter.Body) >= SimilarityThreshold;

            if (!alike) continue;

            pairs[afterOrphans[slot]] = beforeOrphans[slot];
            beforeTaken[beforeOrphans[slot]] = true;
            afterTaken[afterOrphans[slot]] = true;
        }
    }

    private static IReadOnlyList<int> Orphans(int count, bool[] taken) =>
        [.. Enumerable.Range(0, count).Where(index => !taken[index])];

    /// <summary>
    /// Pass 3 and the ordering. The after side sets the running order — it is
    /// the version the reader is looking at — and an unmatched before-section is
    /// emitted as a Removed row in the gap it belonged to, ahead of the
    /// additions in that same gap so a replacement reads "gone, then arrived".
    /// </summary>
    private static IReadOnlyList<ComparedSection> Merge(
        IReadOnlyList<Section> before,
        IReadOnlyList<Section> after,
        Dictionary<int, int> pairs,
        bool[] beforeTaken,
        IReadOnlyList<string> parentPath)
    {
        var merged = new List<ComparedSection>();
        var cursor = 0;

        void FlushRemovalsBefore(int limit)
        {
            while (cursor < limit)
            {
                if (!beforeTaken[cursor]) merged.Add(WholeSubtree(before[cursor], ChangeKind.Removed, parentPath));
                cursor++;
            }
        }

        for (var index = 0; index < after.Count; index++)
        {
            if (pairs.TryGetValue(index, out var counterpart))
            {
                FlushRemovalsBefore(counterpart);
                merged.Add(Align(before[counterpart], after[index], parentPath));
                cursor = Math.Max(cursor, counterpart + 1);
            }
            else
            {
                FlushRemovalsBefore(NextAnchor(after.Count, before.Count, pairs, index + 1));
                merged.Add(WholeSubtree(after[index], ChangeKind.Added, parentPath));
            }
        }

        FlushRemovalsBefore(before.Count);

        return merged;
    }

    /// <summary>Where the next matched pair lands on the before side, which is
    /// how far a run of removals is allowed to flush before the additions that
    /// share its gap.</summary>
    private static int NextAnchor(int afterCount, int beforeCount, Dictionary<int, int> pairs, int from)
    {
        for (var index = from; index < afterCount; index++)
        {
            if (pairs.TryGetValue(index, out var counterpart)) return counterpart;
        }

        return beforeCount;
    }

    /// <summary>
    /// A section that exists on one side only, reported <em>whole</em>: one row
    /// carrying its own prose and its descendants, rather than one row per
    /// heading underneath it. A deleted chapter is one deletion; listing its
    /// four subsections beside it would report one edit as five.
    /// </summary>
    private static ComparedSection WholeSubtree(Section section, ChangeKind kind, IReadOnlyList<string> parentPath)
    {
        IReadOnlyList<string> path = [.. parentPath, section.Text];

        var removed = kind == ChangeKind.Removed;

        return new ComparedSection(
            path,
            section.Level,
            removed ? section.Text : null,
            removed ? null : section.Text,
            kind,
            [.. section.Body.Select(block => removed
                ? new ComparedBlock(kind, [block], [])
                : new ComparedBlock(kind, [], [block]))],
            [.. section.Children.Select(child => WholeSubtree(child, kind, path))]);
    }

    /// <summary>
    /// Aligns the own-body blocks of two matched sections by fingerprint.
    /// </summary>
    /// <remarks>
    /// The two lists are walked together against a longest-common-subsequence
    /// table, so the blocks that survived are the anchors and everything between
    /// two anchors is one edit to describe. Inside such a gap, a before-only
    /// block facing an after-only block <em>of the same block type</em> is one
    /// Changed block; anything else is a Removed followed by an Added. Requiring
    /// the same type is what stops a deleted table and an inserted paragraph
    /// being reported as one edited block.
    /// <para>
    /// The table is what makes the walk positional in the useful sense rather
    /// than the literal one. Stepping the two lists index by index would report
    /// every block after an inserted paragraph as Changed — the whole section
    /// painted as an edit because one thing was added to the top of it, which is
    /// exactly the "hide the change inside the noise it creates" failure the
    /// rename rule exists to avoid, one level down.
    /// </para>
    /// </remarks>
    private static IReadOnlyList<ComparedBlock> AlignBlocks(
        IReadOnlyList<MdBlock> before,
        IReadOnlyList<MdBlock> after)
    {
        var beforeKeys = before.Select(Fingerprint).ToArray();
        var afterKeys = after.Select(Fingerprint).ToArray();

        var common = LongestCommonSubsequence(beforeKeys, afterKeys);

        var aligned = new List<ComparedBlock>();
        var pendingBefore = new List<MdBlock>();
        var pendingAfter = new List<MdBlock>();

        void DrainGap()
        {
            var paired = Math.Min(pendingBefore.Count, pendingAfter.Count);

            for (var index = 0; index < paired; index++)
            {
                if (pendingBefore[index].GetType() == pendingAfter[index].GetType())
                {
                    aligned.Add(new ComparedBlock(ChangeKind.Changed, [pendingBefore[index]], [pendingAfter[index]]));
                }
                else
                {
                    aligned.Add(new ComparedBlock(ChangeKind.Removed, [pendingBefore[index]], []));
                    aligned.Add(new ComparedBlock(ChangeKind.Added, [], [pendingAfter[index]]));
                }
            }

            for (var index = paired; index < pendingBefore.Count; index++)
            {
                aligned.Add(new ComparedBlock(ChangeKind.Removed, [pendingBefore[index]], []));
            }

            for (var index = paired; index < pendingAfter.Count; index++)
            {
                aligned.Add(new ComparedBlock(ChangeKind.Added, [], [pendingAfter[index]]));
            }

            pendingBefore.Clear();
            pendingAfter.Clear();
        }

        int left = 0, right = 0;

        while (left < beforeKeys.Length && right < afterKeys.Length)
        {
            if (string.Equals(beforeKeys[left], afterKeys[right], StringComparison.Ordinal))
            {
                DrainGap();
                aligned.Add(new ComparedBlock(ChangeKind.Unchanged, [before[left]], [after[right]]));
                left++;
                right++;
            }
            else if (common[left + 1, right] >= common[left, right + 1])
            {
                pendingBefore.Add(before[left]);
                left++;
            }
            else
            {
                pendingAfter.Add(after[right]);
                right++;
            }
        }

        while (left < beforeKeys.Length) pendingBefore.Add(before[left++]);
        while (right < afterKeys.Length) pendingAfter.Add(after[right++]);

        DrainGap();

        return aligned;
    }

    /// <summary>Suffix-indexed LCS lengths: <c>table[i, j]</c> is the longest
    /// common subsequence of <c>before[i..]</c> and <c>after[j..]</c>. Filled
    /// backwards so the walk above can read it forwards.</summary>
    private static int[,] LongestCommonSubsequence(string[] before, string[] after)
    {
        var table = new int[before.Length + 1, after.Length + 1];

        for (var left = before.Length - 1; left >= 0; left--)
        {
            for (var right = after.Length - 1; right >= 0; right--)
            {
                table[left, right] = string.Equals(before[left], after[right], StringComparison.Ordinal)
                    ? table[left + 1, right + 1] + 1
                    : Math.Max(table[left + 1, right], table[left, right + 1]);
            }
        }

        return table;
    }

    /// <summary>
    /// What makes two blocks the same block: the type name, plus the block's
    /// content flattened to plain text and normalised the way a heading is.
    /// </summary>
    /// <remarks>
    /// The type is part of the key rather than an afterthought, so a paragraph
    /// and a quote saying the same words are two blocks and not one — the
    /// difference is the author's, and a comparison that swallowed it would be
    /// reporting a document nobody wrote.
    /// </remarks>
    private static string Fingerprint(MdBlock block) => block.GetType().Name + "|" + Flatten(block);

    private static string Flatten(MdBlock block) => block switch
    {
        // Language first: the same lines fenced as `bash` and as `powershell`
        // are a real edit, and the reader is usually looking at the tag.
        MdCode code => Normalize(code.Language) + "|" + Normalize(code.Text),
        MdParagraph paragraph => Normalize(MarkdownRender.PlainText(paragraph.Content)),
        MdQuote quote => Normalize(MarkdownRender.PlainText(quote.Content)),
        MdHeading heading => heading.Level + "|" + Normalize(MarkdownRender.PlainText(heading.Content)),
        MdList list => (list.Ordered ? "ol" : "ul") + "|" + Normalize(FlattenItems(list.Items)),
        MdTable table => Normalize(FlattenRows(table)),
        MdFootnotes footnotes => Normalize(string.Join(
            " § ",
            footnotes.Notes.Select(note => note.Label + " " + MarkdownRender.PlainText(note.Content)))),
        // Defensive: ParseDocument never produces one, but Parse does, and a
        // caller handing us entry-parsed blocks should get a stable key rather
        // than every sub-item collapsing onto the type name.
        MdSubItem subItem => MarkdownRender.PlainText(subItem.Title),
        // A divider has no content, so the type alone is the whole fingerprint.
        _ => string.Empty
    };

    private static string FlattenItems(IReadOnlyList<MdListItem> items) => string.Join(
        " § ",
        items.Select(item =>
            // The tick is content: checking a box is an edit to the document,
            // and a fingerprint that ignored it would report the line unchanged.
            (item.Done is null ? string.Empty : item.Done == true ? "[x] " : "[ ] ")
            + MarkdownRender.PlainText(item.Content)
            + (item.Nested.Count == 0
                ? string.Empty
                : " » " + string.Join(" § ", item.Nested.Select(nested => FlattenItems(nested.Items))))));

    private static string FlattenRows(MdTable table) => string.Join(
        " § ",
        table.Rows.Prepend(table.Header).Select(row =>
            string.Join(" | ", row.Cells.Select(cell => MarkdownRender.PlainText(cell.Content)))));

    /// <summary>Trimmed, and internal whitespace runs collapsed to one space, so
    /// a reflowed paragraph that says the same words is the same block.</summary>
    private static string Normalize(string? value) =>
        string.IsNullOrEmpty(value) ? string.Empty : WhitespaceRuns.Replace(value, " ").Trim();

    /// <summary>
    /// Sørensen–Dice over the set of character bigrams of two normalised heading
    /// texts, lower-cased. Deterministic, allocation-cheap and dependency-free,
    /// which is the whole reason it is this and not an edit distance or a
    /// tokeniser.
    /// </summary>
    /// <remarks>
    /// A one-character heading has no bigrams at all, so the ratio is undefined
    /// rather than zero. It is defined here as 1 when the two texts are equal and
    /// 0 otherwise — the answer the caller wants, and the only one that does not
    /// make <c>## A</c> renamed to <c>## A</c> look unrelated.
    /// </remarks>
    private static double TextSimilarity(string before, string after)
    {
        var left = Bigrams(before.ToLowerInvariant());
        var right = Bigrams(after.ToLowerInvariant());

        if (left.Count == 0 || right.Count == 0)
        {
            return string.Equals(before, after, StringComparison.OrdinalIgnoreCase) ? 1d : 0d;
        }

        var shared = left.Count(bigram => right.Contains(bigram));

        return 2d * shared / (left.Count + right.Count);
    }

    private static HashSet<string> Bigrams(string value) =>
    [
        .. Enumerable.Range(0, Math.Max(0, value.Length - 1)).Select(start => value.Substring(start, 2))
    ];

    /// <summary>
    /// Dice over the multiset of own-body block fingerprints. Descendant
    /// sections are excluded — a rename is judged on the section's own prose,
    /// not on how much of its subtree happened to survive, or a chapter with one
    /// renamed heading and forty untouched subsections would score alike with
    /// anything.
    /// </summary>
    /// <remarks>
    /// Two empty bodies score 1: they do have identical bodies, and with the
    /// level and residual-position constraints that correctly pairs a renamed
    /// heading carrying no prose. The accepted consequence is that two unrelated
    /// empty headings in the same residual slot read as a rename rather than a
    /// delete and an insert. Both heading texts stay on screen, so it is a
    /// labelling difference and not lost information — and it is a named test,
    /// so nobody has to rediscover it.
    /// </remarks>
    private static double BodySimilarity(IReadOnlyList<MdBlock> before, IReadOnlyList<MdBlock> after)
    {
        if (before.Count == 0 && after.Count == 0) return 1d;
        if (before.Count == 0 || after.Count == 0) return 0d;

        var remaining = new Dictionary<string, int>(StringComparer.Ordinal);

        foreach (var block in before)
        {
            var key = Fingerprint(block);
            remaining[key] = remaining.GetValueOrDefault(key) + 1;
        }

        var shared = 0;

        foreach (var block in after)
        {
            var key = Fingerprint(block);
            if (!remaining.TryGetValue(key, out var left) || left == 0) continue;

            remaining[key] = left - 1;
            shared++;
        }

        return 2d * shared / (before.Count + after.Count);
    }
}
