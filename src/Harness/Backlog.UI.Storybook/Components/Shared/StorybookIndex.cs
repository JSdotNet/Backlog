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
/// <item><b>Combined usage.</b> The whole surfaces: a file, a folder, a document,
/// an editor, a comparison. Last, because every one of them is several chapters
/// above it at once.</item>
/// </list>
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
            new("selects", "Selects", "SelectField, BadgeSelect, EnumSelect, TagMultiSelect and the three ready-made selectors."),
            new("badges", "Badges", "Badge, StatusBadge, PriorityBadge, TagChip, MetadataBadge: a value, and the class that says what kind of value it is.")
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
            new("feedback", "Feedback", "Alert, EmptyState, Spinner, SaveIndicator, Toast."),
            new("overlays", "Overlays", "Modal and ConfirmDialog.")
        ]),

        // The base content items only: what a block of markdown, a snippet and a
        // diagram render as. The surfaces built out of them — a file, a folder, a
        // document with comments, an editor beside its read view, a comparison —
        // are in Combined usage, because each is several of these at once.
        new("Content",
        [
            new("markdown", "Markdown", "MarkdownView: every block and inline the read view renders, each beside the source that produced it.", Exact: true),
            new("markdown/diagrams", "Diagrams in markdown", "What happens when a fenced block names a diagram language: MarkdownView hands it to DiagramView."),
            new("markdown/rich-text", "Rich text editing", "MarkdownEditor: a formatting toolbar over the markdown source, and what each button writes."),
            new("code", "Code", "CodeView: a snippet with syntax highlighting, line numbers and a copy button."),
            new("diagrams", "Diagrams", "DiagramView for mermaid, GraphView for node/edge data."),
            new("graph-explorer", "Graph explorer", "GraphExplorer: lanes, spine and cluster layouts over one model."),

            // Last in the group, and the one place the ordering rule is bent rather
            // than followed: a DataTable cell is a template the caller fills, and the
            // examples fill some of theirs with a provider mark and a state chip from
            // the Integrations chapter below. The component is a base content item —
            // rows of records under headings — so it belongs here by kind, and what it
            // borrows is borrowed in exactly the part it declines to own. Recorded
            // rather than resolved: moving the page under Integrations would file a
            // table under a subject it is not about.
            new("data-table", "Data table", "DataTable: rows of records under column headings, in sections or flat — the frame is the component's and the cells are the caller's.")
        ]),

        // Directly after Content, because that is what it extends: the chapter view
        // is MarkdownView with one fence read differently, so a reader arrives here
        // having just met the component being extended.
        //
        // The three pages document one convention from several angles — the meta
        // block, then the two parts of it separable enough to adopt on their own —
        // so they are read as a chapter and not picked out of a list. That is also
        // why the group is named for its subject rather than for a job, and why the
        // parent page is not named "Knowledge base" a second time: the group title
        // sits uppercased directly above it, and the same words twice in two type
        // styles read as a rendering fault.
        new("Knowledge base",
        [
            new("knowledge-base", "Metadata", "The fenced meta block a knowledge chapter carries: every field drawn as a record, and the parameters that turn the drawing on. Opt-in throughout — nothing already rendering changed.", Exact: true),
            new("knowledge-base/references", "References", "What KnowledgeReferenceLink renders for an address, and the three shapes a host can ask for."),
            new("knowledge-base/state", "State", "What the folder parameter buys: five vocabularies under one tone scale, and a flag on a value that is in none.")
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
        // Ordered among themselves by the same rule. The document comes before the
        // file that shows one, the file before the folder that lists files, and the
        // editor and the comparison last — they are a document twice over, once as
        // source and once as rendering.
        new("Combined usage",
        [
            new("markdown-document", "Markdown document", "MarkdownDocument and every option a read view takes: a way into the editor, a copy button, comments inline and comments in the margin."),
            new("file-view", "File view", "FileView with all of its options: a file's header and actions over contents read as markdown or as code, with each chapter's status, diagrams and remarks drawn from the file itself."),
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
