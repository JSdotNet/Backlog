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

        // "Sections" rather than "panes": the strip also shows and hides the roadmap
        // band, and a band is a row above the panes rather than one of them.
        Assert.Contains("AriaLabel=\"Visible sections\"", home, StringComparison.Ordinal);
        Assert.DoesNotContain("Visible panes", home, StringComparison.Ordinal);

        // The four options are the shared ToggleButton, so their test ids reach
        // the DOM through its TestId parameter rather than literal attributes.
        Assert.Contains("TestId=\"roadmap-pane-option\"", home, StringComparison.Ordinal);
        Assert.Contains("TestId=\"inbox-pane-option\"", home, StringComparison.Ordinal);
        Assert.Contains("TestId=\"backlog-pane-option\"", home, StringComparison.Ordinal);
        Assert.Contains("TestId=\"knowledge-pane-option\"", home, StringComparison.Ordinal);

        // The band leads, because it is the thing highest on screen.
        Assert.True(
            home.IndexOf("TestId=\"roadmap-pane-option\"", StringComparison.Ordinal)
            < home.IndexOf("TestId=\"inbox-pane-option\"", StringComparison.Ordinal),
            "The Roadmap option comes first in the strip: the band sits above the panes.");

        // Each pane carries its own landmark id from its own folder; the shell
        // only points the multiselect's aria-controls at them. The band is the same
        // arrangement one level up — its landmark id lives in the Roadmap module.
        Assert.Contains("id=\"inbox-pane\"", NormalizeLineEndings(File.ReadAllText(FindInboxPane())), StringComparison.Ordinal);
        Assert.Contains("id=\"backlog-pane\"", NormalizeLineEndings(File.ReadAllText(FindBacklogPane())), StringComparison.Ordinal);
        Assert.Contains("id=\"repository-knowledge-pane\"", NormalizeLineEndings(File.ReadAllText(FindKnowledgePane())), StringComparison.Ordinal);
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
        Assert.Contains("TestId=\"sessions-toggle-button\"", home, StringComparison.Ordinal);

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
        Assert.Contains("PressedChanged=\"ToggleSessions\"", home, StringComparison.Ordinal);
        Assert.DoesNotContain("aria-expanded=\"@(ToolsVisible", home, StringComparison.Ordinal);
        Assert.DoesNotContain("aria-expanded=\"@(DashboardVisible", home, StringComparison.Ordinal);
        Assert.DoesNotContain("aria-expanded=\"@(SessionsVisible", home, StringComparison.Ordinal);
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
        Assert.True(home.IndexOf("TestId=\"feedback-button\"", StringComparison.Ordinal) > utilities);
        Assert.True(home.IndexOf("TestId=\"app-version\"", StringComparison.Ordinal) > utilities);
        Assert.True(home.IndexOf("data-testid=\"settings-link\"", StringComparison.Ordinal) > utilities);

        // Version sits at the right edge with settings after it.
        Assert.True(
            home.IndexOf("TestId=\"app-version\"", StringComparison.Ordinal)
            < home.IndexOf("data-testid=\"settings-link\"", StringComparison.Ordinal),
            "Settings is the last control in the header.");

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

        Assert.Contains("@if (RoadmapPaneOptionVisible)", home, StringComparison.Ordinal);
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

    [Fact]
    public void Pane_multiselect_uses_selected_state_and_capacity_aware_disabling()
    {
        var home = NormalizeLineEndings(File.ReadAllText(FindHomeRazor()));

        // ToggleButton derives aria-pressed from Pressed, so the visibility of a
        // pane is stated once and the attribute cannot drift away from it.
        Assert.Contains("Pressed=\"RoadmapBandVisible\"", home, StringComparison.Ordinal);
        Assert.Contains("Pressed=\"InboxPaneVisible\"", home, StringComparison.Ordinal);
        Assert.Contains("Pressed=\"BacklogPaneVisible\"", home, StringComparison.Ordinal);
        Assert.Contains("Pressed=\"KnowledgePaneVisible\"", home, StringComparison.Ordinal);

        // Three panes have the capacity rule and the band does not, so exactly three
        // options are ever disabled by it. A fourth would mean the band had been
        // folded into the selection.
        Assert.Equal(3, CountOccurrences(home, "Disabled=\"@PaneToggleDisabled("));

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

        Assert.Contains("(BacklogPaneVisible && RightSidePaneVisible) ? \"knowledge-layout--side-open\"", home, StringComparison.Ordinal);
        Assert.Contains("side-pane-stack--full", home, StringComparison.Ordinal);

        // Tools left the side stack for a full-screen surface of its own, so a
        // stack holding nothing but the tools pane can no longer happen — and the
        // split must not open for it. Stated on the property rather than on the
        // class the stack used to grow, because the docked modifier is gone.
        Assert.Contains(
            "private bool RightSidePaneVisible => KnowledgePaneVisible || (InboxPaneVisible && !BacklogPaneVisible);",
            home,
            StringComparison.Ordinal);
        Assert.DoesNotContain("side-pane-stack--right-docked", home, StringComparison.Ordinal);
    }

    /// <summary>
    /// Tools, the Dashboard and Sessions are takeovers, not panes. Each is the
    /// page's single <c>main</c> landmark while it is open, which is only true as
    /// long as the branches stay mutually exclusive in the markup.
    /// </summary>
    [Fact]
    public void Only_one_surface_renders_and_it_owns_the_main_landmark()
    {
        var home = NormalizeLineEndings(File.ReadAllText(FindHomeRazor()));

        Assert.Contains("@if (ToolsVisible)", home, StringComparison.Ordinal);
        Assert.Contains("else if (DashboardVisible)", home, StringComparison.Ordinal);
        Assert.Contains("else if (SessionsVisible)", home, StringComparison.Ordinal);
        Assert.Contains("data-testid=\"tools-surface\"", home, StringComparison.Ordinal);
        Assert.Contains("data-testid=\"dashboard-surface\"", home, StringComparison.Ordinal);
        Assert.Contains("data-testid=\"sessions-surface\"", home, StringComparison.Ordinal);
        Assert.Contains("data-testid=\"workspace\"", home, StringComparison.Ordinal);

        // One landmark per branch, and the branches are exclusive, so the page has
        // exactly one. Two <main> elements is the failure this counts, which is why
        // the number rises with each takeover rather than being loosened to "some".
        Assert.Equal(4, CountOccurrences(home, "<main class="));

        // The pane row keeps the test id the resizer's JavaScript selects on; what
        // changed is that it is no longer the landmark itself.
        Assert.Contains("<div class=\"knowledge-layout ", home, StringComparison.Ordinal);
        Assert.DoesNotContain("<main class=\"knowledge-layout", home, StringComparison.Ordinal);
        Assert.Contains("data-testid=\"knowledge-layout\"", home, StringComparison.Ordinal);
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

        Assert.Contains("@if (InboxBeforeBacklogVisible)", home, StringComparison.Ordinal);
        Assert.Contains("@if (!BacklogPaneVisible && InboxPaneVisible)", home, StringComparison.Ordinal);
        Assert.Contains("knowledge-layout--inbox-before-backlog", home, StringComparison.Ordinal);

        var inboxGuardIndex = home.IndexOf("@if (InboxBeforeBacklogVisible)", StringComparison.Ordinal);
        var backlogPaneIndex = home.IndexOf("<BacklogPane />", StringComparison.Ordinal);

        Assert.True(inboxGuardIndex >= 0);
        Assert.True(backlogPaneIndex > inboxGuardIndex);
    }

    [Fact]
    public void App_version_opens_a_separate_update_window()
    {
        var home = NormalizeLineEndings(File.ReadAllText(FindHomeRazor()));

        Assert.Contains("TestId=\"app-version\"", home, StringComparison.Ordinal);
        Assert.Contains("OnClick=\"OpenUpdateWindow\"", home, StringComparison.Ordinal);
        // The dialog shell is now the shared Modal component, so the test id
        // reaches the DOM through its TestId parameter instead of a literal
        // attribute.
        Assert.Contains("TestId=\"app-update-dialog\"", home, StringComparison.Ordinal);
        Assert.Contains("data-testid=\"check-for-updates\"", home, StringComparison.Ordinal);
        Assert.Contains("data-testid=\"install-update\"", home, StringComparison.Ordinal);
        Assert.DoesNotContain("app-version__hint", home, StringComparison.Ordinal);
    }

    [Fact]
    public void Knowledge_folder_errors_do_not_use_empty_razor_fragment_tags()
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
        var pane = NormalizeLineEndings(File.ReadAllText(FindBacklogPane()));

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
        var pane = NormalizeLineEndings(File.ReadAllText(FindBacklogPane()));

        // Quoted, because `entry-doc__reading` under the escape hatch is a prefix of
        // it and is a different thing: a hint about what the source parses to, not a
        // surface that opens an editor.
        Assert.DoesNotContain("\"entry-doc__read\"", pane, StringComparison.Ordinal);
        Assert.DoesNotContain("entry-read-view", pane, StringComparison.Ordinal);
        Assert.DoesNotContain("State.BeginEdit", pane, StringComparison.Ordinal);

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

        Assert.Contains("<InboxPane Items=", home, StringComparison.Ordinal);
        Assert.Contains("<BacklogPane />", home, StringComparison.Ordinal);
        Assert.Contains("<KnowledgePane RepositoryAlias=", home, StringComparison.Ordinal);

        // The band and the dashboard are composed on the same terms. Their content
        // belongs to Roadmap and Monitoring; the shell only decides where it goes.
        // The band takes no parameters at all, and that is the point: showing and
        // hiding it is binary, so the shell renders it or it does not, and there is
        // no state to hand down for an in-between size.
        Assert.Contains("<RoadmapBand />", home, StringComparison.Ordinal);
        Assert.Contains("<DashboardPane OnClose=", home, StringComparison.Ordinal);

        Assert.DoesNotContain("entry-doc__meta", home, StringComparison.Ordinal);
        Assert.DoesNotContain("inbox-pane__list", home, StringComparison.Ordinal);
        Assert.DoesNotContain("knowledge-stack__nav", home, StringComparison.Ordinal);
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
    /// The pane half scrolls one box deeper than the list's does: the panel fills the
    /// height it is given so the body inside it can, and a box that both stretched
    /// its child and scrolled it is a box that could do neither.
    /// </para>
    /// </summary>
    [Fact]
    public void Each_half_of_the_backlog_split_scrolls_on_its_own()
    {
        var css = NormalizeLineEndings(File.ReadAllText(FindAppCss()));

        Assert.Contains(".backlog-list {", css, StringComparison.Ordinal);
        Assert.Contains(".entry-detail {", css, StringComparison.Ordinal);

        foreach (var block in new[] { ".backlog-list {", ".entry-detail__panel {" })
        {
            var start = css.IndexOf(block, StringComparison.Ordinal);
            var rules = css[start..css.IndexOf('}', start)];

            Assert.Contains("overflow-y: auto;", rules, StringComparison.Ordinal);
            Assert.Contains("min-height: 0;", rules, StringComparison.Ordinal);
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

    private static string RuleFor(string css, string block)
    {
        // Anchored to the start of a line, so `.backlog-workspace {` is not found
        // inside `.knowledge-layout--side-closed .backlog-workspace {`.
        var start = css.IndexOf($"\n{block}", StringComparison.Ordinal);
        Assert.True(start >= 0, $"`{block}` is not a rule of its own in the stylesheet.");

        return css[start..css.IndexOf('}', start)];
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

    private static string FindHomeRazor() => RepositoryRoot.File("src", "App", "Backlog.Desktop.UI", "Shell", "Home.razor");

    private static string FindWorkspaceSurface() => RepositoryRoot.File("src", "App", "Backlog.Desktop.UI", "Shell", "WorkspaceSurface.cs");

    // The three bounded contexts left Backlog.Desktop.UI and became their own
    // projects under src/Modules; only the shell's own chrome stayed behind.
    private static string FindInboxPane() => RepositoryRoot.File("src", "Modules", "Inbox", "Backlog.Modules.Inbox.UI", "InboxPane.razor");

    private static string FindBacklogPane() => RepositoryRoot.File("src", "Modules", "Backlog", "Backlog.Modules.Backlog.UI", "BacklogPane.razor");

    private static string FindKnowledgePane() => RepositoryRoot.File("src", "Modules", "Knowledge", "Backlog.Modules.Knowledge.UI", "KnowledgePane.razor");

    private static string FindAppJs() => RepositoryRoot.File("src", "App", "Backlog.Desktop.UI", "wwwroot", "app.js");

    private static string FindComponentsJs() => RepositoryRoot.File("src", "Core", "Backlog.UI.Components", "wwwroot", "components.js");
}
