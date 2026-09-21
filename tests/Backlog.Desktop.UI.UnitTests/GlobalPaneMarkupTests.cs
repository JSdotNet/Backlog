namespace Backlog.Desktop.UI.UnitTests;

public sealed class GlobalPaneMarkupTests
{
    [Fact]
    public void Home_shell_exposes_global_pane_multiselect_and_sections()
    {
        var home = NormalizeLineEndings(File.ReadAllText(FindHomeRazor()));

        // The strip is the shared ButtonGroup, so its test id and its label reach
        // the DOM through the component's TestId and AriaLabel parameters rather
        // than as literal attributes — the same convention the update dialog and
        // the pane options below already follow.
        Assert.Contains("TestId=\"global-pane-multiselect\"", home, StringComparison.Ordinal);

        // "Panes", because that is all the strip holds now. It used to be
        // "Sections" while the roadmap band's option sat first in it; the band is a
        // row above the panes rather than one of them, and its toggle stands on its
        // own beside the strip — see below.
        Assert.Contains("AriaLabel=\"Visible panes\"", home, StringComparison.Ordinal);
        Assert.DoesNotContain("Visible sections", home, StringComparison.Ordinal);

        // The three pane options are the shared ToggleButton, so their test ids
        // reach the DOM through its TestId parameter rather than literal attributes.
        Assert.Contains("TestId=\"inbox-pane-option\"", home, StringComparison.Ordinal);
        Assert.Contains("TestId=\"backlog-pane-option\"", home, StringComparison.Ordinal);
        Assert.Contains("TestId=\"devbook-pane-option\"", home, StringComparison.Ordinal);

        // The roadmap toggle leads the strip in the markup, because the band is the
        // thing highest on screen — and it is a toggle, not a pane option: it is
        // outside the group and its test id does not end in -pane-option, so the
        // selector the strip's tests use keeps matching the three panes and nothing
        // else.
        Assert.Contains("TestId=\"roadmap-band-toggle\"", home, StringComparison.Ordinal);
        Assert.DoesNotContain("roadmap-pane-option", home, StringComparison.Ordinal);
        Assert.True(
            home.IndexOf("TestId=\"roadmap-band-toggle\"", StringComparison.Ordinal)
            < home.IndexOf("TestId=\"global-pane-multiselect\"", StringComparison.Ordinal),
            "The Roadmap toggle comes before the panes strip: the band sits above the panes.");

        // Each pane carries its own landmark id from its own folder; the shell
        // only points the multiselect's aria-controls at them. The band is the same
        // arrangement one level up — its landmark id lives in the Roadmap module.
        Assert.Contains("id=\"inbox-pane\"", NormalizeLineEndings(File.ReadAllText(FindInboxPane())), StringComparison.Ordinal);
        Assert.Contains("id=\"backlog-pane\"", NormalizeLineEndings(File.ReadAllText(FindTasksPane())), StringComparison.Ordinal);
        Assert.Contains("id=\"repository-devbook-pane\"", NormalizeLineEndings(File.ReadAllText(FindDevbookPane())), StringComparison.Ordinal);
        Assert.Contains("aria-controls=\"roadmap-band\"", home, StringComparison.Ordinal);
    }

    /// <summary>
    /// The workspace surfaces are one segmented control, because
    /// <c>WorkspaceSurface</c> is one field with one state at a time. They used to be
    /// independent <c>AppButton</c> disclosures carrying <c>aria-expanded</c>, which
    /// described neither their exclusivity nor the fact that a takeover replaces the
    /// workspace instead of expanding beside it. Pressed states describe both, and
    /// the Workspace segment gives the way back a control of its own.
    /// <para>
    /// Every takeover is named here rather than counted, so adding one to the header
    /// without adding it to the group cannot pass.
    /// </para>
    /// </summary>
    [Fact]
    public void The_surfaces_are_one_segmented_group_with_a_way_back()
    {
        var home = NormalizeLineEndings(File.ReadAllText(FindHomeRazor()));

        Assert.Contains("TestId=\"workspace-surface-switcher\"", home, StringComparison.Ordinal);
        Assert.Contains("TestId=\"workspace-surface-option\"", home, StringComparison.Ordinal);
        Assert.Contains("TestId=\"tools-toggle-button\"", home, StringComparison.Ordinal);
        Assert.Contains("TestId=\"dashboard-toggle-button\"", home, StringComparison.Ordinal);

        // Sessions is a tab of the Dashboard surface, not a segment: one segment
        // for the surface, and the strip inside it chooses the view.
        Assert.DoesNotContain("TestId=\"sessions-toggle-button\"", home, StringComparison.Ordinal);
        Assert.Contains("ListTestId=\"dashboard-tabs\"", home, StringComparison.Ordinal);
        Assert.Contains("TabTestId=\"dashboard-overview-tab\"", home, StringComparison.Ordinal);
        Assert.Contains("TabTestId=\"dashboard-sessions-tab\"", home, StringComparison.Ordinal);

        // Workspace leads, because it is the surface the reader starts on and the
        // one the other two return to.
        Assert.True(
            home.IndexOf("TestId=\"workspace-surface-option\"", StringComparison.Ordinal)
            < home.IndexOf("TestId=\"tools-toggle-button\"", StringComparison.Ordinal),
            "The Workspace segment comes first: it is what the takeovers return to.");

        // A selection, not a disclosure. Only Ask AI keeps aria-expanded, because
        // only its panel opens beside the content rather than replacing it.
        Assert.Contains("PressedChanged=\"CloseSurface\"", home, StringComparison.Ordinal);
        Assert.Contains("PressedChanged=\"ToggleTools\"", home, StringComparison.Ordinal);
        Assert.Contains("PressedChanged=\"ToggleDashboard\"", home, StringComparison.Ordinal);
        Assert.DoesNotContain("aria-expanded=\"@(ToolsVisible", home, StringComparison.Ordinal);
        Assert.DoesNotContain("aria-expanded=\"@(DashboardVisible", home, StringComparison.Ordinal);
        // Written out, not bound to the bool: Blazor renders a true bool attribute
        // as `aria-expanded=""` and drops it when false, and aria-expanded accepts
        // neither.
        Assert.Contains("aria-expanded=\"@(_aiExpanded ? \"true\" : \"false\")\"", home, StringComparison.Ordinal);
        Assert.DoesNotContain("aria-expanded=\"@_aiExpanded\"", home, StringComparison.Ordinal);

        // The switcher needs something to switch to: with both takeover features
        // off its only member would be the surface already on screen.
        Assert.Contains("@if (SurfaceSwitcherVisible)", home, StringComparison.Ordinal);

        // The Workspace segment points at a landmark, so the workspace main needs
        // the id the other two panes already have.
        Assert.Contains("id=\"workspace\"", home, StringComparison.Ordinal);

        // The sections strip is a workspace control, so it renders with the
        // workspace and not beside a takeover it cannot act on.
        var sectionsStrip = home.IndexOf("TestId=\"global-pane-multiselect\"", StringComparison.Ordinal);
        var sectionsGate = home.LastIndexOf("@if (WorkspaceVisible)", sectionsStrip, StringComparison.Ordinal);
        Assert.True(sectionsGate >= 0 && sectionsGate > home.IndexOf("TestId=\"workspace-surface-switcher\"", StringComparison.Ordinal),
            "The sections strip renders only while the workspace is on screen.");
    }

    /// <summary>
    /// The shape of a header group says how many of its options can be on at once:
    /// members fused inside one border where several can (the repository scope with
    /// Ctrl held, the sections strip), standalone controls with a gap between them
    /// where exactly one can (the surface switcher). It used to be the other way
    /// round for two of the three — the scope was loose chips and the surfaces were
    /// fused — so a reader had to press to find out which kind of control they were
    /// holding. The markup carries the modifier and the stylesheet keys on it, so both
    /// halves are pinned here.
    /// </summary>
    [Fact]
    public void A_header_groups_shape_says_how_many_options_it_takes()
    {
        var home = NormalizeLineEndings(File.ReadAllText(FindHomeRazor()));
        var css = NormalizeLineEndings(File.ReadAllText(FindAppCss()));

        // The scope is the shared ButtonGroup wearing the fused shape, not a div of
        // its own: several repositories can be scoped at once, so its chips touch.
        Assert.Contains(
            "<ButtonGroup CssClass=\"header-group app-header__repository-scope\"",
            home,
            StringComparison.Ordinal);
        Assert.DoesNotContain("<div class=\"app-header__repository-scope\"", home, StringComparison.Ordinal);

        // Exactly one surface at a time, so its options stand apart.
        Assert.Contains(
            "<ButtonGroup CssClass=\"header-group header-group--loose header-group--surface\"",
            home,
            StringComparison.Ordinal);

        // Any number of panes at once, so the strip stays fused.
        Assert.Contains(
            "<ButtonGroup CssClass=\"header-group header-group--panes\"",
            home,
            StringComparison.Ordinal);

        // The roadmap band is not one of a set at all — on or off, touching no
        // pane — so its toggle stands alone in the loose shape, outside any group.
        Assert.Contains(
            "<ToggleButton BaseClass=\"header-group__option header-group__option--loose\"",
            home,
            StringComparison.Ordinal);
        Assert.True(
            home.IndexOf("TestId=\"roadmap-band-toggle\"", StringComparison.Ordinal)
            > home.IndexOf("@if (WorkspaceVisible && RoadmapPaneOptionVisible)", StringComparison.Ordinal),
            "The roadmap toggle renders on its own gate, not inside the panes strip.");

        // The loose modifier takes the group's own border and fill away and spaces
        // the members out; each member, loose in its own right, then draws the
        // border the group gave up. The option's modifier is its own rather than a
        // descendant rule, so the roadmap toggle can wear it with no group at all.
        var loose = RuleFor(css, ".header-group--loose {");
        Assert.Contains("border: 0;", loose, StringComparison.Ordinal);
        Assert.Contains("background: transparent;", loose, StringComparison.Ordinal);
        Assert.Contains("gap: var(--spacing-xs);", loose, StringComparison.Ordinal);

        var looseOption = RuleFor(css, ".header-group__option--loose {");
        Assert.Contains("border: var(--border-width) solid var(--color-border);", looseOption, StringComparison.Ordinal);
        Assert.Contains("border-radius: var(--border-radius-md);", looseOption, StringComparison.Ordinal);
        Assert.DoesNotContain(".header-group--loose > .header-group__option", css, StringComparison.Ordinal);

        // A standalone option carries its selection on its own edge, the way the
        // library's pressed toggle does, because there is no strip for an underline
        // to run along.
        Assert.Contains(
            "border-color: var(--color-primary);",
            RuleFor(css, ".header-group__option--loose.header-group__option--selected {"),
            StringComparison.Ordinal);

        // The fused scope chips give up the border and radius a loose chip draws, and
        // keep only a hairline between neighbours. The active fill, the identity edge
        // and the anchor underline are untouched: those rules still key on the chip
        // classes the buttons keep wearing.
        var fusedChip = RuleFor(css, ".app-header__repository-scope > .chip {");
        Assert.Contains("border: 0;", fusedChip, StringComparison.Ordinal);
        Assert.Contains("border-radius: var(--border-radius-none);", fusedChip, StringComparison.Ordinal);
        Assert.Contains(
            "border-left: var(--border-width) solid var(--color-border);",
            RuleFor(css, ".app-header__repository-scope > .chip + .chip {"),
            StringComparison.Ordinal);
        Assert.Contains("gap: 0;", RuleFor(css, ".app-header__repository-scope {"), StringComparison.Ordinal);
    }

    /// <summary>
    /// Navigation and cross-cutting concerns are separate regions of the header, not
    /// one row of interchangeable pills. This pins the four regions and the fact
    /// that only the two navigation groups live inside the nav landmark.
    /// </summary>
    [Fact]
    public void The_header_separates_navigation_from_the_cross_cutting_utilities()
    {
        var home = NormalizeLineEndings(File.ReadAllText(FindHomeRazor()));

        Assert.Contains("class=\"app-header__identity\"", home, StringComparison.Ordinal);
        Assert.Contains("class=\"app-header__nav\" aria-label=\"Workspace views\"", home, StringComparison.Ordinal);
        Assert.Contains("class=\"app-header__status\"", home, StringComparison.Ordinal);
        Assert.Contains("class=\"app-header__utilities\"", home, StringComparison.Ordinal);

        var nav = home.IndexOf("class=\"app-header__nav\"", StringComparison.Ordinal);
        var status = home.IndexOf("class=\"app-header__status\"", StringComparison.Ordinal);
        var utilities = home.IndexOf("class=\"app-header__utilities\"", StringComparison.Ordinal);

        // Reading order: what you are looking at, how it is doing, then the things
        // that are not about it at all.
        Assert.True(nav < status && status < utilities);

        // Both navigation groups are inside the landmark; the utilities are not.
        Assert.InRange(home.IndexOf("TestId=\"global-pane-multiselect\"", StringComparison.Ordinal), nav, status);
        Assert.InRange(home.IndexOf("TestId=\"workspace-surface-switcher\"", StringComparison.Ordinal), nav, status);

        // Two utilities are left, and they are the two that are about this screen:
        // Ask AI opens a panel beside its content, and settings is where this
        // screen's own configuration lives. The feedback trigger and the version
        // control were neither — they are true app-shell concerns, so they moved to
        // the footer MainLayout mounts once for every route.
        Assert.True(home.IndexOf("TestId=\"ai-toggle-button\"", StringComparison.Ordinal) > utilities);
        Assert.True(home.IndexOf("data-testid=\"settings-link\"", StringComparison.Ordinal) > utilities);

        // Ask AI leads, and settings closes the header.
        Assert.True(
            home.IndexOf("TestId=\"ai-toggle-button\"", StringComparison.Ordinal)
            < home.IndexOf("data-testid=\"settings-link\"", StringComparison.Ordinal),
            "Settings is the last control in the header.");

        // Neither of the two that left may come back here. A second mount would
        // give the reader two "Report issue" buttons and two version controls the
        // moment the footer is on screen, which it now always is.
        Assert.DoesNotContain("<FeedbackReportButton", home, StringComparison.Ordinal);
        Assert.DoesNotContain("TestId=\"app-version\"", home, StringComparison.Ordinal);

        // The pill row is gone: no interactive control in the header is a 999px
        // pill any more, and the classes that drew them went with it.
        Assert.DoesNotContain("\"pane-multiselect", home, StringComparison.Ordinal);
        Assert.DoesNotContain("pane-multiselect__option", home, StringComparison.Ordinal);
        Assert.DoesNotContain("header-tool-toggle", home, StringComparison.Ordinal);
    }

    /// <summary>
    /// The band's option is gated on its feature the way the Inbox option is, and it
    /// is deliberately not gated on anything else. It carries no <c>Disabled</c>
    /// binding, because <c>PaneToggleDisabled</c> exists for the three panes'
    /// viewport-driven capacity rule and the band — a horizontal row with a grid track
    /// of its own — has none. Nor is it a <c>GlobalPane</c>: joining that selection
    /// would put it inside <c>TrimToCapacity</c>'s reach and let window width evict it.
    /// </summary>
    [Fact]
    public void Roadmap_option_is_feature_gated_and_stays_out_of_the_pane_selection()
    {
        var home = NormalizeLineEndings(File.ReadAllText(FindHomeRazor()));

        // One gate for both conditions, because the toggle stands on its own now
        // rather than inside the strip the workspace gate already wraps.
        Assert.Contains("@if (WorkspaceVisible && RoadmapPaneOptionVisible)", home, StringComparison.Ordinal);
        Assert.Contains("RoadmapFeatures.Roadmap", home, StringComparison.Ordinal);
        Assert.Contains("private bool RoadmapBandVisible => RoadmapPaneOptionVisible && _roadmapVisible;", home, StringComparison.Ordinal);

        // Hidden unless the reader asks for it, and stated as the field default
        // rather than an explicit false, the way the shell's other view-state flags
        // are.
        Assert.Contains("private bool _roadmapVisible;", home, StringComparison.Ordinal);
        Assert.DoesNotContain("private bool _roadmapVisible = ", home, StringComparison.Ordinal);

        // Its own shell field, flipped directly rather than through the selection.
        Assert.Contains("private void ToggleRoadmapBand() => _roadmapVisible = !_roadmapVisible;", home, StringComparison.Ordinal);
        Assert.DoesNotContain("GlobalPane.Roadmap", home, StringComparison.Ordinal);

        // And hiding it reuses the grid variant the feature flag already uses rather
        // than introducing a second one for the same layout.
        Assert.Contains("workspace workspace--no-roadmap", home, StringComparison.Ordinal);
        Assert.DoesNotContain("workspace--roadmap-collapsed", home, StringComparison.Ordinal);
    }

    [Fact]
    public void Inbox_option_and_pane_are_guarded_by_feature_flag()
    {
        var home = NormalizeLineEndings(File.ReadAllText(FindHomeRazor()));

        Assert.Contains("@if (InboxPaneOptionVisible)", home, StringComparison.Ordinal);
        Assert.Contains("AppFeatures.InboxPane", home, StringComparison.Ordinal);
        Assert.Contains("_globalPanes.TrySetAvailable(GlobalPane.Inbox, InboxPaneOptionVisible);", home, StringComparison.Ordinal);
    }

    /// <summary>
    /// Capacity no longer disables an option: a pane the reader asks for makes its own
    /// room by closing the others, so the only thing that can block one of
    /// the three is being the last pane on screen. The binding stays on exactly the
    /// three panes all the same — the band has neither rule.
    /// </summary>
    [Fact]
    public void Pane_multiselect_uses_selected_state_and_last_pane_aware_disabling()
    {
        var home = NormalizeLineEndings(File.ReadAllText(FindHomeRazor()));

        // ToggleButton derives aria-pressed from Pressed, so the visibility of a
        // pane is stated once and the attribute cannot drift away from it.
        Assert.Contains("Pressed=\"RoadmapBandVisible\"", home, StringComparison.Ordinal);
        Assert.Contains("Pressed=\"InboxPaneVisible\"", home, StringComparison.Ordinal);
        Assert.Contains("Pressed=\"TasksPaneVisible\"", home, StringComparison.Ordinal);
        Assert.Contains("Pressed=\"DevbookPaneVisible\"", home, StringComparison.Ordinal);

        // Three panes have the rule and the band does not, so exactly three options
        // are ever disabled by it. A fourth would mean the band had been folded into
        // the selection.
        Assert.Equal(3, CountOccurrences(home, "Disabled=\"@PaneToggleDisabled("));

        // The three pane options press through one handler that reads the
        // modifier, so "this one too" is the option itself and not a second
        // control beside it; the band keeps the plain toggle, because it takes
        // no modifier.
        foreach (var pane in new[] { "Inbox", "Tasks", "Devbook" })
        {
            Assert.Contains($"OnClick=\"args => PressPane(GlobalPane.{pane}, args)\"", home, StringComparison.Ordinal);
            Assert.Contains($"Title=\"@PaneOptionTitle(GlobalPane.{pane}, \"", home, StringComparison.Ordinal);
        }
        Assert.Contains("PressedChanged=\"ToggleRoadmapBand\"", home, StringComparison.Ordinal);

        Assert.Contains("if (_globalPanes.IsEnabled(pane))", home, StringComparison.Ordinal);
        Assert.Contains("return !_globalPanes.CanDisable(pane);", home, StringComparison.Ordinal);
        Assert.Contains("return !_globalPanes.CanEnable(pane);", home, StringComparison.Ordinal);

        Assert.DoesNotContain("Show inbox", home, StringComparison.Ordinal);
        Assert.DoesNotContain("Hide inbox", home, StringComparison.Ordinal);
        Assert.DoesNotContain("Show backlog", home, StringComparison.Ordinal);
        Assert.DoesNotContain("Hide backlog", home, StringComparison.Ordinal);
        Assert.DoesNotContain("Show knowledge", home, StringComparison.Ordinal);
        Assert.DoesNotContain("Hide knowledge", home, StringComparison.Ordinal);
    }

    /// <summary>
    /// Routing turns an Inbox item into Tasks entries, and the shell opens the
    /// Tasks pane on the reader's behalf rather than the reader switching sections.
    /// It goes through the non-switching entry point for exactly that reason: the
    /// plain enable would close the Inbox the item was routed from.
    /// </summary>
    [Fact]
    public void Opening_an_inbox_item_never_closes_the_inbox()
    {
        var home = NormalizeLineEndings(File.ReadAllText(FindHomeRazor()));

        Assert.Contains("_globalPanes.TryOpenAlongside(GlobalPane.Tasks);", home, StringComparison.Ordinal);
        Assert.DoesNotContain("_globalPanes.TrySetEnabled(GlobalPane.Tasks, true);", home, StringComparison.Ordinal);
    }

    /// <summary>
    /// Each pane used to carry a pin, railed along its option's top edge, that held
    /// it through a switch (#283 is the shape it settled on). The pin was a second
    /// control on every option for what one modifier on the option itself says:
    /// a plain press switches to the pane, Ctrl (Cmd on macOS) opens it beside the
    /// open ones — the convention the repository scope in the same header already
    /// follows, so the reader learns it once. The rails are gone from the markup,
    /// the stylesheet and the persisted layout alike, and the tooltip is what names
    /// the modifier, because nothing else on screen does.
    /// </summary>
    [Fact]
    public void A_second_pane_is_asked_for_with_the_modifier_rather_than_a_pin()
    {
        var home = NormalizeLineEndings(File.ReadAllText(FindHomeRazor()));
        var css = NormalizeLineEndings(File.ReadAllText(FindAppCss()));

        Assert.DoesNotContain("-pane-pin", home, StringComparison.Ordinal);
        Assert.DoesNotContain("header-group__pin", home, StringComparison.Ordinal);
        Assert.DoesNotContain("header-group__pane", home, StringComparison.Ordinal);
        Assert.DoesNotContain("ChevronUpIcon", home, StringComparison.Ordinal);
        Assert.DoesNotContain("LastPinnedPanes", home, StringComparison.Ordinal);

        Assert.DoesNotContain(".header-group__pin", css, StringComparison.Ordinal);
        Assert.DoesNotContain(".header-group__pane", css, StringComparison.Ordinal);

        // The modifier opens beside; without it, or in a window that fits one pane,
        // the press is the switch it always was.
        Assert.Contains(
            "if (!_globalPanes.IsEnabled(pane) && (args.CtrlKey || args.MetaKey) && _globalPanes.TakesSeveral)",
            home,
            StringComparison.Ordinal);
        Assert.Contains("_globalPanes.TryOpenBeside(pane);", home, StringComparison.Ordinal);
        Assert.Contains("_globalPanes.Toggle(pane);", home, StringComparison.Ordinal);

        // Named where the reader can find it, and only where it does something.
        Assert.Contains("Ctrl+click to open it beside the open panes", home, StringComparison.Ordinal);
        Assert.Contains("_globalPanes.TakesSeveral", home, StringComparison.Ordinal);
    }

    [Fact]
    public void Home_receives_viewport_pane_capacity_from_javascript()
    {
        var home = NormalizeLineEndings(File.ReadAllText(FindHomeRazor()));
        // The resizer moved into the shared component library; Home still owns
        // the callback it reports into.
        var componentsJs = NormalizeLineEndings(File.ReadAllText(FindComponentsJs()));

        Assert.Contains("public Task SetGlobalPaneCapacityAsync(int capacity)", home, StringComparison.Ordinal);
        Assert.Contains("owner.invokeMethodAsync('SetGlobalPaneCapacityAsync', capacity);", componentsJs, StringComparison.Ordinal);
        Assert.Contains("const BACKLOG_SINGLE_PANE_MAX_REM = 72;", componentsJs, StringComparison.Ordinal);
        Assert.Contains("const BACKLOG_THREE_PANE_MIN_REM = 96;", componentsJs, StringComparison.Ordinal);
    }

    /// <summary>
    /// The resizer holds one owner per layout, not one for the document.
    /// <para>
    /// A single owner is why a page could only have one draggable pane: the second
    /// layout to register replaced the first, and every drag reported its width to
    /// whichever component happened to be in the variable. Home registers for its
    /// knowledge layout on startup, so a <c>SplitPane</c> inside the shell had to
    /// opt the pointer gesture out and keep only its keyboard resize — which is
    /// exactly what the backlog pane's separator did.
    /// </para>
    /// <para>
    /// Asserted on the source because there is no JS engine here. What is pinned is
    /// the shape that makes it work: a map rather than a variable, a lookup keyed on
    /// the layout, and no fallback from a keyed layout to somebody else's owner —
    /// the fallback would be the same cross-talk with an extra step.
    /// </para>
    /// </summary>
    [Fact]
    public void The_resizer_keeps_one_owner_per_layout_rather_than_one_per_document()
    {
        var componentsJs = NormalizeLineEndings(File.ReadAllText(FindComponentsJs()));

        Assert.Contains("const backlogPaneOwners = new Map();", componentsJs, StringComparison.Ordinal);
        Assert.Contains("backlogPaneOwners.set(key ?? '', owner);", componentsJs, StringComparison.Ordinal);
        Assert.Contains("backlogPaneOwners.delete(key ?? '');", componentsJs, StringComparison.Ordinal);
        Assert.Contains("return backlogPaneOwners.get(backlogOwnerKey(layout)) ?? null;", componentsJs, StringComparison.Ordinal);

        // The drag settles to the layout it was performed on, and does nothing at
        // all when that layout has nobody listening.
        Assert.Contains("const owner = backlogPaneOwnerFor(layout);", componentsJs, StringComparison.Ordinal);
        Assert.Contains("if (!owner) return;", componentsJs, StringComparison.Ordinal);

        // The single owner, and the opt-out it forced, are both gone.
        Assert.DoesNotContain("let backlogPaneOwner ", componentsJs, StringComparison.Ordinal);
        Assert.DoesNotContain("data-pane-drag", componentsJs, StringComparison.Ordinal);
    }

    /// <summary>
    /// Capacity reads the viewport, so it can be answered whether or not the pane
    /// layout is mounted. Reporting it behind the layout guard meant a window
    /// resized while a full-screen surface was open reported nothing at all, and
    /// the panes came back sized for a window that no longer existed. There is no
    /// JS engine here to prove the behaviour, so the order of the two reports is
    /// pinned instead: capacity first, then the lookup, then the measured width.
    /// <para>
    /// The guard is a conditional now rather than an early return, because the
    /// reports run per owner: returning would skip every owner after the one whose
    /// layout happened to be off screen, so the width is written only when there is
    /// a layout to measure and the loop carries on either way.
    /// </para>
    /// </summary>
    [Fact]
    public void Pane_capacity_is_reported_even_while_the_layout_is_off_screen()
    {
        var componentsJs = NormalizeLineEndings(File.ReadAllText(FindComponentsJs()));

        var capacity = componentsJs.IndexOf(
            "owner.invokeMethodAsync('SetGlobalPaneCapacityAsync'",
            StringComparison.Ordinal);
        var lookup = componentsJs.IndexOf(
            "const layout = backlogLayoutForKey(key);",
            StringComparison.Ordinal);
        var width = componentsJs.IndexOf(
            "if (layout) owner.invokeMethodAsync('SetSidePaneMaxWidthAsync'",
            StringComparison.Ordinal);

        Assert.True(capacity >= 0 && lookup >= 0 && width >= 0);
        Assert.True(capacity < lookup, "Capacity must be reported before the layout is looked up.");
        Assert.True(lookup < width, "Only the measured width may depend on the layout.");

        // An unmeasurable layout must not cost the owners after it their capacity,
        // so the reporting function may not return early on one. Scoped to that
        // function rather than to the file: the drag handler bails out on a
        // separator with no layout around it, which is a different question with a
        // different right answer.
        var reporter = componentsJs.IndexOf("function backlogReportPaneBounds()", StringComparison.Ordinal);
        Assert.True(reporter >= 0);

        var body = componentsJs[reporter..componentsJs.IndexOf("window.backlogPaneResizer", StringComparison.Ordinal)];
        Assert.DoesNotContain("return;", body, StringComparison.Ordinal);
    }

    [Fact]
    public void Side_layout_opens_split_only_when_backlog_and_side_panes_are_both_visible()
    {
        var home = NormalizeLineEndings(File.ReadAllText(FindHomeRazor()));

        Assert.Contains("(TasksPaneVisible && RightSidePaneVisible) ? \"devbook-layout--side-open\"", home, StringComparison.Ordinal);
        Assert.Contains("side-pane-stack--full", home, StringComparison.Ordinal);

        // Tools left the side stack for a full-screen surface of its own, so a
        // stack holding nothing but the tools pane can no longer happen — and the
        // split must not open for it. Stated on the property rather than on the
        // class the stack used to grow, because the docked modifier is gone.
        Assert.Contains(
            "private bool RightSidePaneVisible => DevbookPaneVisible || (InboxPaneVisible && !TasksPaneVisible);",
            home,
            StringComparison.Ordinal);
        Assert.DoesNotContain("side-pane-stack--right-docked", home, StringComparison.Ordinal);
    }

    /// <summary>
    /// Tools and the Dashboard are takeovers, not panes. Each is the page's single
    /// <c>main</c> landmark while it is open, which is only true as long as the
    /// branches stay mutually exclusive in the markup. The session list is inside
    /// the Dashboard's landmark, as a tab, and has no branch of its own.
    /// </summary>
    [Fact]
    public void Only_one_surface_renders_and_it_owns_the_main_landmark()
    {
        var home = NormalizeLineEndings(File.ReadAllText(FindHomeRazor()));

        Assert.Contains("@if (ToolsVisible)", home, StringComparison.Ordinal);
        Assert.Contains("else if (DashboardVisible)", home, StringComparison.Ordinal);
        Assert.DoesNotContain("SessionsVisible", home, StringComparison.Ordinal);
        Assert.Contains("data-testid=\"tools-surface\"", home, StringComparison.Ordinal);
        Assert.Contains("data-testid=\"dashboard-surface\"", home, StringComparison.Ordinal);
        Assert.DoesNotContain("data-testid=\"sessions-surface\"", home, StringComparison.Ordinal);
        Assert.Contains("data-testid=\"workspace\"", home, StringComparison.Ordinal);

        // One landmark per branch, and the branches are exclusive, so the page has
        // exactly one. Two <main> elements is the failure this counts, which is why
        // the number moves with each takeover rather than being loosened to "some".
        Assert.Equal(3, CountOccurrences(home, "<main class="));

        // The pane row keeps the test id the resizer's JavaScript selects on; what
        // changed is that it is no longer the landmark itself.
        Assert.Contains("<div class=\"devbook-layout ", home, StringComparison.Ordinal);
        Assert.DoesNotContain("<main class=\"devbook-layout", home, StringComparison.Ordinal);
        Assert.Contains("data-testid=\"devbook-layout\"", home, StringComparison.Ordinal);
    }

    /// <summary>
    /// A takeover is a context change, so focus moves onto it and Escape brings the
    /// reader back. It is deliberately not a Modal: the header behind it is not
    /// inert, because that is where the control closing the surface lives.
    /// </summary>
    [Fact]
    public void A_surface_takes_focus_on_open_and_closes_on_escape()
    {
        var home = NormalizeLineEndings(File.ReadAllText(FindHomeRazor()));

        Assert.Contains("tabindex=\"-1\"", home, StringComparison.Ordinal);
        Assert.Contains("@ref=\"_surfaceElement\"", home, StringComparison.Ordinal);
        Assert.Contains("@onkeydown=\"OnSurfaceKeyDown\"", home, StringComparison.Ordinal);
        Assert.Contains("await _surfaceElement.FocusAsync();", home, StringComparison.Ordinal);
        Assert.Contains("if (e.Key == \"Escape\") CloseSurface();", home, StringComparison.Ordinal);

        // No scrim and no focus trap: Modal owns those, and it is still what the
        // update and feedback dialogs are built from.
        Assert.DoesNotContain("workspace-surface-backdrop", home, StringComparison.Ordinal);
    }

    /// <summary>
    /// Closing a surface has to put the reader back where they were. It does so by
    /// construction rather than by remembering anything: the surface is a field of
    /// its own, so <see cref="GlobalPaneSelection"/> is never touched to open one —
    /// which also keeps Tools out of the pane capacity rule.
    /// </summary>
    [Fact]
    public void Opening_a_surface_leaves_the_pane_selection_alone()
    {
        var home = NormalizeLineEndings(File.ReadAllText(FindHomeRazor()));
        var surface = NormalizeLineEndings(File.ReadAllText(FindWorkspaceSurface()));

        Assert.Contains("private WorkspaceSurface _surface = WorkspaceSurface.Workspace;", home, StringComparison.Ordinal);
        Assert.Contains("_surface = _surface == surface ? WorkspaceSurface.Workspace : surface;", home, StringComparison.Ordinal);

        // Three states in one field is what makes a takeover exclusive with the
        // workspace and with the other takeover.
        Assert.Contains("Workspace,", surface, StringComparison.Ordinal);
        Assert.Contains("Tools,", surface, StringComparison.Ordinal);
        Assert.Contains("Dashboard", surface, StringComparison.Ordinal);

        // Tools is not a fourth global pane, and must not become one: the panes
        // carry a viewport capacity rule and an always-one-visible invariant that
        // a full-screen surface has no business in.
        Assert.DoesNotContain("GlobalPane.Tools", home, StringComparison.Ordinal);
        Assert.DoesNotContain("GlobalPane.Dashboard", home, StringComparison.Ordinal);
    }

    /// <summary>
    /// The pane bounds are measured from the layout element, so nothing is reported
    /// while a takeover has it unmounted. Without a nudge on the way back, closing
    /// a surface after the window was resized would leave the capacity describing a
    /// window that is gone.
    /// </summary>
    [Fact]
    public void Home_remeasures_the_pane_bounds_when_the_workspace_comes_back()
    {
        var home = NormalizeLineEndings(File.ReadAllText(FindHomeRazor()));
        var componentsJs = NormalizeLineEndings(File.ReadAllText(FindComponentsJs()));

        Assert.Contains("refresh() {", componentsJs, StringComparison.Ordinal);
        Assert.Contains("backlogReportPaneBounds();", componentsJs, StringComparison.Ordinal);

        Assert.Contains("await JS.InvokeVoidAsync(\"backlogPaneResizer.refresh\");", home, StringComparison.Ordinal);
        Assert.Contains("if (_workspaceWasHidden)", home, StringComparison.Ordinal);
    }

    [Fact]
    public void Inbox_renders_before_backlog_when_both_are_visible()
    {
        var home = NormalizeLineEndings(File.ReadAllText(FindHomeRazor()));

        Assert.Contains("@if (InboxBeforeTasksVisible)", home, StringComparison.Ordinal);
        Assert.Contains("@if (!TasksPaneVisible && InboxPaneVisible)", home, StringComparison.Ordinal);
        Assert.Contains("devbook-layout--inbox-before-backlog", home, StringComparison.Ordinal);

        var inboxGuardIndex = home.IndexOf("@if (InboxBeforeTasksVisible)", StringComparison.Ordinal);
        var backlogPaneIndex = home.IndexOf("<TasksPane />", StringComparison.Ordinal);

        Assert.True(inboxGuardIndex >= 0);
        Assert.True(backlogPaneIndex > inboxGuardIndex);
    }

    [Fact]
    public void App_version_opens_a_separate_update_window()
    {
        var footer = NormalizeLineEndings(File.ReadAllText(FindAppFooterRazor()));

        Assert.Contains("TestId=\"app-version\"", footer, StringComparison.Ordinal);
        Assert.Contains("OnClick=\"OpenUpdateWindow\"", footer, StringComparison.Ordinal);
        // The dialog shell is now the shared Modal component, so the test id
        // reaches the DOM through its TestId parameter instead of a literal
        // attribute.
        Assert.Contains("TestId=\"app-update-dialog\"", footer, StringComparison.Ordinal);
        Assert.Contains("data-testid=\"check-for-updates\"", footer, StringComparison.Ordinal);
        Assert.Contains("data-testid=\"install-update\"", footer, StringComparison.Ordinal);
        Assert.DoesNotContain("app-version__hint", footer, StringComparison.Ordinal);

        // The dialog stays in the same component as the control that opens it.
        // Modal returns focus to whatever had it when the dialog opened, and that
        // only holds while the trigger is still rendered — hoisting the Modal into
        // MainLayout and leaving the button here would silently drop the reader
        // back at the top of the document on Escape.
        Assert.True(
            footer.IndexOf("TestId=\"app-version\"", StringComparison.Ordinal)
            < footer.IndexOf("TestId=\"app-update-dialog\"", StringComparison.Ordinal),
            "The trigger precedes the dialog it owns, in the component that owns both.");
    }

    [Fact]
    public void The_version_control_shows_the_worktree_when_the_host_registers_one()
    {
        var footer = NormalizeLineEndings(File.ReadAllText(FindAppFooterRazor()));

        // The control keeps its test id and its click either way; only what it
        // reads changes, so a host that registers nothing is unaffected.
        Assert.Contains("Services.GetService<DevelopmentWorkspaceLabel>()", footer, StringComparison.Ordinal);
        Assert.Contains("@if (_workspace is null)", footer, StringComparison.Ordinal);
        Assert.Contains("Version <code>@Updates.CurrentVersion</code>", footer, StringComparison.Ordinal);
        Assert.Contains("data-testid=\"app-workspace\"", footer, StringComparison.Ordinal);

        var chipIndex = footer.IndexOf("TestId=\"app-version\"", StringComparison.Ordinal);
        var workspaceIndex = footer.IndexOf("data-testid=\"app-workspace\"", StringComparison.Ordinal);

        Assert.True(chipIndex >= 0);
        Assert.True(workspaceIndex > chipIndex);
    }

    /// <summary>
    /// The footer is part of the app shell, not part of a page: <c>MainLayout</c>
    /// mounts it once, outside the routed body, so it survives every route change
    /// and a third route gets it without saying anything.
    /// <para>
    /// The wrapper matters as much as the tag. <c>.shell</c> used to be the element
    /// that owned the viewport height; with a sibling below it, the height belongs
    /// to a column that holds both, and <c>.shell</c> takes what is left.
    /// </para>
    /// </summary>
    [Fact]
    public void The_layout_wraps_the_routed_body_and_the_footer_in_one_column()
    {
        var layout = NormalizeLineEndings(File.ReadAllText(FindMainLayoutRazor()));

        Assert.Contains("class=\"app-shell\"", layout, StringComparison.Ordinal);
        Assert.Contains("@Body", layout, StringComparison.Ordinal);
        Assert.Contains("<AppFooter />", layout, StringComparison.Ordinal);

        // Below the page, not above it: the footer is the last thing on screen and
        // the last thing in reading order, and those have to be the same thing.
        Assert.True(
            layout.IndexOf("@Body", StringComparison.Ordinal)
            < layout.IndexOf("<AppFooter />", StringComparison.Ordinal),
            "The footer follows the routed body.");
    }

    /// <summary>
    /// What the footer band itself is: one landmark carrying the two controls that
    /// are about the application rather than about the screen.
    /// </summary>
    [Fact]
    public void The_footer_is_a_landmark_carrying_the_app_wide_utilities()
    {
        var footer = NormalizeLineEndings(File.ReadAllText(FindAppFooterRazor()));

        Assert.Contains("<footer class=\"app-footer\" data-testid=\"app-footer\">", footer, StringComparison.Ordinal);
        Assert.Contains("<FeedbackReportButton />", footer, StringComparison.Ordinal);

        // Deliberately not "build-footer". That name belongs to the harness band
        // asserted by HarnessBuildFooterTests, which shows the version and the
        // worktree side by side out of AppVersion.OfEntryAssembly(); this one shows
        // one or the other out of IAppUpdateService and carries live controls.
        Assert.DoesNotContain("build-footer", footer, StringComparison.Ordinal);

        // No heading. FocusOnNavigate in Routes.razor moves focus to the first h1
        // in the document on every navigation, and an h1 down here would take that
        // from the page the reader just opened. The landmark type is the name, so
        // there is nothing for a heading to add — which is the same reason there is
        // no role="contentinfo" and no aria-label on the tag above.
        Assert.DoesNotContain("<h1", footer, StringComparison.Ordinal);
        Assert.DoesNotContain("role=\"contentinfo\"", footer, StringComparison.Ordinal);

        // A containing block here would clip both dialogs to the height of the
        // band: Modal renders position: fixed, and any of these properties on an
        // ancestor makes that ancestor the reference instead of the viewport.
        var css = NormalizeLineEndings(File.ReadAllText(FindAppCss()));
        var block = css[css.IndexOf(".app-footer {", StringComparison.Ordinal)..];
        block = block[..block.IndexOf('}')];

        foreach (var containing in new[] { "transform", "filter", "backdrop-filter", "contain:", "perspective", "will-change" })
        {
            Assert.DoesNotContain(containing, block, StringComparison.Ordinal);
        }

        // The band is as short as its contents allow: 2.25rem of control, and block
        // padding at exactly the reach of a focus ring. Shorter would clip the ring
        // against .app-shell's overflow — .design/accessibility.md#focus-visibility
        // draws focus as an outline offset 2px, so 4px of ink sits outside the
        // control — and taller is the 8px this change bought back on every route.
        Assert.Contains("padding: var(--spacing-xs) var(--spacing-lg);", block, StringComparison.Ordinal);
    }

    /// <summary>
    /// The move is a move, not a copy. Both controls were mounted per page before —
    /// the feedback trigger on both routes, the version control on Home — and the
    /// layout now mounts each exactly once. Leaving either behind would render it
    /// twice on the page that kept it.
    /// </summary>
    [Fact]
    public void The_pages_no_longer_mount_the_app_wide_utilities_themselves()
    {
        var home = NormalizeLineEndings(File.ReadAllText(FindHomeRazor()));
        var settings = NormalizeLineEndings(File.ReadAllText(FindSettingsRazor()));

        Assert.DoesNotContain("FeedbackReportButton", home, StringComparison.Ordinal);
        Assert.DoesNotContain("app-version", home, StringComparison.Ordinal);
        Assert.DoesNotContain("app-update-dialog", home, StringComparison.Ordinal);

        Assert.DoesNotContain("FeedbackReportButton", settings, StringComparison.Ordinal);

        // The state machine went with the markup. A field left behind here is dead
        // weight that the compiler will not complain about, because Razor generates
        // no unused-field warning for a component's @code block.
        Assert.DoesNotContain("_appUpdateWindowOpen", home, StringComparison.Ordinal);
        Assert.DoesNotContain("DevelopmentWorkspaceLabel", home, StringComparison.Ordinal);

        // But _workspaceWasHidden stays: despite the name it is about the workspace
        // *surface* coming back into view and where focus goes when it does, which
        // has nothing to do with the worktree label.
        Assert.Contains("_workspaceWasHidden", home, StringComparison.Ordinal);
    }

    [Fact]
    public void Devbook_folder_errors_do_not_use_empty_razor_fragment_tags()
    {
        var home = NormalizeLineEndings(File.ReadAllText(FindHomeRazor()));

        Assert.DoesNotContain("@<>", home, StringComparison.Ordinal);
        Assert.DoesNotContain("</>", home, StringComparison.Ordinal);
    }

    /// <summary>
    /// The entry list is the shared task list, and which row is open is the pane's
    /// to say.
    /// <para>
    /// This replaces a fact about a fold button on the entry title. There is no fold
    /// left to press: a row in the list is one line and the expansion is the detail
    /// pane beside it, so what used to be "the title integrates its own collapse
    /// button" is now "the title is a row in <c>TaskListView</c>, and the pane hands
    /// it <c>SelectedId</c>". The storybook says why the row cannot decide that for
    /// itself — which one is open is a fact about the pane, not about the row.
    /// </para>
    /// </summary>
    [Fact]
    public void The_entry_list_is_the_shared_task_list_and_the_pane_owns_the_selection()
    {
        var pane = NormalizeLineEndings(File.ReadAllText(FindTasksPane()));

        Assert.Contains("<TaskListView", pane, StringComparison.Ordinal);
        Assert.Contains("SelectedId=\"@SelectedTaskId\"", pane, StringComparison.Ordinal);
        Assert.Contains("OnSelected=\"OnEntrySelectedAsync\"", pane, StringComparison.Ordinal);
        Assert.Contains("TestId=\"entry-list\"", pane, StringComparison.Ordinal);

        // No fold of the pane's own came back beside the list.
        Assert.DoesNotContain("entry-fold-button", pane, StringComparison.Ordinal);
        Assert.DoesNotContain("entry-title-button", pane, StringComparison.Ordinal);
        Assert.DoesNotContain("ToggleEntry", pane, StringComparison.Ordinal);
    }

    /// <summary>
    /// There is no click-to-edit surface for a badge to have to keep its clicks away
    /// from.
    /// <para>
    /// This replaces the pin on the metadata row's <c>stopPropagation</c> pair. Those
    /// existed because the badges sat inside a read view that opened the raw editor
    /// on click and on Enter, so a status change that reached the card swapped the
    /// entry for a textarea mid-edit. The read view is gone: the pane is opened by
    /// selecting a row, and the source is a toggle of its own. Guarding the same
    /// intent now means asserting the surface has not come back, because a
    /// propagation stop is only ever a fix for one.
    /// </para>
    /// </summary>
    [Fact]
    public void No_control_sits_inside_a_surface_that_opens_an_editor_on_click()
    {
        var pane = NormalizeLineEndings(File.ReadAllText(FindTasksPane()));

        // Quoted, because `entry-doc__reading` under the escape hatch is a prefix of
        // it and is a different thing: a hint about what the source parses to, not a
        // surface that opens an editor.
        Assert.DoesNotContain("\"entry-doc__read\"", pane, StringComparison.Ordinal);
        Assert.DoesNotContain("entry-read-view", pane, StringComparison.Ordinal);
        Assert.DoesNotContain("State.BeginEdit", pane, StringComparison.Ordinal);

        // And nowhere else under src/ either. The shell was the last caller: it
        // opened the hatch on a triaged inbox item without selecting the row, which
        // is an editor with no surface to render in — the same anti-pattern one file
        // over, so the guard covers both files rather than only the one it was
        // written for.
        Assert.DoesNotContain(
            "State.BeginEdit",
            NormalizeLineEndings(File.ReadAllText(FindHomeRazor())),
            StringComparison.Ordinal);

        // The source is reached deliberately instead — the shortcut
        // .design/content-editing.md#raw-markdown-escape-hatch asks for. There is no
        // control for it: the row that used to open it said "Markdown" under a body
        // switch that already said "Markdown".
        Assert.DoesNotContain("entry-raw-toggle", pane, StringComparison.Ordinal);
        Assert.Contains("Ctrl+Shift+M", pane, StringComparison.Ordinal);
    }

    /// <summary>
    /// The shell composes the three contexts; it does not render them. If a pane's
    /// own markup starts leaking back into Home.razor, the folder split has
    /// stopped meaning anything.
    /// </summary>
    [Fact]
    public void The_shell_composes_the_panes_rather_than_rendering_them()
    {
        var home = NormalizeLineEndings(File.ReadAllText(FindHomeRazor()));

        // No items handed down, like the Tasks pane: the Inbox asks its own
        // module for them, and a shell handing them down would be a shell that
        // knows what an inbox item looks like. What the shell does pass is the
        // Capture button's three parameters - the run belongs to the Capture
        // context, which the Inbox never sees - and, in the pane's sources
        // slot, that context's own panel: composed here, so the Inbox draws a
        // region it cannot name, and nothing else.
        Assert.Contains("<InboxPane OnCapture=\"RunCaptureAsync\" CaptureRunning=\"_captureRunning\" CaptureMessage=\"@_captureMessage\">", home, StringComparison.Ordinal);
        Assert.Contains("<Sources>", home, StringComparison.Ordinal);
        Assert.Contains("<CaptureSourcesPanel Expanded=", home, StringComparison.Ordinal);
        Assert.DoesNotContain("<InboxPane Items=", home, StringComparison.Ordinal);
        Assert.DoesNotContain("OnAdd=", home, StringComparison.Ordinal);
        Assert.Contains("<TasksPane />", home, StringComparison.Ordinal);
        Assert.Contains("<DevbookPane RepositoryAlias=", home, StringComparison.Ordinal);

        // The band and the dashboard are composed on the same terms. Their content
        // belongs to Roadmap and Monitoring; the shell only decides where it goes.
        // The band takes no parameters at all, and that is the point: showing and
        // hiding it is binary, so the shell renders it or it does not, and there is
        // no state to hand down for an in-between size.
        Assert.Contains("<RoadmapBand />", home, StringComparison.Ordinal);
        Assert.Contains("<DashboardPane OnClose=", home, StringComparison.Ordinal);

        Assert.DoesNotContain("entry-doc__meta", home, StringComparison.Ordinal);
        Assert.DoesNotContain("inbox-pane__list", home, StringComparison.Ordinal);
        Assert.DoesNotContain("devbook-stack__nav", home, StringComparison.Ordinal);
        Assert.DoesNotContain("roadmap-band__content", home, StringComparison.Ordinal);
        Assert.DoesNotContain("dashboard-panel__content", home, StringComparison.Ordinal);
    }

    /// <summary>
    /// The two halves of the split scroll on their own, and the CSS the entry card
    /// needed is gone rather than left behind.
    /// <para>
    /// This replaces a pin on how the entry title line laid its metadata out without
    /// wrapping. There is no title line: a row in the list is the shared task row and
    /// the metadata strip only ever appears in the pane beside it, at one width. What
    /// is worth pinning about the new layout is the thing that would be wrong if
    /// somebody simplified it — one scrollbar for both halves, which would mean
    /// scrolling the list to reach the bottom of the entry next to it.
    /// </para>
    /// <para>
    /// The halves themselves are the library's: <c>SplitPane</c> gives each one
    /// <c>overflow: auto</c>, and that is what scrolls the list column. The list
    /// inside it must not be a second scroller. It used to declare one, and since
    /// the half is a block that hands the list its content height, the list was a
    /// scroll container that never scrolled — which is the one thing a sticky
    /// child cannot survive, because it measures against its nearest scroll
    /// container. The bulk bar and the add-entry row both stick to the half, and
    /// both scrolled straight out of view while the list claimed the job.
    /// </para>
    /// <para>
    /// The pane half scrolls one box deeper than the list's does: the panel fills the
    /// height it is given so the body inside it can, and a box that both stretched
    /// its child and scrolled it is a box that could do neither.
    /// </para>
    /// </summary>
    [Fact]
    public void Each_half_of_the_backlog_split_scrolls_on_its_own()
    {
        var css = NormalizeLineEndings(File.ReadAllText(FindAppCss()));
        var components = NormalizeLineEndings(File.ReadAllText(FindComponentsCss()));

        Assert.Contains(".backlog-list {", css, StringComparison.Ordinal);
        Assert.Contains(".entry-detail {", css, StringComparison.Ordinal);

        var halves = Block(components, ".split-pane__start,\n.split-pane__end {");
        Assert.Contains("overflow: auto;", halves, StringComparison.Ordinal);
        Assert.Contains("min-height: 0;", halves, StringComparison.Ordinal);

        var list = Block(css, ".backlog-list {");
        Assert.DoesNotContain("overflow", list, StringComparison.Ordinal);
        Assert.Contains("min-height: 0;", list, StringComparison.Ordinal);

        var panel = Block(css, ".entry-detail__panel {");
        Assert.Contains("overflow-y: auto;", panel, StringComparison.Ordinal);
        Assert.Contains("min-height: 0;", panel, StringComparison.Ordinal);

        static string Block(string sheet, string selector)
        {
            var start = sheet.IndexOf(selector, StringComparison.Ordinal);
            Assert.True(start >= 0, $"{selector} should be declared.");
            return sheet[start..sheet.IndexOf('}', start)];
        }
    }

    /// <summary>
    /// The two controls that stick to the list half, and the edge each holds. The
    /// bulk bar takes the top so the count and the way out of a selection stay in
    /// reach; the add-entry row takes the bottom so a column longer than the window
    /// never hides the one control that adds to it. Both are asserted together with
    /// the rule above because both are dead the moment the list becomes a scroller.
    /// </summary>
    [Fact]
    public void The_bulk_bar_and_the_add_entry_row_stick_to_the_list_half()
    {
        var css = NormalizeLineEndings(File.ReadAllText(FindAppCss()));

        var bar = Block(css, ".backlog-bulk-bar {");
        Assert.Contains("position: sticky;", bar, StringComparison.Ordinal);
        Assert.Contains("top: 0;", bar, StringComparison.Ordinal);

        var row = Block(css, ".entry-add-row {");
        Assert.Contains("position: sticky;", row, StringComparison.Ordinal);
        Assert.Contains("bottom: 0;", row, StringComparison.Ordinal);
        // Its buttons are transparent by design, so the row itself has to paint,
        // or the rows sliding under it read straight through the strip.
        Assert.Contains("background: var(--color-background);", row, StringComparison.Ordinal);

        static string Block(string sheet, string selector)
        {
            var start = sheet.IndexOf(selector, StringComparison.Ordinal);
            Assert.True(start >= 0, $"{selector} should be declared.");
            return sheet[start..sheet.IndexOf('}', start)];
        }
    }

    /// <summary>
    /// The height reaches the panel, box by box, from the workspace down.
    /// <para>
    /// The split asks for <c>flex: 1 1 auto</c> and the panel asks the split for
    /// what it was given, but a flex request is only answered by a flex parent. The
    /// workspace was a block, so the chain broke at the top of it and every box
    /// below sized itself to its own contents instead: the panel came out exactly
    /// as tall as the split, which was exactly as tall as the panel, and in a tall
    /// window the pair of them left a few hundred pixels of nothing underneath.
    /// </para>
    /// <para>
    /// Two halves to the chain, and they want opposite things. Above the scroller
    /// every box must be allowed to be shorter than its contents, or there is
    /// nothing for the panel to scroll. Inside it every box must not, or a short
    /// window collapses the regions towards zero and the editor's own rows end up
    /// outside every ancestor that could have reported them — which leaves the
    /// panel with nothing to scroll and the writing off the bottom of it.
    /// </para>
    /// <para>
    /// Each link is pinned rather than the outcome, because the outcome is a
    /// rendered height and this is a stylesheet. What would break it is any one of
    /// these going missing, and the one that did was the first.
    /// </para>
    /// </summary>
    [Fact]
    public void The_open_entrys_panel_is_given_the_full_height_of_the_workspace()
    {
        var css = NormalizeLineEndings(File.ReadAllText(FindAppCss()));

        var workspace = RuleFor(css, ".backlog-workspace {");
        Assert.Contains("display: flex;", workspace, StringComparison.Ordinal);
        Assert.Contains("flex-direction: column;", workspace, StringComparison.Ordinal);
        Assert.Contains("min-height: 0;", workspace, StringComparison.Ordinal);

        // A second scrollbar here is what would let the split stop short again.
        Assert.DoesNotContain("overflow: auto;", workspace, StringComparison.Ordinal);

        // And the same for the half the pane sits in: the shared split leaves it a
        // scrolling block, which is a block formatting context, which is a floor the
        // height does not get through.
        var half = RuleFor(css, ".backlog-split > .split-pane__end {");
        Assert.Contains("display: flex;", half, StringComparison.Ordinal);
        Assert.Contains("flex-direction: column;", half, StringComparison.Ordinal);
        Assert.Contains("min-height: 0;", half, StringComparison.Ordinal);
        Assert.DoesNotContain("overflow: auto;", half, StringComparison.Ordinal);

        // Down to the scroller: shorter than its contents is allowed, and required.
        foreach (var block in new[] { ".backlog-split {", ".entry-detail {", ".entry-detail__panel {" })
        {
            var rules = RuleFor(css, block);

            Assert.Contains("flex: 1 1 auto;", rules, StringComparison.Ordinal);
            Assert.Contains("min-height: 0;", rules, StringComparison.Ordinal);
        }

        Assert.Contains("overflow-y: auto;", RuleFor(css, ".entry-detail__panel {"), StringComparison.Ordinal);

        // And below it: take the leftover, but never give up what the reading needs.
        foreach (var block in new[]
        {
            ".entry-detail__panel .task-panel__body {",
            ".entry-detail__body {",
            ".entry-detail__view:not([hidden]) {",
            ".entry-detail__note {",
            ".entry-detail__note .markdown-editor__surface {",
            ".entry-detail__note .markdown-editor__grow {"
        })
        {
            var rules = RuleFor(css, block);

            Assert.Contains("flex: 1 0 auto;", rules, StringComparison.Ordinal);
            Assert.DoesNotContain("min-height: 0;", rules, StringComparison.Ordinal);
        }
    }

    /// <summary>The 960px step's override of a rule. Indentation alone separates it
    /// from the rule it overrides, because a top-level rule starts at column zero —
    /// which is also why <see cref="RuleFor"/> keeps finding that one.</summary>
    private static string CompactRuleFor(string css, string block)
    {
        var media = css.IndexOf("@media (max-width: 960px) {", StringComparison.Ordinal);
        Assert.True(media >= 0, "The compact breakpoint is no longer in the stylesheet.");

        var start = css.IndexOf("    " + block, media, StringComparison.Ordinal);
        Assert.True(start >= 0, $"The compact step has no override for `{block}`.");

        return css[start..css.IndexOf('}', start)];
    }

    private static string RuleFor(string css, string block)
    {
        // Anchored to the start of a line, so `.backlog-workspace {` is not found
        // inside `.devbook-layout--side-closed .backlog-workspace {`.
        var start = css.IndexOf($"\n{block}", StringComparison.Ordinal);
        Assert.True(start >= 0, $"`{block}` is not a rule of its own in the stylesheet.");

        return css[start..css.IndexOf('}', start)];
    }

    // The import result used to be pinned here as a compact footer under the list,
    // with a test of its own holding `.import-plan-result` in place. It is a toast
    // now — raised on the shared channel, drawn by the tray MainLayout mounts — so
    // the rule and the test that existed to pin it went together. What replaced
    // that coverage is the pair of facts below: where the tray is mounted, and what
    // the element it hangs off is allowed to be.

    /// <summary>
    /// The toast tray is mounted once, by the layout, between the routed page and
    /// the footer.
    /// <para>
    /// Its position in that order is the whole design. A tray that is a
    /// <em>descendant</em> of the footer would be a positioned ancestor of the two
    /// fixed Modals rendered from inside it, which is the thing the warning on
    /// <c>.app-footer</c> forbids; a tray above <c>@Body</c> would stack the wrong
    /// way off the band. So this asserts the order rather than merely the presence,
    /// and it does so alongside
    /// <see cref="The_layout_wraps_the_routed_body_and_the_footer_in_one_column"/>
    /// rather than inside it: that test is about the column, this one is about what
    /// hangs in it.
    /// </para>
    /// </summary>
    [Fact]
    public void The_toast_tray_hangs_between_the_page_and_the_band()
    {
        var layout = NormalizeLineEndings(File.ReadAllText(FindMainLayoutRazor()));

        Assert.Contains("class=\"app-toast-anchor\"", layout, StringComparison.Ordinal);
        Assert.Contains("<ToastTray", layout, StringComparison.Ordinal);

        var body = layout.IndexOf("@Body", StringComparison.Ordinal);
        var anchor = layout.IndexOf("class=\"app-toast-anchor\"", StringComparison.Ordinal);
        var tray = layout.IndexOf("<ToastTray", StringComparison.Ordinal);
        var footer = layout.IndexOf("<AppFooter />", StringComparison.Ordinal);

        Assert.True(body < anchor, "The tray's anchor follows the routed body.");
        Assert.InRange(tray, anchor, footer);
        Assert.True(anchor < footer, "The tray's anchor is a sibling above the footer, never inside it.");
    }

    /// <summary>
    /// The anchor is a line of no height that positions the tray, and nothing else.
    /// <para>
    /// It exists so the tray can rise off the band without anything naming the
    /// band's height — that height is content-driven and is not the same as it was.
    /// Asserted explicitly because the tempting simplification is to move the tray
    /// inside <c>AppFooter</c> and delete this, and that would silently clip the
    /// update window and the feedback dialog to the height of the footer. This is
    /// the assertion that makes that fail loudly instead.
    /// </para>
    /// </summary>
    [Fact]
    public void The_toast_anchor_takes_no_room_and_creates_no_containing_block()
    {
        var css = NormalizeLineEndings(File.ReadAllText(FindAppCss()));

        var anchor = RuleFor(css, ".app-toast-anchor {");

        Assert.Contains("position: relative;", anchor, StringComparison.Ordinal);
        Assert.Contains("height: 0;", anchor, StringComparison.Ordinal);

        // No z-index, so it creates no stacking context and the tray's own z-index
        // still competes at the root — which is what puts a toast above the shell.
        Assert.DoesNotContain("z-index", anchor, StringComparison.Ordinal);

        // The library's host is fixed to the viewport corner; this is the modifier
        // that re-anchors it, and CssClass is the hook ui-components.instructions.md
        // blesses for exactly that, so no second tray exists.
        var tray = RuleFor(css, ".toast-host.app-toast-tray {");

        Assert.Contains("position: absolute;", tray, StringComparison.Ordinal);
        Assert.Contains("inset-block-end: var(--spacing-sm);", tray, StringComparison.Ordinal);
        Assert.Contains("inset-inline-end: var(--spacing-lg);", tray, StringComparison.Ordinal);

        // Tokens only: an offset in pixels here is the drift ADR 0011 exists to stop.
        Assert.DoesNotContain("px", tray, StringComparison.Ordinal);
    }

    /// <summary>
    /// The entry card, its four grab rails, its drop zones and the sub-item cards
    /// are gone from the stylesheet as well as from the markup.
    /// <para>
    /// Dead CSS is not harmless: it is the next reader's evidence that a shape still
    /// exists. Every selector below styled something the shared components now draw,
    /// and a rule for it surviving would describe a card nobody renders.
    /// </para>
    /// </summary>
    [Fact]
    public void The_replaced_entry_card_css_was_removed_rather_than_left_behind()
    {
        var css = NormalizeLineEndings(File.ReadAllText(FindAppCss()));

        foreach (var dead in new[]
                 {
                     ".entry-list {",
                     ".entry-group {",
                     ".entry-doc {",
                     ".entry-doc--one-line",
                     ".entry-doc__grip",
                     ".entry-doc__drop",
                     ".entry-doc__read {",
                     ".entry-doc__title-line",
                     ".subitem-card",
                     ".subitem-list"
                 })
        {
            Assert.DoesNotContain(dead, css, StringComparison.Ordinal);
        }
    }

    private static string NormalizeLineEndings(string text) => text.Replace("\r\n", "\n");

    private static int CountOccurrences(string text, string value)
    {
        var count = 0;
        var at = text.IndexOf(value, StringComparison.Ordinal);
        while (at >= 0)
        {
            count++;
            at = text.IndexOf(value, at + value.Length, StringComparison.Ordinal);
        }

        return count;
    }

    private static string FindAppCss() => RepositoryRoot.File("src", "App", "Backlog.Desktop.UI", "wwwroot", "app.css");

    private static string FindComponentsCss() => RepositoryRoot.File("src", "Core", "Backlog.UI.Components", "wwwroot", "components.css");

    private static string FindHomeRazor() => RepositoryRoot.File("src", "App", "Backlog.Desktop.UI", "Shell", "Home.razor");

    // The app shell above the routes, and the footer it mounts. Neither belongs to
    // a page, which is the whole point of asserting against them separately from
    // Home: what they carry has to hold on every route, not just the one below.
    private static string FindMainLayoutRazor() => RepositoryRoot.File("src", "App", "Backlog.Desktop.UI", "Shell", "MainLayout.razor");

    private static string FindAppFooterRazor() => RepositoryRoot.File("src", "App", "Backlog.Desktop.UI", "Shell", "AppFooter.razor");

    private static string FindSettingsRazor() => RepositoryRoot.File("src", "App", "Backlog.Desktop.UI", "Settings", "Settings.razor");

    private static string FindWorkspaceSurface() => RepositoryRoot.File("src", "App", "Backlog.Desktop.UI", "Shell", "WorkspaceSurface.cs");

    // The three bounded contexts left Backlog.Desktop.UI and became their own
    // projects under src/Modules; only the shell's own chrome stayed behind.
    private static string FindInboxPane() => RepositoryRoot.File("src", "Modules", "Inbox", "Backlog.Modules.Inbox.UI", "InboxPane.razor");

    private static string FindTasksPane() => RepositoryRoot.File("src", "Modules", "Tasks", "Backlog.Modules.Tasks.UI", "TasksPane.razor");

    private static string FindDevbookPane() => RepositoryRoot.File("src", "Modules", "Devbook", "Backlog.Modules.Devbook.UI", "DevbookPane.razor");

    private static string FindAppJs() => RepositoryRoot.File("src", "App", "Backlog.Desktop.UI", "wwwroot", "app.js");

    private static string FindComponentsJs() => RepositoryRoot.File("src", "Core", "Backlog.UI.Components", "wwwroot", "components.js");
}
