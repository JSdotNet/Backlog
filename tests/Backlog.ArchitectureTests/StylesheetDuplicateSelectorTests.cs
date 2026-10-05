namespace Backlog.ArchitectureTests;

/// <summary>
/// A selector is declared in one top-level rule of a stylesheet, not several.
///
/// <para>The desktop stylesheet declared <c>.devbook-stack__section &gt;
/// .domain-devbook</c> in three rules a hundred lines apart, and two of them
/// disagreed: one said <c>max-height: none; overflow: visible</c>, the next
/// <c>max-height: 100%; overflow: hidden</c>. Only the last applied, and nobody
/// editing either could see that from where they stood. Finding a selector and
/// editing what is there is how a stylesheet gets changed, and a second
/// declaration is the one that edit does not find (issue #760).</para>
///
/// <para>The duplicates that predate this test are listed with the reason each
/// stays — mostly a selector in a grouped list its siblings share, beside its own
/// rule for the rest, where folding would copy the shared declarations rather than
/// remove anything. The list is a ratchet: a new duplicate fails, a duplicate
/// declared in more rules than listed fails, and an entry whose duplicate has been
/// folded fails until it is removed, so the list only ever shrinks.</para>
/// </summary>
public class StylesheetDuplicateSelectorTests
{
    private const string App = "src/App/Backlog.Desktop.UI/wwwroot/app.css";
    private const string Components = "src/Core/Backlog.UI.Components/wwwroot/components.css";
    private const string Capture = "src/Modules/Capture/Backlog.Modules.Capture.UI/wwwroot/capture.css";

    /// <summary>A duplicate this test tolerates: the selector, how many top-level
    /// rules declare it, and why it has not been folded.</summary>
    private sealed record ToleratedDuplicate(string Selector, int Rules, string Reason);

    private const string SharedWithSiblings =
        "Listed in a grouped rule its siblings share; folding would copy those declarations into a rule of its own.";

    private const string TaskItemTransition =
        "At the transition rule, the row's background and border would follow .task-item--selected at equal specificity and override it.";

    private const string UniversalSplit =
        "box-sizing and the scrollbar colours are two concerns, and the scrollbar rule carries its own long comment.";

    private const string FoldLabelSplit =
        "The flex sizing sits with the FoldControl trigger and the ellipsis with the generic fold names, each under its own comment.";

    private const string PlotPlacement =
        "The grid placements sit beside the comment on the plot grid that places them, apart from each part's own styling.";

    private static readonly Dictionary<string, ToleratedDuplicate[]> Tolerated = new(StringComparer.Ordinal)
    {
        [App] =
        [
            new(".app-header__identity", 2, SharedWithSiblings),
            new(".app-header__nav", 2, SharedWithSiblings),
            new(".app-header__status", 2, SharedWithSiblings),
            new(".app-header__utilities", 2, SharedWithSiblings),
            new(".entry-doc__tags", 2, SharedWithSiblings),
            new(".entry-doc__meta-end", 2, SharedWithSiblings),
            new(".entry-doc__schedule-input", 2, SharedWithSiblings),
            new(".repo-card__header .btn", 2, SharedWithSiblings),
            new(".header-group__option", 2, SharedWithSiblings),
            new(".header-accent", 2, SharedWithSiblings),
            new(".header-accent:focus-visible", 2, SharedWithSiblings),
            new(".header-util:focus-visible", 2, SharedWithSiblings),
            new(".inbox-pane__header", 2, SharedWithSiblings),
            new(".inbox-pane__body", 2, SharedWithSiblings),
            new(".inbox-pane__eyebrow", 2, SharedWithSiblings),
            new(".inbox-pane__title", 2, SharedWithSiblings),
            new(".inbox-pane__subtitle", 2, SharedWithSiblings),
            new(".inbox-pane__capture-result", 2, SharedWithSiblings),
            new(".inbox-pane__files", 2, SharedWithSiblings),
            new(".tools-panel__eyebrow", 2, SharedWithSiblings),
            new(".tools-panel__subtitle", 2, SharedWithSiblings),
            new(".tools-panel__message", 2, SharedWithSiblings),
            new(".tools-panel__count", 2, SharedWithSiblings),
            new(".tools-inventory__meta", 2, SharedWithSiblings),
            new(".roadmap-band__eyebrow", 2, SharedWithSiblings),
            new(".roadmap-band__title", 2, SharedWithSiblings),
            new(".roadmap-pace__note", 2, SharedWithSiblings),
            new(".roadmap-pace__value", 2, SharedWithSiblings),
            new(".roadmap-order__meta", 2, SharedWithSiblings),
            new(".roadmap-order__points", 2, SharedWithSiblings),
            new(".dashboard-panel__eyebrow", 2, SharedWithSiblings),
            new(".dashboard-panel__title", 2, SharedWithSiblings),
            new(".sessions-panel__eyebrow", 2, SharedWithSiblings),
            new(".sessions-panel__title", 2, SharedWithSiblings),
            new(".sessions-panel__subtitle", 2, SharedWithSiblings),
            new(".backlog-workspace", 2, SharedWithSiblings),
            new(".side-pane-stack", 2, SharedWithSiblings),
            new(".devbook-stack", 2, SharedWithSiblings),
            new(".devbook-pane", 2, SharedWithSiblings),
            new(".devbook-pane__subtitle", 2, SharedWithSiblings),
            new(".devbook-pane__source", 2, SharedWithSiblings),
            new(".devbook-empty", 2, SharedWithSiblings),
            new(".devbook-item", 2, SharedWithSiblings),
            new(".devbook-stack__nav", 2, SharedWithSiblings),
            new(".tech-devbook-pane", 2, SharedWithSiblings),
            new(".tech-layer-tab--active", 2, SharedWithSiblings),
            new(".tech-node-grid", 2, SharedWithSiblings),
            new(".tech-layer__header", 2, SharedWithSiblings),
            new(".tech-node__header", 2, SharedWithSiblings),
            new(".tech-node__meta", 2, SharedWithSiblings),
            new(".devbook-stack__header", 2, SharedWithSiblings),
            new(".devbook-stack__section > .devbook-pane", 2, SharedWithSiblings),
            new(".devbook-stack__section > .folder-devbook", 2, SharedWithSiblings),
            new(".folder-devbook__documents", 2, SharedWithSiblings),
            new(".folder-document", 2, SharedWithSiblings),
            new(".folder-section", 4, SharedWithSiblings),
            new(".domain-document", 2, SharedWithSiblings),
            new(".ai-loop__label", 2, SharedWithSiblings),
            new(".ai-loop__lobe", 2, SharedWithSiblings),
            new(".tech-atlas-note", 2, SharedWithSiblings),
        ],
        [Components] =
        [
            new("*", 2, UniversalSplit),
            new("button.badge", 2, SharedWithSiblings),
            new(".badge--feature-dev", 2, SharedWithSiblings),
            new(".badge--feature-beta", 2, SharedWithSiblings),
            new(".badge--outlook-finished", 2, SharedWithSiblings),
            new(".badge--outlook-behind", 2, SharedWithSiblings),
            new(".badge--outlook-overdue", 2, SharedWithSiblings),
            new(".md-compare-fold", 2, SharedWithSiblings),
            new(".fold__label", 2, FoldLabelSplit),
            new(".info-hint__trigger:focus-visible", 2, SharedWithSiblings),
            new(".grow-wrap::after", 2, SharedWithSiblings),
            new(".md-table th", 2, SharedWithSiblings),
            new(".devbook-related", 2, SharedWithSiblings),
            new(".devbook-status", 2, SharedWithSiblings),
            new(".devbook-menu__open-vscode:focus-visible", 2, SharedWithSiblings),
            new(".devbook-menu__item", 2, SharedWithSiblings),
            new(".folder-tree__item", 2, SharedWithSiblings),
            new(".devbook-menu__item:focus-visible", 2, SharedWithSiblings),
            new(".devbook-menu__item--active", 2, SharedWithSiblings),
            new(".folder-tree__item:focus-visible", 2, SharedWithSiblings),
            new(".folder-tree__item--active", 2, SharedWithSiblings),
            new(".md-block__comment:focus-visible", 3, SharedWithSiblings),
            new(".task-item", 2, TaskItemTransition),
            new(".task-panel__check", 2, SharedWithSiblings),
            new(".task-item__body-edit:focus-visible", 2, SharedWithSiblings),
            new(".task-item__edit:focus-visible", 2, SharedWithSiblings),
            new(".task-item__delete:focus-visible", 2, SharedWithSiblings),
            new(".task-list__completed-header:focus-visible", 2, SharedWithSiblings),
            new(".task-action__clear:focus-visible", 2, SharedWithSiblings),
            new(".markdown-editor__button:focus-visible", 2, SharedWithSiblings),
            new(".markdown-editor__input", 2, SharedWithSiblings),
            new(".markdown-editor__highlight", 2, SharedWithSiblings),
            new(".diagram-view__rendered", 2, SharedWithSiblings),
            new(".tech-graph__canvas", 2, SharedWithSiblings),
            new(".diagram-view__fallback", 2, SharedWithSiblings),
            new(".tech-graph__header p", 2, SharedWithSiblings),
            new(".app-update-dialog__description", 2, SharedWithSiblings),
            new(".feedback-dialog__description", 2, SharedWithSiblings),
            new(".menu-list__item:focus-visible", 2, SharedWithSiblings),
            new(".file-view", 2, SharedWithSiblings),
            new(".folder-view", 2, SharedWithSiblings),
            new(".file-view__header", 2, SharedWithSiblings),
            new(".file-view__identity", 2, SharedWithSiblings),
            new(".file-view__name", 2, SharedWithSiblings),
            new(".folder-view__body", 2, SharedWithSiblings),
            new(".change-scope__group", 2, SharedWithSiblings),
            new(".changed-file", 2, SharedWithSiblings),
            new(".md-compare-section__head--added", 2, SharedWithSiblings),
            new(".md-compare-section__head--removed", 2, SharedWithSiblings),
            new(".md-compare-section__head--changed", 2, SharedWithSiblings),
            new(".md-compare-section__was", 2, SharedWithSiblings),
            new(".md-compare-section__now", 2, SharedWithSiblings),
            new(".md-compare-block--added", 2, SharedWithSiblings),
            new(".md-compare-block--removed", 2, SharedWithSiblings),
            new(".md-compare-block--changed", 2, SharedWithSiblings),
            new(".open-folder:focus-visible", 2, SharedWithSiblings),
            new(".folder-view__open:focus-visible", 2, SharedWithSiblings),
            new(".graph-explorer__zoom", 2, SharedWithSiblings),
            new(".graph-explorer__tab:focus-visible", 2, SharedWithSiblings),
            new(".graph-explorer__zoom-button:focus-visible", 2, SharedWithSiblings),
            new(".graph-explorer__branch--left::after", 2, SharedWithSiblings),
            new(".graph-explorer__branch--right::before", 2, SharedWithSiblings),
            new(".graph-explorer__cloud-links", 2, SharedWithSiblings),
            new(".graph-explorer__cloud-hub", 2, SharedWithSiblings),
            new(".graph-explorer__cloud-node", 2, SharedWithSiblings),
            new(".graph-explorer__cloud-node:focus-visible", 2, SharedWithSiblings),
            new(".graph-explorer__card:focus-visible", 2, SharedWithSiblings),
            new(".graph-explorer__card-summary", 2, SharedWithSiblings),
            new(".graph-explorer__card-relations", 2, SharedWithSiblings),
            new(".metric-heatmap__label", 2, SharedWithSiblings),
            new(".metric-stacked-bars__column-track--grouped .metric-stacked-bars__segment", 2, SharedWithSiblings),
            new(".metric-stacked-bars__tip-total-value", 2, SharedWithSiblings),
            new(".metric-score__opaque", 2, SharedWithSiblings),
            new(".metric-stacked-area__scale", 2, PlotPlacement),
            new(".metric-stacked-area__canvas-wrap", 2, PlotPlacement),
            new(".metric-stacked-area__axis", 2, PlotPlacement),
            new(".roadmap-bar__title", 2, SharedWithSiblings),
            new(".roadmap-bar__badge", 2, SharedWithSiblings),
            new(".roadmap-bar__unestimated", 2, SharedWithSiblings),
            new(".integration-link__auto-merge", 2, SharedWithSiblings),
            new(".graph-atlas-index__status", 2, SharedWithSiblings),
        ],
        // A module stylesheet starts with none, and a duplicate in it fails.
        [Capture] = []
    };

    public static TheoryData<string> Stylesheets => [App, Components, Capture];

    [Theory]
    [MemberData(nameof(Stylesheets))]
    public void No_selector_is_declared_in_more_top_level_rules_than_tolerated(string stylesheet)
    {
        var tolerated = Tolerated[stylesheet].ToDictionary(entry => entry.Selector, StringComparer.Ordinal);

        var offenders = CssRules.Duplicates(Read(stylesheet))
            .Where(duplicate => !tolerated.TryGetValue(duplicate.Key, out var entry) || duplicate.Value.Count > entry.Rules)
            .Select(duplicate => $"{duplicate.Key} (lines {string.Join(", ", duplicate.Value)})")
            .OrderBy(line => line, StringComparer.Ordinal)
            .ToList();

        Assert.True(
            offenders.Count == 0,
            $"{stylesheet} declares a selector in more than one top-level rule: {string.Join("; ", offenders)}. "
            + "Fold the declarations into one rule, at the later position, after checking that no rule between "
            + "them sets the same property on the same element at equal or higher specificity.");
    }

    [Theory]
    [MemberData(nameof(Stylesheets))]
    public void Every_tolerated_duplicate_is_still_declared_as_often_as_listed(string stylesheet)
    {
        var duplicates = CssRules.Duplicates(Read(stylesheet));

        var retired = Tolerated[stylesheet]
            .Where(entry => !duplicates.TryGetValue(entry.Selector, out var lines) || lines.Count < entry.Rules)
            .Select(entry => $"{entry.Selector} (listed as {entry.Rules} rules, now "
                + $"{(duplicates.TryGetValue(entry.Selector, out var lines) ? lines.Count : 1)})")
            .ToList();

        Assert.True(
            retired.Count == 0,
            $"The tolerated list for {stylesheet} overstates {string.Join("; ", retired)}. Lower or remove the "
            + "entry, so the duplicate cannot come back unnoticed.");
    }

    [Fact]
    public void Every_tolerated_duplicate_says_why_it_stays()
    {
        var unexplained = Tolerated.Values
            .SelectMany(entries => entries)
            .Where(entry => string.IsNullOrWhiteSpace(entry.Reason) || entry.Rules < 2)
            .Select(entry => entry.Selector)
            .ToList();

        Assert.Empty(unexplained);
    }

    /// <summary>The contradiction the issue was opened for, settled the way the
    /// cascade already settled it: the last of the three rules won, so the panel
    /// clips at the section's height and its document scrolls inside it.</summary>
    [Fact]
    public void The_domain_panel_in_the_devbook_stack_has_one_rule_with_the_values_that_won()
    {
        const string selector = ".devbook-stack__section > .domain-devbook";

        var rules = CssRules.TopLevel(Read(App))
            .Where(rule => rule.Selectors.Contains(selector, StringComparer.Ordinal))
            .ToList();

        var rule = Assert.Single(rules);

        Assert.Equal(
            "background: color-mix(in srgb, var(--color-background-alt) 82%, var(--color-background)); "
            + "border: var(--border-width) solid var(--color-border); border-radius: var(--border-radius-lg); "
            + "box-shadow: var(--shadow-md); display: flex; flex-direction: column; max-height: 100%; min-height: 0; "
            + "overflow: hidden; padding: var(--spacing-md); position: static",
            Canonical(rule.Declarations.ToDictionary(d => d.Key, d => d.Value, StringComparer.Ordinal)));
    }

    /// <summary>Folding moves declarations, and must not change what they add up
    /// to. These are the values each folded selector — and each sibling that
    /// shared a grouped list with one — had from its top-level rules before the
    /// fold.</summary>
    [Theory]
    [InlineData(App, ".shell--wide", "margin: 0; max-width: none")]
    [InlineData(App, ".tools-panel__title", "color: var(--color-primary); font-size: var(--font-size-lg); margin: 0")]
    [InlineData(App, ".tools-panel__eyebrow", "color: var(--color-primary); font-size: var(--font-size-xs); font-weight: 700; letter-spacing: 0.08em; margin: 0; text-transform: uppercase")]
    [InlineData(App, ".tools-panel__subtitle", "color: var(--color-text-secondary); font-size: var(--font-size-sm); margin: 0")]
    [InlineData(App, ".tools-panel__message", "color: var(--color-text-secondary); font-size: var(--font-size-sm); margin: 0")]
    [InlineData(App, ".tech-node", "background: var(--color-background); border: var(--border-width) solid var(--color-border); border-left: 4px solid var(--color-border-strong); border-radius: var(--border-radius-md); display: grid; gap: var(--spacing-sm); padding: var(--spacing-md)")]
    [InlineData(App, ".design-token", "background: var(--color-background); border: var(--border-width) solid var(--color-border); border-radius: var(--border-radius-md); padding: var(--spacing-md)")]
    [InlineData(App, ".folder-section", "background: var(--color-background); border: var(--border-width) solid var(--color-border); border-radius: var(--border-radius-md); display: grid; gap: var(--spacing-md); grid-template-columns: minmax(0, 1fr); overflow-x: auto; padding: var(--spacing-md)")]
    [InlineData(App, ".domain-document", "background: var(--color-background); border: var(--border-width) solid var(--color-border); border-radius: var(--border-radius-md); display: grid; gap: var(--spacing-md); padding: var(--spacing-md)")]
    [InlineData(App, ".devbook-stack__section > .domain-devbook", "background: color-mix(in srgb, var(--color-background-alt) 82%, var(--color-background)); border: var(--border-width) solid var(--color-border); border-radius: var(--border-radius-lg); box-shadow: var(--shadow-md); display: flex; flex-direction: column; max-height: 100%; min-height: 0; overflow: hidden; padding: var(--spacing-md); position: static")]
    [InlineData(App, ".devbook-stack__header", "background: color-mix(in srgb, var(--color-background-alt) 82%, var(--color-background)); border: var(--border-width) solid var(--color-border); border-radius: var(--border-radius-lg); box-shadow: var(--shadow-md); display: grid; gap: 0.375rem; padding: var(--spacing-md)")]
    [InlineData(App, ".devbook-stack__section > .devbook-pane", "background: color-mix(in srgb, var(--color-background-alt) 82%, var(--color-background)); border: var(--border-width) solid var(--color-border); border-radius: var(--border-radius-lg); box-shadow: var(--shadow-md); max-height: none; overflow: visible; padding: var(--spacing-md); position: static")]
    [InlineData(App, ".devbook-stack__section > .folder-devbook", "background: color-mix(in srgb, var(--color-background-alt) 82%, var(--color-background)); border: var(--border-width) solid var(--color-border); border-radius: var(--border-radius-lg); box-shadow: var(--shadow-md); max-height: none; overflow: visible; padding: var(--spacing-md); position: static")]
    [InlineData(App, ".devbook-stack__section > .devbook-pane--arc42", "display: flex; flex-direction: column; max-height: 100%; min-height: 0; overflow: hidden")]
    [InlineData(App, ".devbook-stack__section > .instructions-devbook", "display: flex; flex-direction: column; max-height: 100%; min-height: 0; overflow: hidden")]
    [InlineData(App, ".devbook-stack__section > .folder-devbook--chapter", "display: flex; flex-direction: column; max-height: 100%; min-height: 0; overflow: hidden")]
    [InlineData(App, ".devbook-stack__section > .tech-devbook-pane", "max-height: 100%; min-height: 0; overflow: auto; scrollbar-gutter: stable")]
    [InlineData(App, ".repo-subpage", "align-items: center; background: var(--color-background); border: var(--border-width) solid var(--color-border); border-radius: var(--border-radius-md); color: var(--color-text-secondary); cursor: pointer; display: flex; font: inherit; font-size: var(--font-size-sm); gap: var(--spacing-sm); justify-content: space-between; padding: var(--spacing-sm) var(--spacing-md); text-align: left; transition: background var(--transition-fast), color var(--transition-fast), border-color var(--transition-fast); width: 100%")]
    [InlineData(App, ".repo-subpage:hover", "border-color: var(--color-border-strong); color: var(--color-text-primary)")]
    [InlineData(App, ".repo-subpage:focus-visible", "outline: var(--border-width-2) solid var(--color-border-focus); outline-offset: 2px")]
    [InlineData(App, ".repo-subpage--active", "background: rgba(242, 193, 78, 0.1); border-color: var(--color-primary); color: var(--color-text-primary)")]
    [InlineData(App, ".instructions-layout", "display: grid; gap: var(--spacing-md); grid-template-columns: minmax(14rem, 18rem) minmax(0, 1fr); min-width: 0")]
    [InlineData(App, ".instructions-list", "display: flex; flex-direction: column; gap: var(--spacing-xs)")]
    [InlineData(App, ".instructions-list__item", "background: var(--color-background); border: var(--border-width) solid var(--color-border); border-radius: var(--border-radius-md); color: var(--color-text-secondary); cursor: pointer; display: flex; flex-direction: column; font: inherit; gap: var(--spacing-xs); overflow-wrap: anywhere; padding: var(--spacing-sm); text-align: left")]
    [InlineData(App, ".instructions-list__item:hover", "border-color: var(--color-border-strong); color: var(--color-text-primary)")]
    [InlineData(App, ".instructions-list__item:focus-visible", "outline: var(--border-width-2) solid var(--color-border-focus); outline-offset: 2px")]
    [InlineData(App, ".instructions-list__item--active", "background: rgba(242, 193, 78, 0.1); border-color: var(--color-primary); color: var(--color-text-primary)")]
    [InlineData(App, ".instructions-list__title", "font-family: var(--font-family-mono); font-size: var(--font-size-sm); overflow-wrap: anywhere")]
    [InlineData(App, ".instructions-list__meta", "color: var(--color-text-disabled); font-size: var(--font-size-xs)")]
    [InlineData(App, ".instruction-doc__meta", "color: var(--color-text-disabled); font-size: var(--font-size-xs)")]
    [InlineData(App, ".instruction-doc", "background: var(--color-background); border: var(--border-width) solid var(--color-border); border-radius: var(--border-radius-md); min-width: 0")]
    [InlineData(App, ".instruction-doc__header", "align-items: flex-start; border-bottom: var(--border-width) solid var(--color-border); display: flex; gap: var(--spacing-md); justify-content: space-between; padding: var(--spacing-sm) var(--spacing-md)")]
    [InlineData(App, ".instruction-doc__title", "font-size: var(--font-size-base)")]
    [InlineData(App, ".instruction-doc__header code", "color: var(--color-text-disabled); overflow-wrap: anywhere")]
    [InlineData(App, ".instruction-doc__content", "color: var(--color-text-secondary); font-family: var(--font-family-mono); font-size: var(--font-size-sm); line-height: 1.6; margin: 0; max-height: min(72vh, 52rem); overflow: auto; padding: var(--spacing-md); white-space: pre-wrap")]
    [InlineData(Components, ".task-item__meta", "align-items: center; color: var(--color-text-secondary); display: flex; flex-wrap: wrap; font-size: var(--font-size-xs); gap: var(--spacing-sm); grid-column: 1 / -1; min-width: 0")]
    [InlineData(Components, ".split-pane__start", "grid-column: 1; min-height: 0; min-width: 0; overflow: auto")]
    [InlineData(Components, ".split-pane__separator", "grid-column: 2")]
    [InlineData(Components, ".split-pane__end", "grid-column: 3; min-height: 0; min-width: 0; overflow: auto")]
    [InlineData(Components, ".metric-trellis__change", "color: var(--chart-ink-muted); font-family: var(--font-family-base); font-size: var(--font-size-xs)")]
    [InlineData(Components, ".metric-trellis__detail", "color: var(--chart-ink-muted); font-family: var(--font-family-base); font-size: var(--font-size-xs); margin: 0; overflow: hidden; text-overflow: ellipsis; white-space: nowrap")]
    public void A_folded_selector_keeps_the_declarations_it_had(string stylesheet, string selector, string declarations)
    {
        Assert.Equal(declarations, Canonical(CssRules.Effective(Read(stylesheet), selector)));
    }

    [Fact]
    public void A_selector_written_twice_at_top_level_is_a_duplicate()
    {
        var duplicates = CssRules.Duplicates(
            ".a { color: red; }\n.b,\n.a { margin: 0; }\n.c { padding: 0; }");

        Assert.Equal(new[] { 1, 3 }, Assert.Contains(".a", duplicates));
        Assert.Single(duplicates);
    }

    [Fact]
    public void A_selector_inside_an_at_rule_is_a_separate_scope()
    {
        var duplicates = CssRules.Duplicates(
            ".a { color: red; }\n"
            + "@media (max-width: 40rem) { .a { color: blue; } }\n"
            + "@supports (display: grid) { .a { display: grid; } }\n"
            + "@container pane (min-width: 30rem) { .a { gap: 0; } }\n"
            + "@keyframes pulse { from { opacity: 0; } to { opacity: 1; } }\n"
            + "@media print { .b { color: black; } .b { margin: 0; } }");

        Assert.Empty(duplicates);
    }

    [Fact]
    public void Commas_inside_a_pseudo_class_and_comments_do_not_make_selectors()
    {
        var duplicates = CssRules.Duplicates(
            "/* .a { color: red; } */\n"
            + ".x:is(.a, .b) { color: red; }\n"
            + ".a { content: \"}\"; }\n"
            + ".x:is(.a, .b) { margin: 0; }");

        Assert.Equal(new[] { ".x:is(.a, .b)" }, duplicates.Keys);
    }

    private static string Read(string stylesheet) =>
        File.ReadAllText(Path.Combine(Repository.Root.FullName, stylesheet));

    private static string Canonical(IReadOnlyDictionary<string, string> declarations) =>
        string.Join("; ", declarations
            .OrderBy(declaration => declaration.Key, StringComparer.Ordinal)
            .Select(declaration => $"{declaration.Key}: {declaration.Value}"));
}
