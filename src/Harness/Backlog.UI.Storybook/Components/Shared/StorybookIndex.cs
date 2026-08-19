using Backlog.UI.Components.Menus;

namespace Backlog.UI.Storybook.Components.Shared;

/// <summary>
/// The one list of pages in this storybook. The sidebar and the introduction
/// both render from here, because they used to hold two hand-maintained copies
/// of the same eleven links and a new page could be added to one and forgotten
/// in the other.
/// </summary>
/// <remarks>
/// The groups are named after what the components in them do. An earlier
/// arrangement split them into "Requested" and "Rest of the library", which
/// recorded the order the work was commissioned in rather than anything a
/// reader of the library could act on.
/// <para>
/// "Knowledge base" is the one group named after a subject rather than a job,
/// and it is also the only one that is a parent page with subpages under it.
/// Both follow from the same thing: its three pages document one convention —
/// the meta block, then the two parts of it separable enough to adopt on their
/// own — so they are read as a chapter and not picked out of a list.
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
        new("Input and action",
        [
            new("buttons", "Buttons", "AppButton, IconButton, ButtonGroup, ToggleButton."),
            new("inputs", "Inputs", "TextField, TextArea, SearchBox, Toggle, Checkbox."),
            new("selects", "Selects", "SelectField, BadgeSelect, EnumSelect, TagMultiSelect, SearchBox."),
            new("task-list", "Task list", "TaskItem, TaskListView and TaskAction: the controls a list of things-to-do is made of.", Exact: true),
            new("task-list/prompts", "Prompt tasks", "A body on a row, and the ids it waits on: a whole prompt on a task, and prompts chained so they run in order.")
        ]),
        new("Structure and navigation",
        [
            new("layout", "Layout", "Tabs, Card, SectionHeader, FoldControl, SplitPane."),
            new("menus", "Menus", "TreeView, MenuList, ContextMenu, NavList.")
        ]),
        new("Content",
        [
            new("file-view", "File view", "FileView: a file's header, and its contents read as markdown or as code."),
            new("folder-view", "Folder view", "FolderView: a folder's header, and what is in it as a tree — the knowledge menu with the vocabulary taken out."),
            new("markdown", "Markdown", "MarkdownView: every block and inline the read view renders, and how each is styled.", Exact: true),
            new("markdown/diagrams", "Diagrams in markdown", "What happens when a fenced block names a diagram language: MarkdownView hands it to DiagramView."),
            new("markdown/rich-text", "Rich text editing", "MarkdownEditor: a formatting toolbar over the markdown source, and where it stops short of a WYSIWYG."),
            new("compare", "Section comparison",
                "ChangeScopePicker, ChangedFileList and MarkdownCompareView: which change to look at, which file, and what moved in it \u2014 aligned by heading, never by line."),
            new("entry-edit", "Entry edit", "The same markdown being written: source beside read view, auto-save, task toggling, sub-items."),
            new("code", "Code", "CodeView: a snippet with syntax highlighting, line numbers and a copy button."),
            new("badges", "Badges", "Badge, StatusBadge, PriorityBadge, TagChip, MetadataBadge."),
            new("diagrams", "Diagrams", "DiagramView for mermaid, GraphView for node/edge data."),
            new("graph-explorer", "Graph explorer", "GraphExplorer: lanes, spine and cluster layouts over one model."),
            new("roadmap", "Roadmap", "RoadmapTimeline and RoadmapTimelineBar: a plan against a quarter-ruled time axis, with swimlanes, dependency arrows and bars you can drag.")
        ]),
        new("Dashboard",
        [
            new("usage-metrics", "Usage metrics", "MetricTile, MetricGrid, MetricSparkline, MetricBars, MetricBreakdownBar, MetricMeter, MetricBreakdown, MetricStatus."),
            new("ai-usage", "AI usage and cost", "The same components composed into the view they were built for: what AI cost this fortnight, on what, and for whom."),
            new("productivity", "Productivity over time", "MetricScore, MetricTrellis, MetricHeatmap, MetricSpotlight, MetricStackedArea: a score, where the time went, and the ways one hue can compare them across repositories.")
        ]),

        // Its own group rather than three rows at the bottom of Content, and
        // placed directly after Content because that is what it is built on:
        // the chapter view is MarkdownView with one fence read differently, so
        // a reader arrives here having just met the component it extends.
        // Feedback stays last — it is a small utility group, and burying a
        // subject area behind it would read as an afterthought.
        //
        // The parent page is named for its subject rather than repeating the
        // group's name: the group title sits uppercased directly above it, and
        // the same words twice in two type styles read as a rendering fault.
        new("Knowledge base",
        [
            new("knowledge-base", "Metadata", "The fenced meta block a knowledge chapter carries: every field drawn as a record, and how MarkdownView and MetadataBadge were extended to draw it. Opt-in throughout — nothing already rendering changed.", Exact: true),
            new("knowledge-base/references", "References", "Why related, depends-on and implements hold addresses rather than labels, and what KnowledgeReferenceLink renders for each thing a host can do with one."),
            new("knowledge-base/state", "State", "Five folders spell their lifecycle five ways. What the folder parameter buys: the vocabulary, one tone scale under all of them, and a flag on a value that is in none.")
        ]),
        new("Feedback",
        [
            new("feedback", "Feedback", "Alert, EmptyState, Spinner, SaveIndicator, Toast."),
            new("overlays", "Overlays", "Modal and ConfirmDialog.")
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
