using System.Text;

using Backlog.Modules.Devbook.Abstractions;
using Backlog.UI.Components.Markdown;
using Backlog.UI.Components.Metadata;

namespace Backlog.Infrastructure.Mcp;

/// <summary>
/// A chapter as the two reading modes see it, and the notes that hang off it.
/// <para>
/// Separated from the tool class because this is the whole of the interesting
/// behaviour and none of it needs a transport, a repository or a folder source to
/// exercise.
/// </para>
/// <para>
/// <b>Both modes answer with the file's own bytes.</b> The parse decides what a
/// block is and where it starts and stops; it never decides how a block is
/// spelled. An ordinary read is the file with two kinds of fence cut out of it by
/// line range, a review read is the file with the notes spliced into it, and
/// everything else in either answer is the author's text unchanged — the table
/// as it was aligned, the list as it was indented, the raw HTML, the
/// reference-style link and the setext heading the parser does not model. Writing
/// the parse back out would have made a session's next edit a reformat of the
/// chapter.
/// </para>
/// </summary>
internal static class ChapterReading
{
    /// <summary>
    /// The devbook convention's own note fence.
    /// <para>
    /// A constant here rather than a literal at the call site, beside
    /// <see cref="MetadataReader.FenceLanguage"/> which the component library
    /// already owns. It is the <em>other</em> kind of annotation — a repository
    /// artefact written into the chapter by the devbook plugin's own tool, never
    /// by this app (local ADR 0012 §6). This project only recognises it well
    /// enough to leave it out of an ordinary read.
    /// </para>
    /// </summary>
    internal const string AnnotationFenceLanguage = "annotation";

    /// <summary>
    /// The chapter spellings a note may have been filed under.
    /// <para>
    /// Two, because the panels disagree: the domain panel keys its notes by a
    /// repository-relative path (<c>.domain/sessions/domain.md</c>) and the arc42
    /// panel by an area-relative one (<c>adr/0012-….md</c>), each passing its own
    /// store's document path straight to
    /// <see cref="IDevbookAnnotationStore.List"/>. A tool that picked one spelling
    /// would answer nothing for half the chapters in the product, so it asks for
    /// both and merges. Neither is invented: both are spellings the store is
    /// holding today.
    /// </para>
    /// </summary>
    internal static IReadOnlyList<string> Spellings(string chapterPath, string? folderRelativePath)
    {
        var spellings = new List<string> { chapterPath };

        if (!string.IsNullOrEmpty(folderRelativePath)
            && !string.Equals(folderRelativePath, chapterPath, StringComparison.OrdinalIgnoreCase))
        {
            spellings.Add(folderRelativePath);
        }

        return spellings;
    }

    /// <summary>
    /// The notes on a chapter: live and typed into.
    /// <para>
    /// <c>List</c> already drops tombstones and keeps drafts, so the one filter
    /// left is the draft — a remark nobody has typed into, kept by the store so
    /// that closing the pane does not lose it, and never replicated. Neither
    /// existing method gives live-and-not-draft on its own.
    /// </para>
    /// </summary>
    internal static IReadOnlyList<DevbookAnnotation> Notes(
        IDevbookAnnotationStore store,
        string? repositoryAlias,
        IEnumerable<string> spellings)
    {
        var seen = new HashSet<Guid>();
        var notes = new List<DevbookAnnotation>();

        foreach (var spelling in spellings)
        {
            foreach (var note in store.List(repositoryAlias, spelling))
            {
                if (note.IsDraft) continue;
                if (!seen.Add(note.Id)) continue;

                notes.Add(note);
            }
        }

        return [.. notes.OrderBy(note => note.BlockIndex).ThenBy(note => note.CreatedAt)];
    }

    /// <summary>
    /// The chapter, in one mode or the other.
    /// <para>
    /// Parsed once. <paramref name="review"/> decides what is emitted and never
    /// what is numbered: an index is assigned over the full parse in both modes,
    /// so the fences an ordinary read leaves out do not shift the block after
    /// them. That is what makes a note's anchor mean the same thing to a session
    /// reading the chapter and to the panel the note was left in.
    /// </para>
    /// <para>
    /// Only <c>meta</c> and <c>annotation</c> fences are left out, and only
    /// outside review mode. A <c>mermaid</c> fence or a code listing stays: those
    /// are the chapter, and stripping them would hand a session a document the
    /// author would not recognise.
    /// </para>
    /// </summary>
    internal static ChapterPayload Build(
        string repositoryId,
        string repositoryAlias,
        string contextKey,
        string chapterPath,
        string? markdown,
        bool review,
        IReadOnlyList<DevbookAnnotation> notes)
    {
        var text = markdown ?? string.Empty;
        var blocks = MarkdownPreview.ParseDocument(text);
        var segments = ChapterSource.Lines(text);

        var emitted = new List<ChapterBlockPayload>(blocks.Count);

        for (var index = 0; index < blocks.Count; index++)
        {
            var block = blocks[index];

            if (!review && IsPrivateFence(block)) continue;

            // Both ends null together for a block the parse read from no lines —
            // the footnote block, collected from definitions scattered through
            // the body. Saying so out loud rather than answering 0 to 0, which a
            // caller cannot tell from a real empty block at the top of the file.
            emitted.Add(new ChapterBlockPayload(
                index,
                Kind(block),
                block.Source.IsKnown ? block.Source.StartLine : null,
                block.Source.IsKnown ? block.Source.EndLineExclusive : null,
                Language(block),
                review ? [.. notes.Where(note => note.BlockIndex == index).Select(Projections.Note)] : []));
        }

        // A note whose block has gone still has to land somewhere. Showing it at
        // the end says it is adrift, which is what MarkdownView already does with
        // one rather than dropping it — a lost note is worse than a stray one.
        var orphans = review
            ? notes
                .Where(note => note.BlockIndex < 0 || note.BlockIndex >= blocks.Count)
                .Select(Projections.Note)
                .ToList()
            : [];

        return new ChapterPayload(
            repositoryId,
            repositoryAlias,
            contextKey,
            chapterPath,
            review,
            review ? WithNotes(text, segments, blocks, notes) : WithoutPrivateFences(text, segments, blocks),
            blocks.Count,
            emitted,
            orphans);
    }

    /// <summary>
    /// The file, minus the lines its <c>meta</c> and <c>annotation</c> fences
    /// occupy. Nothing else is touched — not the blank lines that surrounded a
    /// removed fence, not a trailing space, not a line ending. A cut, not a
    /// rewrite.
    /// </summary>
    private static string WithoutPrivateFences(
        string text,
        IReadOnlyList<string> segments,
        IReadOnlyList<MdBlock> blocks)
    {
        var cuts = blocks
            .Where(block => IsPrivateFence(block) && block.Source.IsKnown)
            .Select(block => block.Source)
            .ToList();

        if (cuts.Count == 0) return text;

        var builder = new StringBuilder(text.Length);
        var next = 0;

        for (var line = 0; line < segments.Count; line++)
        {
            // The cuts arrive in source order and never overlap, so one cursor
            // walks them: past the current cut, take the next; inside it, skip.
            while (next < cuts.Count && line >= cuts[next].EndLineExclusive) next++;

            if (next < cuts.Count && line >= cuts[next].StartLine) continue;

            builder.Append(segments[line]);
        }

        return builder.ToString();
    }

    /// <summary>
    /// The file, unmodified, with each note spliced in after the last line of the
    /// block its <c>BlockIndex</c> names.
    /// <para>
    /// A note anchored past the end of the parse goes at the end rather than
    /// being dropped, and so does one anchored to the footnote block, which is
    /// collected from definitions scattered through the body and so names no
    /// lines to sit after. Both travel in <c>OrphanedNotes</c> or on their
    /// block's payload as well — a note is never only in the prose.
    /// </para>
    /// </summary>
    private static string WithNotes(
        string text,
        IReadOnlyList<string> segments,
        IReadOnlyList<MdBlock> blocks,
        IReadOnlyList<DevbookAnnotation> notes)
    {
        if (notes.Count == 0) return text;

        var newline = ChapterSource.Newline(text);
        var anchored = new Dictionary<int, List<DevbookAnnotation>>();
        var trailing = new List<DevbookAnnotation>();

        foreach (var note in notes)
        {
            var block = note.BlockIndex >= 0 && note.BlockIndex < blocks.Count ? blocks[note.BlockIndex] : null;

            if (block is null || !block.Source.IsKnown)
            {
                trailing.Add(note);
                continue;
            }

            var after = block.Source.EndLineExclusive - 1;

            if (!anchored.TryGetValue(after, out var here)) anchored[after] = here = [];

            here.Add(note);
        }

        var builder = new StringBuilder(text.Length);

        for (var line = 0; line < segments.Count; line++)
        {
            builder.Append(segments[line]);

            if (!anchored.TryGetValue(line, out var here)) continue;

            Splice(builder, here, newline);
        }

        Splice(builder, trailing, newline);

        return builder.ToString();
    }

    /// <summary>Writes a run of notes at the cursor, ending the line under them
    /// first where the file did not. A file with no final newline gains one when
    /// a note is spliced onto its last block — the alternative is a marker
    /// welded onto the author's last sentence.</summary>
    private static void Splice(StringBuilder builder, List<DevbookAnnotation> notes, string newline)
    {
        if (notes.Count == 0) return;

        if (builder.Length > 0 && builder[^1] != '\n') builder.Append(newline);

        foreach (var note in notes)
        {
            ChapterNoteMarkup.Append(builder, note, newline);
        }
    }

    /// <summary>What kind of block this is, in the vocabulary the payload
    /// publishes. A classification, never a serialization — the block's text
    /// comes from the file.</summary>
    internal static string Kind(MdBlock block) => block switch
    {
        MdHeading => "heading",
        MdParagraph => "paragraph",
        MdList => "list",
        MdQuote => "quote",
        MdCode => "code",
        MdDivider => "divider",
        MdTable => "table",
        MdFootnotes => "footnotes",
        MdSubItem => "sub-item",
        _ => "block"
    };

    /// <summary>A code fence's language, and null for every other block. What
    /// tells a caller that a <c>mermaid</c> block is a diagram and a <c>meta</c>
    /// block is a record.</summary>
    internal static string? Language(MdBlock block) =>
        block is MdCode { Language.Length: > 0 } code ? code.Language.Trim() : null;

    /// <summary>A fence an ordinary read leaves out: the chapter's own metadata
    /// record, and the devbook convention's review note. Asked of
    /// <see cref="MetadataReader"/> rather than compared against a literal, so the
    /// one place that decides what a <c>meta</c> block is keeps deciding it.</summary>
    private static bool IsPrivateFence(MdBlock block) =>
        block is MdCode code
        && (MetadataReader.IsMetaBlock(code.Language)
            || code.Language.Trim().Equals(AnnotationFenceLanguage, StringComparison.OrdinalIgnoreCase));
}
