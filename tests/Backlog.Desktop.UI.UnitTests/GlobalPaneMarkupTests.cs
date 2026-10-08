namespace Backlog.Desktop.UI.UnitTests;

public sealed class GlobalPaneMarkupTests
{
    [Fact]
    public void Home_shell_exposes_the_view_switch_and_the_side_pane_toggles()
    {
        var home = NormalizeLineEndings(File.ReadAllText(FindHomeRazor()));

        // The view switch is the shared ButtonGroup, so its test id and its label
        // reach the DOM through the component's TestId and AriaLabel parameters
        // rather than as literal attributes.
        Assert.Contains("TestId=\"view-switch\"", home, StringComparison.Ordinal);
        Assert.Contains("AriaLabel=\"View\"", home, StringComparison.Ordinal);
        Assert.Contains("TestId=\"tasks-view-option\"", home, StringComparison.Ordinal);
        Assert.Contains("TestId=\"roadmap-view-option\"", home, StringComparison.Ordinal);

        // The side panes are two toggles, and the task list is no longer one of them:
        // the pane strip, its label and the Tasks option are gone.
        Assert.Contains("TestId=\"inbox-pane-option\"", home, StringComparison.Ordinal);
        Assert.Contains("TestId=\"devbook-pane-option\"", home, StringComparison.Ordinal);
        Assert.DoesNotContain("backlog-pane-option", home, StringComparison.Ordinal);
        Assert.DoesNotContain("global-pane-multiselect", home, StringComparison.Ordinal);
        Assert.DoesNotContain("Visible panes", home, StringComparison.Ordinal);

        // The roadmap is a view, not a takeover: no surface landmark of its own, and
        // no takeover toggle.
        Assert.DoesNotContain("roadmap-toggle-button", home, StringComparison.Ordinal);
        Assert.DoesNotContain("roadmap-surface", home, StringComparison.Ordinal);
        Assert.DoesNotContain("roadmap-pane-option", home, StringComparison.Ordinal);

        // Each view and pane carries its own landmark id; the shell only points the
        // options' aria-controls at them.
        Assert.Contains("id=\"inbox-pane\"", NormalizeLineEndings(File.ReadAllText(FindInboxPane())), StringComparison.Ordinal);
        Assert.Contains("id=\"backlog-pane\"", NormalizeLineEndings(File.ReadAllText(FindTasksPane())), StringComparison.Ordinal);
        Assert.Contains("id=\"repository-devbook-pane\"", NormalizeLineEndings(File.ReadAllText(FindDevbookPane())), StringComparison.Ordinal);
        Assert.Contains("aria-controls=\"backlog-pane\"", home, StringComparison.Ordinal);
        Assert.Contains("aria-controls=\"roadmap-view\"", home, StringComparison.Ordinal);
        Assert.Contains("id=\"roadmap-view\"", home, StringComparison.Ordinal);
    }

    /// <summary>
    /// The nav, left to right, as the design draws it: the Inbox toggle, the view
    /// switch, the Devbook toggle, the work in progress, and the other takeovers.
    /// Every takeover is named here rather than counted, so adding one to the header
    /// without adding it to a group cannot pass. They are pressed states, not
    /// disclosures: <c>WorkspaceSurface</c> is one field with one state at a time.
    /// </summary>
    [Fact]
    public void The_nav_orders_the_workspace_controls_then_the_takeover_groups()
    {
        var home = NormalizeLineEndings(File.ReadAllText(FindHomeRazor()));

        var order = new[]
        {
            "<nav class=\"app-header__nav\"",
            "TestId=\"inbox-pane-option\"",
            "TestId=\"view-switch\"",
            "TestId=\"tasks-view-option\"",
            "TestId=\"board-view-option\"",
            "TestId=\"calendar-view-option\"",
            "TestId=\"roadmap-view-option\"",
            "TestId=\"devbook-pane-option\"",
            "TestId=\"work-in-progress-switcher\"",
            "TestId=\"sessions-toggle-button\"",
            "TestId=\"pull-requests-toggle-button\"",
            "TestId=\"workspace-surface-switcher\"",
            "TestId=\"dashboard-toggle-button\"",
            "TestId=\"tools-toggle-button\"",
            "</nav>",
        };
        for (var i = 1; i < order.Length; i++)
        {
            Assert.True(
                home.IndexOf(order[i - 1], StringComparison.Ordinal) < home.IndexOf(order[i], StringComparison.Ordinal),
                $"{order[i - 1]} comes before {order[i]}.");
        }

        Assert.DoesNotContain("workspace-surface-option", home, StringComparison.Ordinal);
        Assert.DoesNotContain("PressedChanged=\"CloseSurface\"", home, StringComparison.Ordinal);
        Assert.DoesNotContain("ListTestId=\"dashboard-tabs\"", home, StringComparison.Ordinal);

        // A selection, not a disclosure. Only Ask AI keeps aria-expanded, because
        // only its panel opens beside the content rather than replacing it.
        Assert.Contains("PressedChanged=\"ToggleTools\"", home, StringComparison.Ordinal);
        Assert.Contains("PressedChanged=\"ToggleDashboard\"", home, StringComparison.Ordinal);
        Assert.Contains("PressedChanged=\"ToggleSessions\"", home, StringComparison.Ordinal);
        Assert.Contains("PressedChanged=\"TogglePullRequests\"", home, StringComparison.Ordinal);
        Assert.DoesNotContain("ToggleRoadmap", home, StringComparison.Ordinal);
        Assert.Contains("aria-expanded=\"@(_aiExpanded ? \"true\" : \"false\")\"", home, StringComparison.Ordinal);
        Assert.DoesNotContain("aria-expanded=\"@_aiExpanded\"", home, StringComparison.Ordinal);

        // Each takeover group renders only when one of its takeovers is offered; the
        // view switch always renders, because it is the way back from a takeover.
        Assert.Contains("@if (SurfaceSwitcherVisible)", home, StringComparison.Ordinal);
        Assert.Contains("@if (SessionsOffered || PullRequestsOffered)", home, StringComparison.Ordinal);
        var nav = home.IndexOf("<nav class=\"app-header__nav\"", StringComparison.Ordinal);
        var viewSwitch = home.IndexOf("TestId=\"view-switch\"", StringComparison.Ordinal);
        var inboxGuard = home.IndexOf("@if (InboxPaneOptionVisible)", nav, StringComparison.Ordinal);
        Assert.DoesNotContain("@if", home[(home.IndexOf('}', inboxGuard) + 1)..viewSwitch], StringComparison.Ordinal);

        // During a takeover no option reads pressed.
        Assert.Contains(
            "private bool PaneOptionPressed(GlobalPane pane) => WorkspaceVisible && _globalPanes.IsEnabled(pane);",
            home,
            StringComparison.Ordinal);
        Assert.Contains(
            "private bool ViewOptionPressed(ShellView view) => WorkspaceVisible && EffectiveView == view;",
            home,
            StringComparison.Ordinal);

        Assert.Contains("id=\"workspace\"", home, StringComparison.Ordinal);
    }

    /// <summary>
    /// The shape of a header group says how its options belong together: fused where
    /// they are siblings read as one control — the repository scope, the view switch,
    /// and the work in progress — loose where they are related only by kind, the
    /// Dashboard and Tools. The Inbox and Devbook toggles stand alone, each wearing
    /// the loose option shape with no group around it. The markup carries the
    /// modifier and the stylesheet keys on it, so both halves are pinned here.
    /// </summary>
    [Fact]
    public void A_header_groups_shape_says_how_its_options_belong_together()
    {
        var home = NormalizeLineEndings(File.ReadAllText(FindHomeRazor()));
        var css = NormalizeLineEndings(File.ReadAllText(FindAppCss()));

        Assert.Contains(
            "<ButtonGroup CssClass=\"header-group app-header__repository-scope\"",
            home,
            StringComparison.Ordinal);
        Assert.DoesNotContain("<div class=\"app-header__repository-scope\"", home, StringComparison.Ordinal);

        Assert.Contains("<ButtonGroup CssClass=\"header-group header-group--views\"", home, StringComparison.Ordinal);
        Assert.Contains("<ButtonGroup CssClass=\"header-group header-group--work\"", home, StringComparison.Ordinal);
        Assert.Contains(
            "<ButtonGroup CssClass=\"header-group header-group--loose header-group--surface\"",
            home,
            StringComparison.Ordinal);
        Assert.DoesNotContain("header-group--panes", home, StringComparison.Ordinal);

        // Two standalone toggles and the two loose takeovers wear the loose option.
        Assert.Equal(4, CountOccurrences(home, "<ToggleButton BaseClass=\"header-group__option header-group__option--loose\""));

        // The workspace's own three controls are one cluster, closer together than
        // the groups around them.
        Assert.Contains("<div class=\"app-header__workspace\" data-testid=\"workspace-navigation\">", home, StringComparison.Ordinal);
        Assert.Contains("gap: var(--spacing-xs);", RuleFor(css, ".app-header__workspace {"), StringComparison.Ordinal);

        var loose = RuleFor(css, ".header-group--loose {");
        Assert.Contains("border: 0;", loose, StringComparison.Ordinal);
        Assert.Contains("background: transparent;", loose, StringComparison.Ordinal);
        Assert.Contains("gap: var(--spacing-xs);", loose, StringComparison.Ordinal);

        var looseOption = RuleFor(css, ".header-group__option--loose {");
        Assert.Contains("border: var(--border-width) solid var(--color-border);", looseOption, StringComparison.Ordinal);
        Assert.Contains("border-radius: var(--border-radius-md);", looseOption, StringComparison.Ordinal);
        Assert.DoesNotContain(".header-group--loose > .header-group__option", css, StringComparison.Ordinal);

        Assert.Contains(
            "border-color: var(--color-primary);",
            RuleFor(css, ".header-group__option--loose.header-group__option--selected {"),
            StringComparison.Ordinal);

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
    /// that only the navigation groups live inside the nav landmark.
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

        Assert.True(nav < status && status < utilities);

        Assert.InRange(home.IndexOf("TestId=\"view-switch\"", StringComparison.Ordinal), nav, status);
        Assert.InRange(home.IndexOf("TestId=\"work-in-progress-switcher\"", StringComparison.Ordinal), nav, status);
        Assert.InRange(home.IndexOf("TestId=\"workspace-surface-switcher\"", StringComparison.Ordinal), nav, status);

        Assert.True(home.IndexOf("TestId=\"ai-toggle-button\"", StringComparison.Ordinal) > utilities);
        Assert.True(home.IndexOf("data-testid=\"settings-link\"", StringComparison.Ordinal) > utilities);
        Assert.True(
            home.IndexOf("TestId=\"ai-toggle-button\"", StringComparison.Ordinal)
            < home.IndexOf("data-testid=\"settings-link\"", StringComparison.Ordinal),
            "Settings is the last control in the header.");

        Assert.DoesNotContain("<FeedbackReportButton", home, StringComparison.Ordinal);
        Assert.DoesNotContain("TestId=\"app-version\"", home, StringComparison.Ordinal);

        Assert.DoesNotContain("\"pane-multiselect", home, StringComparison.Ordinal);
        Assert.DoesNotContain("pane-multiselect__option", home, StringComparison.Ordinal);
        Assert.DoesNotContain("header-tool-toggle", home, StringComparison.Ordinal);
    }

    /// <summary>
    /// The roadmap's option is gated on its feature the way the Inbox option is, and
    /// it is a view, not a takeover and not a pane: it lives in <c>ShellView</c> beside
    /// Tasks. <c>WorkspaceSurface.Roadmap</c> stays only as a stored name an older file
    /// may hold, and nothing assigns it.
    /// </summary>
    [Fact]
    public void Roadmap_option_is_feature_gated_and_is_a_view_not_a_takeover()
    {
        var home = NormalizeLineEndings(File.ReadAllText(FindHomeRazor()));
        var surface = NormalizeLineEndings(File.ReadAllText(FindWorkspaceSurface()));

        Assert.Contains("@if (RoadmapViewOffered)", home, StringComparison.Ordinal);
        Assert.Contains("private bool RoadmapViewOffered => IsFeatureEnabled(RoadmapFeatures.Roadmap);", home, StringComparison.Ordinal);
        Assert.Contains(
            "private bool WorkspaceVisible => !ToolsVisible && !DashboardVisible && !SessionsVisible && !PullRequestsVisible;",
            home,
            StringComparison.Ordinal);
        Assert.Contains(
            "ShellView.Roadmap when !RoadmapViewOffered => ShellView.Tasks,",
            home,
            StringComparison.Ordinal);

        // Readable as a stored name, never assigned as a surface.
        Assert.Contains("    Roadmap,\n", surface, StringComparison.Ordinal);
        Assert.DoesNotContain("_surface = WorkspaceSurface.Roadmap", home, StringComparison.Ordinal);
        Assert.DoesNotContain("ToggleSurface(WorkspaceSurface.Roadmap)", home, StringComparison.Ordinal);

        Assert.DoesNotContain("GlobalPane.Roadmap", home, StringComparison.Ordinal);
        Assert.DoesNotContain("GlobalPane.Tasks", home, StringComparison.Ordinal);
        Assert.DoesNotContain("RoadmapBandVisible", home, StringComparison.Ordinal);
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
    /// The side-pane toggles state their pressed state once, through ToggleButton's
    /// Pressed, and press through one handler that opens the pane beside the view or
    /// closes it. Nothing disables them: closing the last side pane leaves the view
    /// on screen, and opening one makes its own room.
    /// </summary>
    [Fact]
    public void Side_pane_toggles_use_selected_state_and_are_never_disabled()
    {
        var home = NormalizeLineEndings(File.ReadAllText(FindHomeRazor()));

        foreach (var pane in new[] { "Inbox", "Devbook" })
        {
            Assert.Contains($"Pressed=\"PaneOptionPressed(GlobalPane.{pane})\"", home, StringComparison.Ordinal);
            Assert.Contains($"OnClick=\"() => PressPane(GlobalPane.{pane})\"", home, StringComparison.Ordinal);
            Assert.Contains($"Title=\"@PaneOptionTitle(GlobalPane.{pane}, \"", home, StringComparison.Ordinal);
        }

        foreach (var view in new[] { "Tasks", "Roadmap" })
        {
            Assert.Contains($"Pressed=\"ViewOptionPressed(ShellView.{view})\"", home, StringComparison.Ordinal);
            Assert.Contains($"OnClick=\"() => ShowView(ShellView.{view})\"", home, StringComparison.Ordinal);
        }

        Assert.DoesNotContain("PaneToggleDisabled", home, StringComparison.Ordinal);
        Assert.DoesNotContain("args.CtrlKey || args.MetaKey) && _globalPanes", home, StringComparison.Ordinal);
        Assert.Contains("_globalPanes.Toggle(pane);", home, StringComparison.Ordinal);

        Assert.DoesNotContain("Show inbox", home, StringComparison.Ordinal);
        Assert.DoesNotContain("Hide inbox", home, StringComparison.Ordinal);
        Assert.DoesNotContain("Show knowledge", home, StringComparison.Ordinal);
        Assert.DoesNotContain("Hide knowledge", home, StringComparison.Ordinal);
    }

    /// <summary>
    /// Routing turns an Inbox item into Tasks entries, and the shell shows them on the
    /// task list on the reader's behalf. It changes the view and leaves the Inbox
    /// open: the queue the item was routed from stays beside the list.
    /// </summary>
    [Fact]
    public void Opening_an_inbox_item_never_closes_the_inbox()
    {
        var home = NormalizeLineEndings(File.ReadAllText(FindHomeRazor()));

        Assert.Contains("if (EffectiveView is not (ShellView.Tasks or ShellView.Board or ShellView.Calendar)) SetView(ShellView.Tasks);", home, StringComparison.Ordinal);
        Assert.DoesNotContain("_globalPanes.TryClose(GlobalPane.Inbox)", home, StringComparison.Ordinal);
    }

    /// <summary>
    /// Each pane used to carry a pin that held it through a switch, and then a Ctrl
    /// modifier that opened it beside the others. Neither is needed now that the
    /// side panes are plain toggles beside the view: the pins stay gone from the
    /// markup, the stylesheet and the persisted layout, and so does the modifier.
    /// </summary>
    [Fact]
    public void A_side_pane_needs_neither_a_pin_nor_a_modifier()
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

        Assert.DoesNotContain("Ctrl+click to open it beside", home, StringComparison.Ordinal);
        Assert.DoesNotContain("TryOpenBeside", home, StringComparison.Ordinal);
        Assert.Contains("_globalPanes.TryOpen(pane);", home, StringComparison.Ordinal);
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

    /// <summary>
    /// The split opens for the Devbook, the one pane in the side stack, whichever
    /// view it sits beside. The "side panes only" stack that filled the row when the
    /// task list was closed is gone with the Tasks pane toggle.
    /// </summary>
    [Fact]
    public void Side_layout_opens_split_whenever_the_devbook_is_beside_the_view()
    {
        var home = NormalizeLineEndings(File.ReadAllText(FindHomeRazor()));
        var css = NormalizeLineEndings(File.ReadAllText(FindAppCss()));

        Assert.Contains("@(DevbookPaneVisible ? \"devbook-layout--side-open\" : \"devbook-layout--side-closed\")", home, StringComparison.Ordinal);
        Assert.DoesNotContain("side-pane-stack--full", home, StringComparison.Ordinal);
        Assert.DoesNotContain("side-pane-stack--full", css, StringComparison.Ordinal);
        Assert.DoesNotContain("RightSidePaneVisible", home, StringComparison.Ordinal);
        Assert.DoesNotContain("TasksPaneVisible", home, StringComparison.Ordinal);
        Assert.DoesNotContain("side-pane-stack--right-docked", home, StringComparison.Ordinal);
    }

    /// <summary>
    /// The takeovers are not panes. Each is the page's single <c>main</c> landmark
    /// while it is open, which is only true as long as the branches stay mutually
    /// exclusive in the markup; the workspace is the last branch and holds the main
    /// view — the task list or the roadmap — inside its own landmark.
    /// </summary>
    [Fact]
    public void Only_one_surface_renders_and_it_owns_the_main_landmark()
    {
        var home = NormalizeLineEndings(File.ReadAllText(FindHomeRazor()));

        Assert.Contains("@if (ToolsVisible)", home, StringComparison.Ordinal);
        Assert.Contains("else if (DashboardVisible)", home, StringComparison.Ordinal);
        Assert.Contains("else if (SessionsVisible)", home, StringComparison.Ordinal);
        Assert.Contains("else if (PullRequestsVisible)", home, StringComparison.Ordinal);
        Assert.Contains("data-testid=\"tools-surface\"", home, StringComparison.Ordinal);
        Assert.Contains("data-testid=\"dashboard-surface\"", home, StringComparison.Ordinal);
        Assert.Contains("data-testid=\"sessions-surface\"", home, StringComparison.Ordinal);
        Assert.Contains("data-testid=\"pull-requests-surface\"", home, StringComparison.Ordinal);
        Assert.Contains("data-testid=\"workspace\"", home, StringComparison.Ordinal);

        // One landmark per branch, and the branches are exclusive, so the page has
        // exactly one. The roadmap gave up its own when it became a view.
        Assert.Equal(5, CountOccurrences(home, "<main class="));

        // The roadmap view sits inside the pane row, in the main column.
        var workspace = home.IndexOf("<main class=\"workspace\"", StringComparison.Ordinal);
        Assert.True(home.IndexOf("@if (RoadmapViewVisible)", StringComparison.Ordinal) > workspace);
        Assert.Contains("<div class=\"roadmap-view\" id=\"roadmap-view\" data-testid=\"roadmap-view\">", home, StringComparison.Ordinal);

        Assert.Contains("<div class=\"devbook-layout ", home, StringComparison.Ordinal);
        Assert.DoesNotContain("<main class=\"devbook-layout", home, StringComparison.Ordinal);
        Assert.Contains("data-testid=\"devbook-layout\"", home, StringComparison.Ordinal);
    }

    /// <summary>
    /// A takeover is a context change, so focus moves onto it and Escape brings the
    /// reader back. It is deliberately not a Modal: the header behind it is not
    /// inert, because that is where the control closing the surface lives. The
    /// Roadmap view has no Escape of its own here.
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
        Assert.Equal(4, CountOccurrences(home, "data-escape-closes"));

        // A view option or a side-pane toggle pressed during a takeover closes it the
        // same way.
        Assert.Contains("if (!WorkspaceVisible) CloseSurface();", home, StringComparison.Ordinal);

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
    public void Inbox_renders_before_the_main_view_when_it_is_open()
    {
        var home = NormalizeLineEndings(File.ReadAllText(FindHomeRazor()));

        Assert.Contains("devbook-layout--inbox-before-backlog", home, StringComparison.Ordinal);
        Assert.DoesNotContain("InboxBeforeTasksVisible", home, StringComparison.Ordinal);

        var layout = home.IndexOf("data-testid=\"devbook-layout\"", StringComparison.Ordinal);
        var inbox = home.IndexOf("@InboxPaneContent", layout, StringComparison.Ordinal);
        var roadmap = home.IndexOf("@if (RoadmapViewVisible)", layout, StringComparison.Ordinal);
        var tasks = home.IndexOf("<TasksPane OnOpenSession=\"OpenSessionAsync\" OnOpenDevbookReference=\"OpenDevbookReferenceAsync\" Layout=\"@TasksPaneLayout\" BoardGrouping=\"@BoardGrouping\" BoardGroupingChanged=\"OnBoardGroupingChanged\" LinkedWork=\"LinkedWorkFor\" />", StringComparison.Ordinal);
        var devbook = home.IndexOf("<DevbookPane ", layout, StringComparison.Ordinal);

        Assert.True(inbox > layout);
        Assert.True(roadmap > inbox && tasks > inbox);
        Assert.True(devbook > tasks && devbook > roadmap);

        // One place only: the Inbox is never in the side stack any more.
        Assert.Equal(1, CountOccurrences(home, "@InboxPaneContent"));
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

        // The band is as short as its contents allow: 1.5rem of control, and block
        // padding at exactly the reach of a focus ring. Shorter padding would clip
        // the ring against .app-shell's overflow — .devbook/design/accessibility.md#focus-visibility
        // draws focus as an outline offset 2px, so 4px of ink sits outside the
        // control.
        Assert.Contains("padding: var(--spacing-xs) var(--spacing-lg);", block, StringComparison.Ordinal);

        // The utilities give up .header-util's 2.25rem inside the band — the trade
        // that takes the band from about 45px to about 32px — and keep its type.
        var utility = css[css.IndexOf(".app-footer .header-util {", StringComparison.Ordinal)..];
        utility = utility[..utility.IndexOf('}')];
        Assert.Contains("min-height: 1.5rem;", utility, StringComparison.Ordinal);
        Assert.Contains("padding-block: 0;", utility, StringComparison.Ordinal);
        Assert.DoesNotContain("font-size", utility, StringComparison.Ordinal);
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
        // .devbook/design/content-editing.md#raw-markdown-escape-hatch asks for. There is no
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
        Assert.Contains("<CaptureSourcesPanel OnImported=", home, StringComparison.Ordinal);
        Assert.DoesNotContain("<InboxPane Items=", home, StringComparison.Ordinal);
        Assert.DoesNotContain("OnAdd=", home, StringComparison.Ordinal);
        Assert.Contains("<TasksPane OnOpenSession=\"OpenSessionAsync\" OnOpenDevbookReference=\"OpenDevbookReferenceAsync\" Layout=\"@TasksPaneLayout\" BoardGrouping=\"@BoardGrouping\" BoardGroupingChanged=\"OnBoardGroupingChanged\" LinkedWork=\"LinkedWorkFor\" />", home, StringComparison.Ordinal);
        Assert.Contains("<DevbookPane RepositoryAlias=", home, StringComparison.Ordinal);

        // The roadmap and the dashboard are composed on the same terms. Their content
        // belongs to Roadmap and the Dashboard; the shell only decides where it goes.
        // The roadmap takes no parameters at all: the shell renders it as a surface
        // or it does not.
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
    /// <c>overflow: auto</c>. The Tasks pane turns that off on both and scrolls one
    /// box deeper on each side. On the list side the scroller is
    /// <c>.backlog-list__scroll</c>, everything under the filter bar, so the bar
    /// stays put and the scrollbar starts below it. <c>.backlog-list</c> holds the
    /// bar and the scroller and must not be a scroller itself: a scroll container
    /// that never scrolls is the one thing a sticky child cannot survive, because it
    /// measures against its nearest scroll container.
    /// </para>
    /// <para>
    /// The pane half scrolls one box deeper too: the panel fills the height it is
    /// given so the body inside it can, and a box that both stretched its child and
    /// scrolled it is a box that could do neither.
    /// </para>
    /// </summary>
    [Fact]
    public void Each_half_of_the_backlog_split_scrolls_on_its_own()
    {
        var css = NormalizeLineEndings(File.ReadAllText(FindAppCss()));
        var components = NormalizeLineEndings(File.ReadAllText(FindComponentsCss()));

        Assert.Contains(".backlog-list {", css, StringComparison.Ordinal);
        Assert.Contains(".entry-detail {", css, StringComparison.Ordinal);

        foreach (var half in new[] { ".split-pane__start {", ".split-pane__end {" })
        {
            var halfRule = Block(components, half);
            Assert.Contains("overflow: auto;", halfRule, StringComparison.Ordinal);
            Assert.Contains("min-height: 0;", halfRule, StringComparison.Ordinal);
        }

        // The list half is a column that hands its height down and scrolls nothing.
        var listHalf = Block(css, ".backlog-split > .split-pane__start {");
        Assert.Contains("display: flex;", listHalf, StringComparison.Ordinal);
        Assert.Contains("flex-direction: column;", listHalf, StringComparison.Ordinal);
        Assert.Contains("min-height: 0;", listHalf, StringComparison.Ordinal);
        Assert.Contains("overflow: hidden;", listHalf, StringComparison.Ordinal);

        var list = Block(css, ".backlog-list {");
        Assert.DoesNotContain("overflow", list, StringComparison.Ordinal);
        Assert.Contains("min-height: 0;", list, StringComparison.Ordinal);
        Assert.Contains("flex: 1 1 auto;", list, StringComparison.Ordinal);
        Assert.Contains("container-type: inline-size;", list, StringComparison.Ordinal);

        // The filter bar keeps its own height; the scroller under it takes the rest.
        Assert.Contains("flex: none;", Block(css, ".filter-bar {"), StringComparison.Ordinal);

        var scroller = Block(css, ".backlog-list__scroll {");
        Assert.Contains("flex: 1 1 auto;", scroller, StringComparison.Ordinal);
        Assert.Contains("min-height: 0;", scroller, StringComparison.Ordinal);
        Assert.Contains("overflow-y: auto;", scroller, StringComparison.Ordinal);

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
    /// The list scrolls down and never sideways.
    /// <para>
    /// A horizontal bar under the rows is always a row that refused to shrink, and
    /// reading the end of one line by dragging every row is not a gesture this pane
    /// asks anybody for. So the list's scroller turns that axis off, and the shared
    /// halves keep both axes for the hosts that mean it.
    /// </para>
    /// <para>
    /// The clamps below are what keep it from being a cut: a row that fits is never
    /// clipped, and the three boxes named here are the ones that were free to grow
    /// past the track they sit in. <c>.task-item</c> is a grid item, so
    /// <c>.task-list</c>'s <c>minmax(0, 1fr)</c> caps its track and not the row in
    /// it; <c>.task-item__detail</c> is a <c>nowrap</c> box, so a pasted link or a
    /// long branch name held the whole line open from the innermost box out.
    /// </para>
    /// </summary>
    [Fact]
    public void The_list_half_never_scrolls_sideways()
    {
        var css = NormalizeLineEndings(File.ReadAllText(FindAppCss()));
        var components = NormalizeLineEndings(File.ReadAllText(FindComponentsCss()));

        var scroller = Block(css, ".backlog-list__scroll {");
        Assert.Contains("overflow-x: hidden;", scroller, StringComparison.Ordinal);
        // The vertical axis is the scroller's whole job, so it must not be turned
        // off with it — a shorthand here would take both.
        Assert.Contains("overflow-y: auto;", scroller, StringComparison.Ordinal);
        Assert.DoesNotContain("overflow:", scroller, StringComparison.Ordinal);

        var row = Block(components, ".task-item {");
        Assert.Contains("min-width: 0;", row, StringComparison.Ordinal);

        var meta = Block(components, ".task-item__meta {\n    display: flex;");
        Assert.Contains("min-width: 0;", meta, StringComparison.Ordinal);

        var detail = Block(components, ".task-item__detail {");
        Assert.Contains("min-width: 0;", detail, StringComparison.Ordinal);
        Assert.Contains("overflow: hidden;", detail, StringComparison.Ordinal);
        // Ellipsis has no effect on a flex container's items, so a declaration here
        // would promise the reader a mark they never get.
        Assert.DoesNotContain("text-overflow", detail, StringComparison.Ordinal);

        static string Block(string sheet, string selector)
        {
            var start = sheet.IndexOf(selector, StringComparison.Ordinal);
            Assert.True(start >= 0, $"{selector} should be declared.");
            return sheet[start..sheet.IndexOf('}', start)];
        }
    }

    /// <summary>
    /// The two controls that stick to the list's scroller, and the edge each holds.
    /// The bulk bar takes the top so the count and the way out of a selection stay
    /// in reach; the add-entry row takes the bottom so a column longer than the
    /// window never hides the one control that adds to it. Both are asserted together
    /// with the rules above because both are dead the moment the wrong box becomes
    /// a scroller. And the filter bar is outside the scroller, so it never scrolls.
    /// </summary>
    [Fact]
    public void The_bulk_bar_and_the_add_entry_row_stick_to_the_list_scroller()
    {
        var css = NormalizeLineEndings(File.ReadAllText(FindAppCss()));
        var tasks = NormalizeLineEndings(File.ReadAllText(FindTasksPane()));

        // Markup order: the filter bar, then the scroller, then the bulk bar and
        // the rows inside it.
        var filterBar = tasks.IndexOf("<div class=\"filter-bar\">", StringComparison.Ordinal);
        var scroll = tasks.IndexOf("<div class=\"backlog-list__scroll\" data-testid=\"backlog-list-scroll\">", StringComparison.Ordinal);
        var bulkBar = tasks.IndexOf("<SelectionBar", StringComparison.Ordinal);
        var rows = tasks.IndexOf("<TaskListView", StringComparison.Ordinal);
        Assert.True(filterBar >= 0 && scroll > filterBar, "The scroller follows the filter bar.");
        Assert.True(bulkBar > scroll && rows > scroll, "The bulk bar and the rows are inside the scroller.");
        Assert.True(
            tasks.IndexOf("<div class=\"backlog-list__scroll\"", filterBar, StringComparison.Ordinal)
            > tasks.IndexOf("TestId=\"open-work-summary\"", StringComparison.Ordinal),
            "The scroller opens after the filter bar's last control, not around it.");

        var scroller = Block(css, ".backlog-list__scroll {");
        Assert.Contains("display: flex;", scroller, StringComparison.Ordinal);
        Assert.Contains("flex-direction: column;", scroller, StringComparison.Ordinal);
        Assert.Contains("gap: var(--spacing-md);", scroller, StringComparison.Ordinal);
        // Positioned, or the rows' absolutely positioned pieces escape its clip and
        // make the document thousands of pixels taller than the window.
        Assert.Contains("position: relative;", scroller, StringComparison.Ordinal);

        var bar = Block(css, ".backlog-bulk-bar {");
        Assert.Contains("position: sticky;", bar, StringComparison.Ordinal);
        Assert.Contains("top: 0;", bar, StringComparison.Ordinal);
        // Above the row's edit pencil, which the library lifts to z-index 1.
        Assert.Contains("z-index: 2;", bar, StringComparison.Ordinal);

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
