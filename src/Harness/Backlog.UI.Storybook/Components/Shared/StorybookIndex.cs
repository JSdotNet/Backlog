using Backlog.UI.Components.Menus;

namespace Backlog.UI.Storybook.Components.Shared;

/// <summary>
/// The one list of pages in this storybook. The sidebar and the introduction
/// both render from here, because they used to hold two hand-maintained copies
/// of the same eleven links and a new page could be added to one and forgotten
/// in the other.
/// </summary>
/// <remarks>
/// The order is one rule: <b>nothing is shown before its parts</b>. The library is
/// read top to bottom by someone who has not met it, so a page may only use
/// components that a page above it has already introduced. That rule, not taste,
/// is what fixes the sequence:
/// <list type="number">
/// <item><b>Parts.</b> Input and action, Structure and navigation, Feedback,
/// Content, Knowledge base — the components that compose into everything
/// else.</item>
/// <item><b>Subjects.</b> Integrations, Task list, Roadmap, Dashboard — a set of
/// components that only mean something together, each its own chapter because
/// each is its own convention.</item>
/// <item><b>Combined usage.</b> The whole surfaces: a document, a folder, an
/// editor, a comparison. Last, because every one of them is several chapters
/// above it at once.</item>
/// </list>
/// <para>
/// The rule has exceptions, and each is a decision rather than an argument. The
/// file pane — <c>file-view</c> with <c>file-view/header</c> and
/// <c>file-view/content</c> under it — sits at the bottom of Content because the
/// owner put it there, not because everything it composes is above it. It is not,
/// and the nesting inverts the rule a second time: the pane is shown before the two
/// halves it is made of, because the owner asked for the halves to read as the pane
/// taken apart. <c>markdown/references</c> is in Content for
/// the same kind of reason: it is a page about an inline, and it draws components
/// the Knowledge base chapter two groups below introduces. Both are recorded where
/// they sit rather than argued away, and both notes say which way the rule was
/// bent.
/// </para>
/// <para>
/// Knowledge base and Integrations sit as high as the rule allows and no higher.
/// Both extend MarkdownView, so both have to follow Content; putting either above
/// it would show an extension of a component before the component.
/// </para>
/// <para>
/// Task list and Roadmap are chapters of their own rather than rows in a bigger
/// group. Each is a small family of components with a convention attached — how a
/// row states what it is waiting on, how a bar states which quarter it lands in —
/// and a convention read as one of nine sibling links is a convention nobody
/// reads.
/// </para>
/// <para>
/// What the pages themselves say is how a component is used. What it is allowed to
/// look like is in `.design`, linked from each page header — see
/// <see cref="DesignGuideline"/>.
/// </para>
/// </remarks>
internal static class StorybookIndex
{
    /// <summary>A page in the storybook. <paramref name="Summary"/> is the blurb
    /// on the introduction's card; the sidebar shows the title alone.
    /// <para>
    /// <paramref name="Exact"/> is for a page that has a subpage under its own
    /// path. The sidebar highlights on a prefix, so without it a parent stays lit
    /// while you are reading its child and two rows claim to be the current page.
    /// </para></summary>
    internal sealed record Page(string Href, string Title, string Summary, bool Exact = false);

    internal sealed record Group(string Title, IReadOnlyList<Page> Pages);

    public static IReadOnlyList<Group> Groups { get; } =
    [
        new("Overview",
        [
            new("", "Introduction", "Why the library exists and what this host is for."),
            new("foundations", "Foundations", "The colour, type, spacing and motion tokens every component is built from.")
        ]),

        // The smallest things first: a control that takes one value or performs
        // one act. Badges belong here rather than in Content, which is where they
        // used to sit. A badge is not something you read a document for — it is a
        // value with a class on it, sitting on the rows, headers and pickers this
        // group is about, and the pages that follow put one on almost everything.
        new("Input and action",
        [
            new("buttons", "Buttons", "AppButton, IconButton, ButtonGroup, ToggleButton, CopyButton."),
            new("inputs", "Inputs", "TextField, TextArea, SearchBox, Toggle, Checkbox — plus the dates, times and repeats a scheduled thing is set with, and TaskAction over them."),

            // Directly after Inputs and not folded into it. Every control on that
            // page hands back what the user typed; this one hands back the contents
            // of a file it read and size-checked itself, which is a different
            // bargain with the host and the only reason the component exists.
            new("file-field", "File field", "FileField: a picked file, read here and handed to the host as text."),

            new("selects", "Selects", "SelectField, BadgeSelect, EnumSelect, TagMultiSelect and the three ready-made selectors."),
            new("badges", "Badges", "Badge, StatusBadge, PriorityBadge, TagChip, MetadataBadge: a value, and the class that says what kind of value it is."),

            // Last in the group, because it is the one page here made out of the
            // others: the bar is a Checkbox from Inputs, an AppButton from
            // Buttons and a slot the host fills with whatever selectors it
            // needs. Nothing is shown before its parts, so it reads after all
            // three.
            new("selection-bar", "Selection bar", "SelectionBar: the count, the select-all, the way out of a selection, and a slot for whatever a host can do to every picked row at once.")
        ]),

        new("Structure and navigation",
        [
            new("layout", "Layout", "Tabs, Card, SectionHeader, FoldControl, SplitPane."),
            new("menus", "Menus", "TreeView, MenuList, ContextMenu, NavList, OpenFolderButton.")
        ]),

        // Ahead of Content because Content composes it: a document that saves says
        // so with a SaveIndicator, and an empty file view is an EmptyState. Small
        // utility components, so a short group — but a group whose parts the four
        // chapters below all reach for.
        new("Feedback",
        [
            new("feedback", "Feedback", "Alert, EmptyState, Spinner, Skeleton, SaveIndicator, Toast."),
            new("overlays", "Overlays", "Modal and ConfirmDialog.")
        ]),

        // The base content items — what a block of markdown, a snippet and a
        // diagram render as — and then, at the bottom, the file pane built out of
        // them. The other surfaces are still in Combined usage: a folder, a
        // document with comments, an editor beside its read view, a comparison.
        //
        // The file pane is here by the owner's call and not by the rule, and the
        // rule does not reach it: FileView composes markdown, code, a comparison,
        // badges, selects and a metadata record, and the record's own chapter is
        // two groups below this one. So the honest reading of this group is "the
        // content items, and then one surface placed by hand" — recorded rather
        // than argued away, because a comment arguing for an order the file does
        // not follow is worse than no comment at all. The argument for the call is
        // a reader's: someone looking for how to show a file looks under Content.
        //
        // Among the three file pages the rule does hold, which is why they are in
        // this order. File header and File content document the two halves the pane
        // is made of; File view is the container over them.
        new("Content",
        [
            new("markdown", "Markdown", "MarkdownView: every block and inline the read view renders, each beside the source that produced it.", Exact: true),
            new("markdown/diagrams", "Diagrams in markdown", "What happens when a fenced block names a diagram language: MarkdownView hands it to DiagramView."),
            new("markdown/rich-text", "Rich text editing", "MarkdownEditor: a formatting toolbar over the markdown source, and what each button writes."),

            // Two more subpages of Markdown, about two inlines the parser gives a
            // class of this product's own rather than a plain element. They are
            // here and not under Knowledge base because neither is a metadata
            // subject: a tag and a reference are both things an author writes in
            // the middle of a sentence, and the record is only the other place one
            // of them turns up.
            //
            // Tags before References, and by the rule: everything the Tags page
            // draws — TagChip, TagMultiSelect, the markdown inline itself — is in
            // Input and action or on the parent page above. References is where the
            // rule bends. It draws KnowledgeReferenceLink and a metadata record,
            // and the Knowledge base chapter that introduces both is two groups
            // below this one. Recorded rather than resolved, for the reason the
            // file pane's note gives: the owner's call is a reader's argument —
            // somebody who has just read what an inline renders as looks for what
            // a path in a sentence renders as in the same place — and the page is
            // about the inline, not about the field.
            new("markdown/tags", "Tags", "The `#tag` inline: what makes one, what it renders as, and why the chip in a body is not the TagChip component even though the two share a class."),
            new("markdown/references", "References", "A path written in a body: what a reference is, and the two places one appears — in prose, through the inline-target hook, and in a metadata record."),
            new("code", "Code", "CodeView: a snippet with syntax highlighting, line numbers and a copy button."),
            new("diagrams", "Diagrams", "DiagramView for mermaid, GraphView for node/edge data.", Exact: true),

            // Directly under the page that introduces DiagramView, because it puts
            // DiagramView beside something else and cannot be read before it. Not a
            // component chapter at all: it is evidence about a rule `.design` has
            // already settled, kept where the diagrams it compares are.
            new("diagrams/archify", "Mermaid beside Archify", "The same diagram drawn twice — the live mermaid render next to a generated Archify document — and what the second one adds, loses and costs."),

            // Beside its neighbour above and for the same reason: it puts DiagramView
            // to a use the Diagrams page does not introduce. Where that page compares
            // two renderers of one fence, this one shows a source that is not a fence
            // at all — a Structurizr workspace read into mermaid at render time.
            new("diagrams/c4", "C4 from Structurizr DSL", "One workspace authored in c4hero, drawn as a landscape, a context, a container and a deployment view — and what the reader refuses to guess at."),
            new("graph-explorer", "Graph explorer", "GraphExplorer: lanes, spine and cluster layouts over one model."),
            new("graph-atlas", "Graph atlas", "GraphAtlas: a graph drawn as a place — clustered in depth on a canvas, with the list beside it that the keyboard actually operates."),

            // Last of the base content items, and one of the places the ordering
            // rule is bent rather than followed: a DataTable cell is a template the caller
            // fills, and the examples fill some of theirs with a provider mark and a
            // state chip from the Integrations chapter below. The component is a base
            // content item — rows of records under headings — so it belongs here by
            // kind, and what it borrows is borrowed in exactly the part it declines to
            // own. Recorded rather than resolved: moving the page under Integrations
            // would file a table under a subject it is not about.
            new("data-table", "Data table", "DataTable: rows of records under column headings, in sections or flat — the frame is the component's and the cells are the caller's."),

            // The file pane, after every content item it draws. See the note on the
            // group for why it is in Content at all — and note that it is not the
            // only page here placed by hand: the markdown subpages above include one
            // that borrows from the Knowledge base chapter below, and the data table
            // borrows from Integrations.
            //
            // The pane first, then its two halves as subpages under it. This inverts
            // "nothing is shown before its parts" and does so on purpose: the owner
            // asked for the halves to sit under the pane, and a reader looking for how
            // to show a file looks for the pane, not for one end of it. What the
            // nesting buys is the relationship — the halves read as the pane taken
            // apart rather than as two more components in a list of nine — and it is
            // carried by the href alone, since a subpage's path under its parent's is
            // what the sidebar indents on.
            //
            // Header before content, and by the rule rather than by taste: the header
            // is where a file's own record is drawn, and the content page refers to it
            // for the identity the region does not carry.
            //
            // The pane is matched exactly, like every other page with a subpage under
            // its own path — without it the parent stays lit while you are reading a
            // half, and two rows claim to be the current page.
            new("file-view", "File view", "FileView: the container over both halves. What a file's name decides it is read as, and how edit, compare and read are arbitrated between.", Exact: true),
            new("file-view/header", "File header", "FileHeader: the half of a file pane that says which file this is and stays put — the path, the name, the file's own record, and the three things a reader does to it."),
            new("file-view/content", "File content", "FileContent: the half that holds the file and scrolls — prose or source, the frontmatter strip above it, a comparison, or a body the host brought.")
        ]),

        // Directly after Content, because that is what it extends: the chapter view
        // is MarkdownView with one fence read differently, so a reader arrives here
        // having just met the component being extended.
        //
        // The four pages document one convention from several angles — the meta
        // block, then what a status word in it means, then every field of it one at
        // a time, and last the two shapes a whole record is assembled into — so
        // they are read as a chapter and not picked out of a list. That is also
        // why the group is named for its subject rather than for a job, and why the
        // parent page is not named "Knowledge base" a second time: the group title
        // sits uppercased directly above it, and the same words twice in two type
        // styles read as a rendering fault.
        //
        // There were five. References left for Content, under Markdown, because a
        // reference is not only a metadata subject: an address written in the prose
        // of a chapter is the same reference drawn by the same component, and the
        // page that says so belongs beside the other inlines. What stays here is
        // which fields hold one, which is a fact about the record.
        new("Knowledge base",
        [
            new("knowledge-base", "Metadata", "The fenced meta block a knowledge chapter carries: every field drawn as a record, and the parameters that turn the drawing on. Opt-in throughout — nothing already rendering changed.", Exact: true),
            new("knowledge-base/state", "State", "What scoping a status buys: five folder vocabularies under one tone scale, a caller's own words where no folder applies, and a flag on a value that is in neither."),

            // After State rather than before it: two of its fourteen stories are
            // the status, and a reader who has just met the vocabularies recognises
            // the word instead of taking it on trust. Its `related`, `depends-on`
            // and `implements` stories draw addresses, and what an address is is on
            // Markdown → References, above this group.
            new("knowledge-base/fields", "Fields", "Every kind of metadata on its own: one story per field, the line an author writes, and the smallest component that draws that value."),

            // Last of the group, and last by the rule rather than by taste: it is
            // the first page that draws a whole record rather than one field, so
            // every shape on it was introduced by Fields directly above. The two
            // components it documents are the two ways a record is assembled — a
            // chapter's and a file's — and a reader who has not met the fields
            // would be reading a composition of parts they have not seen.
            //
            // It draws FileHeader too, beside the bare file shape, because a record
            // read without the surface it sits on is only half of what an author
            // wrote. That is the rule holding rather than bending: File header is in
            // Content, which is above this group, so the header has been introduced
            // by the time this page borrows it. It is also the reason this page no
            // longer precedes File view — the file pane moved above the whole
            // Knowledge base chapter, and the note on Content says why.
            new("knowledge-base/chapter-and-file", "Chapter and file", "The two shapes a whole record takes: a chapter's block folded into its heading, and a file's drawn wherever the surface says which file this is — bare, and in the header it was drawn for.")
        ]),

        // The second subject-named group with subpages, for the reason recorded
        // against the first: its five pages document one convention from several
        // angles — the same readiness-and-lifecycle idea explains the act, the
        // reference, the density rule and the AI proposal — so they are read as a
        // chapter rather than picked out of a list. It is a subject and not a job
        // for the same reason it cannot go anywhere else: its components span
        // input-and-action, content and feedback, and no job-named group can hold
        // it without splitting it across three.
        //
        // Here rather than lower, because it is one of the two chapters most often
        // opened to be followed rather than browsed; and not higher, because its AI
        // subpage extends MarkdownView the same way the chapter view above it does.
        new("Integrations",
        [
            new("integrations", "Availability and lifecycle", "Whether the product can perform an outward act at all, and if not why, plus the five states one act moves through. The substrate the other four pages are built on.", Exact: true),
            new("integrations/actions", "Actions", "The six acts the product performs on an external tool, the two hand-offs to an agent session, and which of them wear a provider mark."),
            new("integrations/references", "References", "Issues, pull requests and agent sessions that live outside the product: their state, their drift against local truth, and when it was last read."),
            new("integrations/density", "Density and overflow", "The same acts as a header toolbar, an inline row, an icon-only cluster and a set of menu items, and how the budget decides which."),
            new("integrations/ai", "AI in the document", "Rewriting a block and resolving a comment, attached to the comment model that already exists.")
        ]),

        // Its own chapter, out of Input and action where its pages used to be three
        // rows among seven. A task row is a composition — a checkbox, a title that
        // renames in place, a metadata line, badges, actions — so it belongs after
        // the parts, and the parts it is made of have stories of their own on
        // Inputs: the date, the time, the repeat, and TaskAction over them.
        //
        // The panel follows the row for the same reason the row follows the fields:
        // it is the whole of one task, so it is read after the list it opens from.
        new("Task list",
        [
            new("task-list", "Task list", "TaskItem and TaskListView: the controls a list of things-to-do is made of.", Exact: true),
            new("task-list/panel", "Task side panel", "TaskPanel, TaskActionPane and TaskAction: the whole of one task beside its list — a title you can tick and retitle, tags, the detail rows in two columns, then its sub-items or its markdown."),
            new("task-list/prompts", "Prompt tasks", "A body on a row, and the ids it waits on: a whole prompt on a task, and prompts chained so they run in order.")
        ]),

        // Its own chapter for the same reason Task list is: two components, one
        // convention — a plan against a quarter-ruled axis — and it was previously
        // the last of twelve rows under Content, where a timeline read as another
        // kind of content rather than as the one thing on the page.
        new("Roadmap",
        [
            new("roadmap", "Roadmap", "RoadmapTimeline and RoadmapTimelineBar: a plan against a quarter-ruled time axis, with swimlanes, dependency arrows and bars you can drag.")
        ]),

        new("Dashboard",
        [
            new("usage-metrics", "Usage metrics", "MetricTile, MetricGrid, MetricSparkline, MetricBars, MetricBreakdownBar, MetricMeter, MetricBreakdown, MetricStatus."),
            new("ai-usage", "AI usage and cost", "The same components composed into the view they were built for: what AI cost this fortnight, on what, and for whom."),
            new("productivity", "Productivity over time", "MetricScore, MetricTrellis, MetricHeatmap, MetricSpotlight, MetricStackedArea: a score, where the time went, and how one hue compares them across repositories.")
        ]),

        // Last, and last by the rule rather than by convention: every page here
        // composes several chapters above it into one surface, and a reader who
        // arrives having read them recognises the parts instead of meeting them for
        // the first time inside something bigger.
        //
        // Ordered among themselves by the same rule, with one gap left in it. The
        // document comes first, the folder that lists files after it, and the editor
        // and the comparison last — they are a document twice over, once as source
        // and once as rendering.
        //
        // The gap is the file the folder lists: File view moved to Content by the
        // owner's call, so the folder's "after the file that shows one" now points
        // at a page in another group. Folder view stays where it is, and the
        // asymmetry is deliberate rather than overlooked. A folder is not a base
        // content item — it is a surface over a tree of entries, and what it draws a
        // row with is TreeView from Menus rather than anything in Content — so the
        // argument that moved the file pane does not reach it, and moving it anyway
        // would be a second rearrangement nobody asked for. If it ever follows, the
        // sentence above is the one to fix.
        new("Combined usage",
        [
            new("markdown-document", "Markdown document", "MarkdownDocument and every option a read view takes: a way into the editor, a copy button, comments inline and comments in the margin."),
            new("folder-view", "Folder view", "FolderView with all of its options: a folder's header, and what is in it as a tree."),
            new("entry-edit", "Entry edit", "The same markdown being written: source beside read view, auto-save, task toggling, sub-items."),
            new("compare", "Section comparison",
                "ChangeScopePicker, ChangedFileList and MarkdownCompareView: which change to look at, which file, and what moved in it — aligned by heading, never by line.")
        ])
    ];

    /// <summary>Every page, flattened — what the introduction's card grid shows.</summary>
    public static IReadOnlyList<Page> AllPages { get; } =
        [.. Groups.SelectMany(group => group.Pages)];

    /// <summary>Every page except the introduction itself, which links to the rest.</summary>
    public static IReadOnlyList<Page> ComponentPages { get; } =
        [.. AllPages.Where(page => page.Href.Length > 0)];

    /// <summary>The sidebar's links for one group. Prefix matching is what keeps
    /// the right row highlighted for a whole-path href; the introduction and any
    /// page with a subpage under it are matched exactly instead, so a parent does
    /// not stay lit while its child is the page being read.</summary>
    public static IReadOnlyList<NavItem> NavItemsFor(Group group) =>
        [.. group.Pages.Select(page => new NavItem(page.Href, page.Title, Match: page.Href.Length == 0 || page.Exact))];
}
