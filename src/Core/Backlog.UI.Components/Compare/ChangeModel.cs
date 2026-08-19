using Backlog.UI.Components.Markdown;

namespace Backlog.UI.Components.Compare;

/// <summary>
/// The vocabulary the section-comparison feature speaks in: which change is
/// being looked at, which file, and what moved inside it.
///
/// <para>
/// One folder rather than three. The repository files by subject where a feature
/// is a set — <c>Knowledge/</c>, <c>Roadmap/</c>, <c>Diagrams/</c> — and by
/// widget kind only where a widget is genuinely generic — <c>Buttons/</c>,
/// <c>Inputs/</c>. Splitting the picker into <c>Menus/</c>, the file list into
/// <c>Layout/</c> and the view into <c>Markdown/</c> would scatter one chapter
/// across three folders and leave the storybook page as the only place the five
/// files are ever seen together.
/// </para>
/// <para>
/// The class stem is <c>md-compare-*</c> and the view is
/// <c>MarkdownCompareView</c>, deliberately not <c>md-diff-*</c> and not
/// <c>MarkdownDiffView</c>: <strong>this compares sections, it does not diff
/// text.</strong> A name with "diff" in it is a promise of a line-level view
/// this will never make good on, and the first person to point it at a
/// <c>.cs</c> file would find that out the hard way. Naming it <em>compare</em>
/// is the cheapest place to be honest about the boundary.
/// </para>
/// <para>
/// Everything here is data the host has already computed. No component in this
/// folder reads a repository, a file system or a git object; see the header on
/// each one.
/// </para>
/// </summary>
public enum ChangeKind
{
    /// <summary>Present on both sides and identical.</summary>
    Unchanged,

    /// <summary>Present only after.</summary>
    Added,

    /// <summary>Present only before.</summary>
    Removed,

    /// <summary>Present on both sides, and not the same.</summary>
    Changed
}

/// <summary>
/// One of the ranges at the top of the picker — "Committed", "Last commit",
/// "Uncommitted".
/// </summary>
/// <param name="Id">Identity within the picker's one id space. Scopes and
/// commits share it because exactly one thing is selected across both.</param>
/// <param name="Label">What the row is called.</param>
/// <param name="FileCount">How many files the range covers. Zero is a real,
/// selectable answer and not a reason to disable the row — see
/// <c>ChangeScopePicker</c>.</param>
public sealed record ChangeScope(string Id, string Label, int FileCount);

/// <summary>
/// One commit under the picker's divider.
/// </summary>
/// <param name="Id">Identity within the picker's one id space.</param>
/// <param name="ShortSha">The abbreviated hash. First in the row, because it is
/// the row's identity.</param>
/// <param name="Subject">The commit's first line. Second, because it is what a
/// human recognises.</param>
/// <param name="Age">How long ago, already written out — "36m ago". A display
/// string rather than a timestamp on purpose: a relative time computed inside
/// the component either goes stale on screen or needs a timer nobody asked for.
/// The host knows when it read the data and whether this is a live pane or a
/// fixture, so the host writes the words.</param>
public sealed record ChangeCommit(string Id, string ShortSha, string Subject, string Age);

/// <summary>
/// One file in the selected scope, with what happened to it.
/// </summary>
/// <param name="Path">The full path — the row's identity, and what a host keys
/// its selection on.</param>
/// <param name="Name">The file name, shown on the row's first line.</param>
/// <param name="Directory">The folders above it, shown on a second line. Null
/// for a file at the root.</param>
/// <param name="Kind">Added, Removed or Changed. Never <c>Renamed</c>: this
/// feature detects renames of <em>headings</em>, not of paths, so a
/// file-rename badge would be a claim the model cannot support.</param>
/// <param name="AddedSections">How many sections or blocks are new.</param>
/// <param name="RemovedSections">How many are gone.</param>
/// <param name="ChangedSections">How many were edited.</param>
public sealed record ChangedFile(
    string Path,
    string Name,
    string? Directory,
    ChangeKind Kind,
    int AddedSections,
    int RemovedSections,
    int ChangedSections);

/// <summary>
/// One block, on one or both sides of the comparison.
/// </summary>
/// <param name="Kind">What happened to it. <see cref="ChangeKind.Changed"/>
/// means both sides are present and differ, which is why the view stacks them.</param>
/// <param name="Before">The block as it was, or empty when it is new.</param>
/// <param name="After">The block as it is, or empty when it is gone.</param>
/// <remarks>
/// Both lists hold 0 or 1 elements today. They are lists rather than single
/// blocks so a <c>MarkdownView</c> can be handed one directly without allocating
/// a wrapper array on every render, and so a future many-to-one alignment has
/// somewhere to go.
/// </remarks>
public sealed record ComparedBlock(
    ChangeKind Kind,
    IReadOnlyList<MdBlock> Before,
    IReadOnlyList<MdBlock> After);

/// <summary>
/// One section of the document — a heading, its own prose, and the sections
/// written under it — aligned across the two versions.
/// </summary>
/// <param name="HeadingPath">The headings above and including this one, so a
/// host can address a section without walking the tree. The view does not print
/// it: the nesting on screen already says it, and a breadcrumb over a section
/// whose parents are two rows above repeats what the layout shows. The one case
/// where a breadcrumb would earn its place — a deep changed section whose
/// ancestors are folded away — cannot occur, because the collapse rule never
/// collapses a section with a change beneath it. The rule and the absent
/// breadcrumb are one decision.</param>
/// <param name="Level">The heading level, 1-6. The synthetic root that holds a
/// document's preamble is level 0.</param>
/// <param name="BeforeHeading">The heading text as it was, or null when the
/// section is new.</param>
/// <param name="AfterHeading">The heading text as it is, or null when the
/// section is gone.</param>
/// <param name="Kind">What happened to <em>the heading</em>, not to the subtree:
/// <see cref="ChangeKind.Added"/> when the heading is new,
/// <see cref="ChangeKind.Removed"/> when it is gone,
/// <see cref="ChangeKind.Changed"/> when its text was edited, and
/// <see cref="ChangeKind.Unchanged"/> otherwise. Body changes live in
/// <paramref name="Blocks"/> and heading changes further down live in
/// <paramref name="Children"/>, so a section reading Unchanged is not a claim
/// that nothing under it moved.</param>
/// <param name="Blocks">This section's own prose — the blocks between its
/// heading and its first child heading — aligned block by block.</param>
/// <param name="Children">The sections written under this one, aligned as
/// siblings.</param>
public sealed record ComparedSection(
    IReadOnlyList<string> HeadingPath,
    int Level,
    string? BeforeHeading,
    string? AfterHeading,
    ChangeKind Kind,
    IReadOnlyList<ComparedBlock> Blocks,
    IReadOnlyList<ComparedSection> Children)
{
    /// <summary>
    /// True when this section, its own prose and everything under it is
    /// <see cref="ChangeKind.Unchanged"/>. The single predicate the collapse
    /// rule is written against, so "a section with any change anywhere beneath
    /// it is never collapsed" is one expression rather than a walk repeated at
    /// each call site.
    /// </summary>
    /// <remarks>
    /// Computed rather than stored. A stored flag would have to be kept in step
    /// by every <c>with</c> expression, and a record whose derived field can
    /// disagree with its own data is worse than one that recomputes: the tree is
    /// a document, not a data set, and the walk is cheap next to rendering it.
    /// </remarks>
    public bool IsWhollyUnchanged =>
        Kind == ChangeKind.Unchanged
        && Blocks.All(block => block.Kind == ChangeKind.Unchanged)
        && Children.All(child => child.IsWhollyUnchanged);

    /// <summary>The heading to show: what it says now, falling back to what it
    /// said before for a section that was removed.</summary>
    public string? Heading => AfterHeading ?? BeforeHeading;
}
