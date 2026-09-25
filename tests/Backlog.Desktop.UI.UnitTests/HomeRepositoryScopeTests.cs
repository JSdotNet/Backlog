using Backlog.Infrastructure.AzureFoundry;
using Backlog.Modules.Capture.Abstractions.Services;
using Backlog.Modules.Capture.Extensions;
using Backlog.Infrastructure.Copilot;
using Backlog.Infrastructure.GitHub;
using AngleSharp.Dom;

using Backlog.Modules.Dashboard.UI;
using Backlog.Modules.Sessions.UI;
using Bunit;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;

namespace Backlog.Desktop.UI.UnitTests;

/// <summary>
/// The repository identity mark on the header's scope filter, and the header control
/// that decides whether the mark is drawn at all.
/// <para>
/// Classes rather than colours, because the shell says <em>which</em> repository a chip
/// is for and the stylesheet says which hue that is. What matters here is that the
/// shell reads the same answer the entry list and the roadmap read — which is the whole
/// reason the choice lives in Settings rather than on any one of them.
/// </para>
/// <para>
/// Every test that expects a mark turns the visualization on first, because off is what
/// a fresh workspace gets. That is not scaffolding around an awkward default — it is the
/// feature: the marks are an opt-in layer, so a test that wants one has to say so.
/// </para>
/// </summary>
public sealed class HomeRepositoryScopeTests
{
    /// <summary>
    /// The identity region opens on the scope chips, because there is nothing in
    /// front of them any more.
    /// <para>
    /// It used to open with a "Backlog" wordmark. The window border carries the name,
    /// so the wordmark was the app saying twice which app you were in and spending
    /// the width of the word on it — and it left the shell owning an <c>h1</c> that
    /// was chrome rather than the page's title, which is a heading every other screen
    /// supplies for itself.
    /// </para>
    /// </summary>
    [Fact]
    public void The_header_carries_no_wordmark()
    {
        using var harness = CreateHarness();
        var component = Render(harness);

        Assert.Empty(component.FindAll(".app-header h1"));

        // And the region is still there, still holding the scope: the wordmark went,
        // not the region around it.
        Assert.NotNull(component.Find(".app-header__identity [aria-label='Repository scope']"));
    }

    /// <summary>
    /// The scope is a fused group, because a fused group is what this header draws
    /// where several options can be on at once — and with Ctrl held, several
    /// repositories can. It is the shared ButtonGroup rather than a div wearing the
    /// group role, so the role and the label are the component's, and it wears the
    /// same <c>header-group</c> shape the sections strip does. The chips inside keep
    /// their chip classes: the active fill, the identity edge and the anchor mark all
    /// key on those, and they are exactly what a fused chip must not lose.
    /// </summary>
    [Fact]
    public void The_scope_is_a_fused_group_of_chips()
    {
        using var harness = CreateHarness();
        var component = Render(harness);

        var group = component.Find(".app-header__identity [aria-label='Repository scope']");

        Assert.Equal("group", group.GetAttribute("role"));
        Assert.Contains("btn-group", group.ClassList);
        Assert.Contains("header-group", group.ClassList);
        Assert.DoesNotContain("header-group--loose", group.ClassList);

        var chips = component.WaitForElements("[data-testid='repository-filter-option']");
        Assert.All(chips, chip =>
        {
            // A direct child, because the fused hairlines key on `> .chip + .chip`.
            Assert.Equal("Repository scope", chip.ParentElement?.GetAttribute("aria-label"));
            Assert.Contains("chip", chip.ClassList);
            Assert.Contains("chip--scope", chip.ClassList);
        });
    }

    [Fact]
    public void Each_scope_chip_carries_its_repositorys_mark()
    {
        using var harness = CreateHarness(ShowingColours);
        var component = Render(harness);

        var chips = component.WaitForElements("[data-testid='repository-filter-option']");

        // Position, because neither repository has been given a colour of its own.
        Assert.Contains("repo-mark--1", chips[0].ClassName);
        Assert.Contains("repo-mark--2", chips[1].ClassName);
    }

    [Fact]
    public void A_chosen_colour_reaches_the_chip()
    {
        using var harness = CreateHarness(settings =>
        {
            ShowingColours(settings);
            Assert.Null(settings.SetRepositoryColour("backlog", 5));
        });
        var component = Render(harness);

        var chips = component.WaitForElements("[data-testid='repository-filter-option']");

        Assert.Contains("repo-mark--5", chips[0].ClassName);
    }

    [Fact]
    public void The_mark_is_never_the_only_thing_saying_which_repository()
    {
        using var harness = CreateHarness();
        var component = Render(harness);

        var chips = component.WaitForElements("[data-testid='repository-filter-option']");

        // .devbook/design/color-scheme.md#band-identity-tokens requires it: the alias is written
        // on the chip, so a reader who never sees a hue loses nothing.
        Assert.Equal("backlog", chips[0].TextContent.Trim());
        Assert.Equal("docs", chips[1].TextContent.Trim());
    }

    [Fact]
    public void Selecting_a_chip_keeps_its_mark()
    {
        using var harness = CreateHarness(ShowingColours);
        var component = Render(harness);

        var chips = component.WaitForElements("[data-testid='repository-filter-option']");
        chips[0].Click();

        // The mark is identity and the fill is selection. They are two different facts
        // and the chip has to be able to carry both at once, which is why the identity
        // rule is an edge rather than a surface.
        component.WaitForAssertion(() =>
        {
            var selected = component.FindAll("[data-testid='repository-filter-option']")[0];
            Assert.Contains("chip--active", selected.ClassName);
            Assert.Contains("repo-mark--1", selected.ClassName);
        });
    }

    // --- More than one repository at once ----------------------------------------
    //
    // A plain press is the single select the strip always had. With Ctrl (Cmd on a
    // Mac) held it adds or removes one, and the first one taken is the anchor — the
    // repository the Devbook pane reads, since the pane can read one and not
    // several. The anchor is marked only once there is a second chip for it to be
    // distinct from; with one chip pressed the markup is what it was before.

    [Fact]
    public void A_plain_press_scopes_to_one_repository_and_a_second_plain_press_replaces_it()
    {
        using var harness = CreateHarness();
        var component = Render(harness);
        var state = harness.Context.Services.GetRequiredService<TasksDesktopState>();

        Chips(component)[0].Click();
        Assert.Equal(["backlog"], state.SelectedRepositoryAliases);

        Chips(component)[1].Click();
        Assert.Equal(["docs"], state.SelectedRepositoryAliases);

        // And pressing the one that is alone in the scope clears it, as it always did.
        Chips(component)[1].Click();
        Assert.Empty(state.SelectedRepositoryAliases);
    }

    [Fact]
    public void A_modified_press_adds_a_repository_beside_the_first()
    {
        using var harness = CreateHarness();
        var component = Render(harness);
        var state = harness.Context.Services.GetRequiredService<TasksDesktopState>();

        Chips(component)[0].Click();
        Chips(component)[1].Click(new MouseEventArgs { CtrlKey = true });

        Assert.Equal(["backlog", "docs"], state.SelectedRepositoryAliases);
        component.WaitForAssertion(() =>
            Assert.All(Chips(component), chip => Assert.Equal("true", chip.GetAttribute("aria-pressed"))));
    }

    [Fact]
    public void Cmd_counts_as_the_modifier_too()
    {
        using var harness = CreateHarness();
        var component = Render(harness);
        var state = harness.Context.Services.GetRequiredService<TasksDesktopState>();

        Chips(component)[0].Click();
        Chips(component)[1].Click(new MouseEventArgs { MetaKey = true });

        Assert.Equal(["backlog", "docs"], state.SelectedRepositoryAliases);
    }

    [Fact]
    public void A_modified_press_on_a_scoped_chip_takes_it_back_out()
    {
        using var harness = CreateHarness();
        var component = Render(harness);
        var state = harness.Context.Services.GetRequiredService<TasksDesktopState>();

        Chips(component)[0].Click();
        Chips(component)[1].Click(new MouseEventArgs { CtrlKey = true });
        Chips(component)[1].Click(new MouseEventArgs { CtrlKey = true });

        Assert.Equal(["backlog"], state.SelectedRepositoryAliases);
    }

    [Fact]
    public void The_knowledge_pane_follows_the_anchor_not_the_latest_press()
    {
        using var harness = CreateHarness();
        var component = Render(harness);

        Chips(component)[1].Click();

        // With Ctrl, so opening Devbook puts it beside the list rather than in its
        // place — a plain pane press is a switch, and the list has to stay for a
        // second repository to have anywhere to be.
        component.WaitForElement("[data-testid='devbook-pane-option']").Click(new MouseEventArgs { CtrlKey = true });
        component.WaitForAssertion(() =>
        {
            Assert.NotEmpty(component.FindAll("[data-testid='backlog-pane']"));
            Assert.Equal("docs", component.FindComponent<DevbookPane>().Instance.RepositoryAlias);
        });

        Chips(component)[0].Click(new MouseEventArgs { CtrlKey = true });

        // Two repositories in the list, and the pane still reading the first one taken:
        // adding a repository to the backlog scope must not move the knowledge the reader
        // was already reading beside it.
        component.WaitForAssertion(() =>
            Assert.Equal("docs", component.FindComponent<DevbookPane>().Instance.RepositoryAlias));
    }

    /// <summary>
    /// One repository scope for the whole screen. The dashboard used to carry a
    /// second select for the same question; it reads the header's scope now — the
    /// whole of it, in the order taken — and a cleared scope reads as every
    /// repository.
    /// </summary>
    [Fact]
    public void The_dashboard_follows_the_whole_header_scope()
    {
        using var harness = CreateHarness();
        var component = Render(harness);

        Chips(component)[1].Click();
        Chips(component)[0].Click(new MouseEventArgs { CtrlKey = true });

        component.WaitForElement("[data-testid='dashboard-toggle-button']").Click();

        component.WaitForAssertion(() =>
        {
            Assert.NotEmpty(component.FindAll("[data-testid='dashboard-panel']"));
            Assert.Equal(["docs", "backlog"], component.FindComponent<DashboardPane>().Instance.RepositoryAliases);
        });

        // The chips stay live on the dashboard, and a press there reaches it.
        Chips(component)[0].Click();
        component.WaitForAssertion(() =>
            Assert.Equal(["backlog"], component.FindComponent<DashboardPane>().Instance.RepositoryAliases));

        // Cleared: an empty scope, which the pane reads as all repositories.
        Chips(component)[0].Click();
        component.WaitForAssertion(() =>
            Assert.Empty(component.FindComponent<DashboardPane>().Instance.RepositoryAliases));
    }

    /// <summary>
    /// The dashboard shows several repositories at once, so the modifier counts on
    /// it the way it does on the task list — and the tooltip offers it.
    /// </summary>
    [Fact]
    public void On_the_dashboard_a_modified_press_adds_a_repository()
    {
        using var harness = CreateHarness();
        var component = Render(harness);
        var state = harness.Context.Services.GetRequiredService<TasksDesktopState>();

        Chips(component)[0].Click();
        component.WaitForElement("[data-testid='dashboard-toggle-button']").Click();
        component.WaitForAssertion(() => Assert.NotEmpty(component.FindAll("[data-testid='dashboard-panel']")));

        Assert.Contains("Ctrl+click", Chips(component)[1].GetAttribute("title"), StringComparison.Ordinal);

        Chips(component)[1].Click(new MouseEventArgs { CtrlKey = true });

        Assert.Equal(["backlog", "docs"], state.SelectedRepositoryAliases);
    }

    /// <summary>
    /// Tools shows no repository at all, so leaving the dashboard for it narrows the
    /// scope to its anchor — the same drop the scope makes when the task list closes
    /// — and a chip there is a single select.
    /// </summary>
    [Fact]
    public void Leaving_for_tools_narrows_the_scope_to_its_anchor()
    {
        using var harness = CreateHarness();
        var component = Render(harness);
        var state = harness.Context.Services.GetRequiredService<TasksDesktopState>();

        Chips(component)[1].Click();
        Chips(component)[0].Click(new MouseEventArgs { CtrlKey = true });
        Assert.Equal(["docs", "backlog"], state.SelectedRepositoryAliases);

        component.WaitForElement("[data-testid='tools-toggle-button']").Click();
        component.WaitForAssertion(() => Assert.NotEmpty(component.FindAll("[data-testid='tools-surface']")));

        Assert.Equal(["docs"], state.SelectedRepositoryAliases);
        Assert.DoesNotContain("Ctrl+click", Chips(component)[0].GetAttribute("title"), StringComparison.Ordinal);
    }

    /// <summary>
    /// The sessions tab takes the same scope, and the shell hands it the lookup that
    /// turns a session's recorded repository — an <c>owner/name</c>, or an alias
    /// that arrived on the wire — into the alias the scope is written in.
    /// </summary>
    [Fact]
    public void The_sessions_tab_is_handed_the_scope_and_the_alias_lookup()
    {
        using var harness = CreateHarness();
        var component = Render(harness);

        Chips(component)[0].Click();

        component.WaitForElement("[data-testid='dashboard-toggle-button']").Click();
        component.WaitForElement("[data-testid='dashboard-sessions-tab']").Click();
        component.WaitForAssertion(() => Assert.NotEmpty(component.FindAll("[data-testid='sessions-panel']")));

        var pane = component.FindComponent<SessionsPane>().Instance;
        Assert.Equal(["backlog"], pane.RepositoryScope);

        var aliasFor = pane.RepositoryAlias;
        Assert.NotNull(aliasFor);
        Assert.Equal("backlog", aliasFor("JSdotNet/Backlog"));
        Assert.Equal("backlog", aliasFor("backlog"));
        Assert.Null(aliasFor("Someone/Else"));
        Assert.Null(aliasFor(null));

        Chips(component)[1].Click(new MouseEventArgs { CtrlKey = true });
        component.WaitForAssertion(() =>
            Assert.Equal(["backlog", "docs"], component.FindComponent<SessionsPane>().Instance.RepositoryScope));
    }

    [Fact]
    public void The_anchor_is_marked_only_once_there_is_a_second_chip_to_tell_it_from()
    {
        using var harness = CreateHarness();
        var component = Render(harness);

        Chips(component)[1].Click();

        // One chip pressed: nothing to be the anchor of, so no mark — the markup
        // stays what it was before the strip could hold two.
        component.WaitForAssertion(() =>
        {
            var chips = Chips(component);
            Assert.All(chips, chip => Assert.Null(chip.GetAttribute("aria-current")));
            Assert.All(chips, chip => Assert.DoesNotContain("chip--anchor", chip.ClassName));
        });

        Chips(component)[0].Click(new MouseEventArgs { CtrlKey = true });

        component.WaitForAssertion(() =>
        {
            var chips = Chips(component);
            Assert.Equal("true", chips[1].GetAttribute("aria-current"));
            Assert.Contains("chip--anchor", chips[1].ClassName);
            Assert.Null(chips[0].GetAttribute("aria-current"));
            Assert.DoesNotContain("chip--anchor", chips[0].ClassName);
        });
    }

    [Fact]
    public void Removing_the_anchor_hands_the_mark_to_the_next_repository()
    {
        using var harness = CreateHarness();
        var component = Render(harness);
        var state = harness.Context.Services.GetRequiredService<TasksDesktopState>();

        Chips(component)[1].Click();
        Chips(component)[0].Click(new MouseEventArgs { CtrlKey = true });
        Chips(component)[1].Click(new MouseEventArgs { CtrlKey = true });

        Assert.Equal(["backlog"], state.SelectedRepositoryAliases);
        Assert.Equal("backlog", state.AnchorRepositoryAlias);
    }

    [Fact]
    public void The_chip_says_how_to_add_one()
    {
        using var harness = CreateHarness();
        var component = Render(harness);

        var chips = Chips(component);

        // The modifier is the one thing about the strip nothing on screen shows, so
        // the chip's own tooltip is where it is written down.
        Assert.Contains("Ctrl+click", chips[0].GetAttribute("title"));
    }

    // --- Several only while the list is on screen ------------------------------------
    //
    // More than one repository is a list affordance: the backlog list is the only
    // pane that can show several. So the scope holds several only while Tasks is on
    // screen — going to Devbook, which as a plain switch takes the list's
    // place, narrows the scope to the anchor, and without the list a modified press
    // is an ordinary press.

    [Fact]
    public void Going_to_knowledge_in_place_of_the_list_narrows_the_scope_to_the_anchor()
    {
        using var harness = CreateHarness();
        var component = Render(harness);
        var state = harness.Context.Services.GetRequiredService<TasksDesktopState>();

        Chips(component)[1].Click();
        Chips(component)[0].Click(new MouseEventArgs { CtrlKey = true });
        Assert.Equal(["docs", "backlog"], state.SelectedRepositoryAliases);

        GoToDevbook(component);

        // The anchor, not the latest: docs was taken first, and it is what the
        // knowledge pane would have been reading beside the list.
        component.WaitForAssertion(() => Assert.Equal(["docs"], state.SelectedRepositoryAliases));
        Assert.Equal("docs", component.FindComponent<DevbookPane>().Instance.RepositoryAlias);
    }

    [Fact]
    public void Without_the_list_a_modified_press_is_an_ordinary_press()
    {
        using var harness = CreateHarness();
        var component = Render(harness);
        var state = harness.Context.Services.GetRequiredService<TasksDesktopState>();

        Chips(component)[0].Click();
        GoToDevbook(component);

        Chips(component)[1].Click(new MouseEventArgs { CtrlKey = true });

        // Waited for rather than read straight off, the way every other press in this
        // file is: going to Devbook leaves the pane's own load draining through the
        // renderer, and a press dispatched while it does lands a batch later. The
        // assertion is the same one; what changes is that it stops racing the drain.
        component.WaitForAssertion(() => Assert.Equal(["docs"], state.SelectedRepositoryAliases));
        Assert.DoesNotContain("Ctrl+click", Chips(component)[0].GetAttribute("title"));
    }

    /// <summary>Presses the Devbook option, which — a plain press being a switch —
    /// puts the Devbook pane where the list was.</summary>
    private static void GoToDevbook(IRenderedComponent<Home> component)
    {
        component.WaitForElement("[data-testid='devbook-pane-option']").Click();
        component.WaitForAssertion(() =>
        {
            Assert.NotEmpty(component.FindAll("[data-testid='devbook-stack']"));
            Assert.Empty(component.FindAll("[data-testid='backlog-pane']"));
        });
    }

    private static IReadOnlyList<IElement> Chips(IRenderedComponent<Home> component) =>
        component.WaitForElements("[data-testid='repository-filter-option']");

    // --- The repository colour visualization ----------------------------------
    //
    // The switch is here, on the main page, beside the chips it is most visibly about.
    // What it flips is not a shell concern though — it is the settings store's answer to
    // "which hue", which is why turning it off has to empty every surface at once rather
    // than only this one.
    //
    // Asserted on the switch inside the control rather than on the control itself: the
    // shared Toggle puts the test id on the wrapper that holds the track and the label
    // together, because the label is part of the control and a test id on the button
    // alone would name only half of it.

    [Fact]
    public void The_header_offers_the_colour_visualization_switch_beside_the_scope_chips()
    {
        using var harness = CreateHarness();
        var component = Render(harness);

        var control = component.WaitForElement("[data-testid='repository-colours-toggle']");
        var switchElement = ColourSwitch(component);

        // A switch and not a disclosure or a selection: nothing opens, nothing is picked
        // out of a set, a fact about the whole workspace changes. So role=switch with
        // aria-checked, and no aria-expanded to suggest otherwise.
        Assert.Equal("switch", switchElement.GetAttribute("role"));
        Assert.Equal("false", switchElement.GetAttribute("aria-checked"));
        Assert.Null(switchElement.GetAttribute("aria-expanded"));

        // Named by the text beside it rather than by an aria-label over the top of it,
        // so the accessible name and the visible one cannot drift apart.
        Assert.Null(switchElement.GetAttribute("aria-label"));
        var labelId = switchElement.GetAttribute("aria-labelledby");
        Assert.False(string.IsNullOrWhiteSpace(labelId));
        Assert.Equal("Colors", component.Find("#" + labelId).TextContent.Trim());
        Assert.Contains("Colors", control.TextContent);
    }

    [Fact]
    public void The_switch_is_a_real_button_so_the_keyboard_already_works()
    {
        using var harness = CreateHarness();
        var component = Render(harness);

        var switchElement = ColourSwitch(component);

        // Enter and Space are the browser's, not ours — which is the whole reason the
        // library draws a switch as a button rather than as a styled div. Asserting the
        // element is the honest version of that test; simulating the two keys would be a
        // test of the renderer rather than of anything this screen decides.
        Assert.Equal("BUTTON", switchElement.TagName);
        Assert.Equal("button", switchElement.GetAttribute("type"));
        Assert.False(switchElement.HasAttribute("disabled"));
    }

    [Fact]
    public void No_chip_carries_a_mark_until_the_visualization_is_turned_on()
    {
        using var harness = CreateHarness();
        var component = Render(harness);

        var chips = component.WaitForElements("[data-testid='repository-filter-option']");

        // Off is the first-run answer, and off means the chip's ordinary presentation —
        // the one a repository with nothing configured already had — rather than a
        // "colours off" style invented for the occasion.
        Assert.All(chips, chip => Assert.DoesNotContain("repo-mark", chip.ClassName));
    }

    [Fact]
    public void Turning_the_visualization_on_marks_every_chip()
    {
        using var harness = CreateHarness();
        var component = Render(harness);

        ColourSwitch(component).Click();

        component.WaitForAssertion(() =>
        {
            var chips = component.FindAll("[data-testid='repository-filter-option']");
            Assert.Contains("repo-mark--1", chips[0].ClassName);
            Assert.Contains("repo-mark--2", chips[1].ClassName);
            Assert.Equal("true", ColourSwitch(component).GetAttribute("aria-checked"));
        });
    }

    [Fact]
    public void The_toggle_records_the_choice_where_every_surface_reads_it()
    {
        using var harness = CreateHarness();
        var component = Render(harness);

        ColourSwitch(component).Click();

        // Not a field on the shell. The point of pressing it here is that the entry list,
        // the sessions pane and the roadmap band read the same answer — and that the
        // answer is on disk before the next launch asks for it.
        component.WaitForAssertion(() => Assert.True(harness.GitHubSettings.Current.ShowRepositoryColours));
        Assert.True(new GitHubSettingsStore(harness.GitHubSettings.SettingsPath).Current.ShowRepositoryColours);
    }

    [Fact]
    public void Turning_the_visualization_off_again_takes_the_marks_back_off()
    {
        using var harness = CreateHarness(ShowingColours);
        var component = Render(harness);

        Assert.Equal("true", ColourSwitch(component).GetAttribute("aria-checked"));

        ColourSwitch(component).Click();

        component.WaitForAssertion(() => Assert.All(
            component.FindAll("[data-testid='repository-filter-option']"),
            chip => Assert.DoesNotContain("repo-mark", chip.ClassName)));

        Assert.Equal("false", ColourSwitch(component).GetAttribute("aria-checked"));

        // Hiding the layer does not unmake the choice underneath it — Settings' swatches
        // still have something to show.
        Assert.Equal(1, harness.GitHubSettings.Current.ColourFor("backlog"));
    }

    [Fact]
    public void The_sessions_pane_is_handed_the_gated_answer_too()
    {
        using var harness = CreateHarness();
        var component = Render(harness);

        // The list is the Dashboard's second tab: the segment, then the tab.
        component.WaitForElement("[data-testid='dashboard-toggle-button']").Click();
        component.WaitForElement("[data-testid='dashboard-sessions-tab']").Click();
        component.WaitForAssertion(() => Assert.NotEmpty(component.FindAll("[data-testid='sessions-panel']")));

        // The pane is handed a function rather than a dictionary, so the gate has to be
        // inside the function the shell passes down — the pane itself knows nothing about
        // a visualization and should not have to.
        var colourFor = component.FindComponent<SessionsPane>().Instance.RepositoryColour;
        Assert.NotNull(colourFor);
        Assert.Null(colourFor("JSdotNet/Backlog"));

        ColourSwitch(component).Click();

        component.WaitForAssertion(() => Assert.Equal(
            1,
            component.FindComponent<SessionsPane>().Instance.RepositoryColour!("JSdotNet/Backlog")));
    }

    /// <summary>The switch inside the header's colour control. Found through the
    /// control's test id rather than by its own, because that is where the shared Toggle
    /// puts one — see the section comment above.</summary>
    private static IElement ColourSwitch(IRenderedComponent<Home> component) =>
        component.WaitForElement("[data-testid='repository-colours-toggle'] [role='switch']");

    /// <summary>Turns the identity-hue visualization on, the way somebody using the
    /// header switch would have before this screen was opened.</summary>
    private static void ShowingColours(GitHubSettingsStore settings) =>
        Assert.Null(settings.SetShowRepositoryColours(true));

    private static IRenderedComponent<Home> Render(Harness harness)
    {
        harness.Context.JSInterop.Mode = JSRuntimeMode.Loose;
        return harness.Context.Render<Home>();
    }

    private static Harness CreateHarness(Action<GitHubSettingsStore>? configureRepositories = null)
    {
        var root = Path.Combine(Path.GetTempPath(), "backlog-repository-scope-tests", Guid.NewGuid().ToString("n"));
        var store = new WorkspaceSettingsStore(Path.Combine(root, "store"));
        var gitHubSettings = new GitHubSettingsStore(Path.Combine(root, "github", "github.json"));
        var featureSettings = new AppFeatureSettingsStore(AppFeatures.All, Path.Combine(root, "features", "features.json"));

        // The three surfaces under test are on; everything that would put extra
        // chrome or a network call in the way is off.
        _ = featureSettings.SetEnabled(RoadmapFeatures.Roadmap, true);
        _ = featureSettings.SetEnabled(DashboardFeatures.Dashboard, true);
        _ = featureSettings.SetEnabled(DevPcFeatures.SystemTools, true);
        _ = featureSettings.SetEnabled(SessionFeatures.Sessions, true);
        _ = featureSettings.SetEnabled(DevbookFeatures.DevbookSections, true);
        _ = featureSettings.SetEnabled(DevbookFeatures.RepositoryDevbook, true);
        _ = featureSettings.SetEnabled(AppFeatures.InboxPane, false);
        _ = featureSettings.SetEnabled(AppFeatures.AiAssistant, false);
        _ = featureSettings.SetEnabled(AppFeatures.FeedbackReporting, false);
        _ = featureSettings.SetEnabled(TasksFeatures.GitHubIntegration, false);

        var (repositories, errors) = GitHubSettings.ParseText(
            "backlog = JSdotNet/Backlog" + Environment.NewLine + "docs = JSdotNet/Docs");
        Assert.Empty(errors);

        Assert.Null(gitHubSettings.SetRepositories(
        [
            .. repositories.Select(repository => repository with
            {
                CloneDirectory = RepositoryRoot.Root.FullName,
                DevbookFolders = DevbookFolderSetting.Defaults()
            })
        ]));

        configureRepositories?.Invoke(gitHubSettings);

        var gitHub = new GitHubIntegration(gitHubSettings, new StubGitHubClient(), new StubProbe());
        var devbookFolderSource = new DevbookFolderSource(gitHubSettings, store);

        var context = new BunitContext();
        context.Services.AddSingleton(store);
        context.Services.AddSingleton<IAppFeatureSettings>(featureSettings);
        context.Services.AddSingleton(new ShellNavigationStore(Path.Combine(root, "shell", "shell-navigation.json")));
        context.Services.AddSingleton(gitHubSettings);
        context.Services.AddSingleton(gitHub);
        context.Services.AddSingleton(new FeedbackReporter(gitHub));
        context.Services.AddSingleton<IAzureFoundryChatClient, StubAzureFoundryChatClient>();
        context.Services.AddSingleton<IDevToolService, UnsupportedDevToolService>();

        // The sessions takeover, with nothing on the machine behind it. A shell test
        // asking whether the takeover replaced the panes should not also be reading
        // whatever the person running it had been doing that morning — and a pane that
        // only worked with rows in it would fail here, which is the point.
        context.Services.AddSingleton<IAgentSessionSource>(new EmptySessionSource());
        // And no delivery runs behind it, for the same reason.
        context.Services.AddSingleton<IDeliveryRunSource>(new EmptyRunSource());
        context.Services.AddSingleton<IAppUpdateService, UnsupportedAppUpdateService>();
        context.Services.AddSingleton<IDevbookFolderSource>(devbookFolderSource);
        // The Roadmap module the way a host wires it: a real plan document under the
        // same storage root, so the band draws what was stored rather than a fixture.
        context.Services.AddSingleton<IRoadmapPlanning>(sp =>
            TasksTestHost.PlanningFor(sp.GetRequiredService<WorkspaceSettingsStore>()));
        // The band gathers an item's linked and tagged work through this port before it
        // opens the editor, so a host that composes the band composes the rollup with it.
        context.Services.AddSingleton<IRoadmapItemRollup>(sp =>
            new Backlog.Infrastructure.FileSystem.Roadmap.RoadmapItemRollupService(
                TasksTestHost.EntriesFor(sp.GetRequiredService<WorkspaceSettingsStore>()),
                () => sp.GetRequiredService<WorkspaceSettingsStore>().RootDirectory));
        context.Services.AddSingleton<IImportedPlanSource>(sp =>
            TasksTestHost.ImportedPlansFor(sp.GetRequiredService<WorkspaceSettingsStore>()));
        context.Services.AddSingleton<IRoadmapWorkChanges>(TasksTestHost.WorkChanges());
        context.Services.AddSingleton(TasksTestHost.UntouchedPace());
        context.Services.AddSingleton<DesignDevbookProvider>();
        context.Services.AddSingleton<AiDevbookProvider>();
        context.Services.AddSingleton<TechnologyDevbookService>();
        context.Services.AddSingleton<InstructionSourceDiscovery>();
        context.Services.AddSingleton<DevbookMenu>();
        context.Services.AddSingleton<Arc42DevbookStore>();
        context.Services.AddSingleton<IFolderEditorLauncher, UnsupportedFolderEditorLauncher>();
        context.Services.AddSingleton<DevbookFolderOpenService>();
        context.Services.AddSingleton<DevbookScope>();
        // The pane publishes its open chapter here for the Ask AI source; a pane
        // rendered without it would fail on inject, as the application hosts would.
        context.Services.AddScoped<DevbookOpenChapter>();
        context.Services.AddSingleton<DevbookUpdateService>();
        context.Services.AddSingleton<IGitHubBranchCatalog>(new StubBranchCatalog());
        context.Services.AddSingleton<DevbookSourceSelection>();
        context.Services.AddSingleton(new DevbookCopilotCli(new UnavailableCopilotCliLauncher()));
        context.Services.AddSingleton<ILocalGitRepositoryService, LocalGitRepositoryService>();
        // The dashboard takeover, with no provider behind it — see DashboardTestHost.
        _ = context.Services.AddUnavailableDashboard("backlog", "backlog-ide");
        context.Services.AddScoped(sp => new DomainDevbookStore(sp.GetRequiredService<IDevbookFolderSource>()));
        context.Services.AddScoped(sp => TasksTestHost.StateFor(
            sp.GetRequiredService<WorkspaceSettingsStore>(),
            sp.GetRequiredService<GitHubIntegration>(),
            TasksCopilotCli.Unavailable,
            toasts: sp.GetRequiredService<IToastChannel>()));
        // Home answers the Inbox's Capture button through the module's runner and
        // injects it hard, so a host that renders Home composes the module and
        // picks where its sources are kept, the same as the application hosts do.
        context.Services.AddSingleton<ICaptureSourceSettings>(
            new CaptureSourcesSettingsStore(Path.Combine(root, "capture", "capture-sources.json")));
        context.Services.AddSingleton<ICaptureRunLog>(
            new CaptureRunLogStore(Path.Combine(root, "capture", "capture-runs.json")));
        context.Services.AddCaptureModule();
        InboxTestHost.AddCaptureDelivery(context.Services);

        TasksTestHost.AddToastChannel(context.Services);
        // The Inbox pane the shell now composes: its state over the in-memory
        // module, the same terms as the Tasks state above.
        _ = InboxTestHost.AddInboxState(context.Services);

        return new Harness(root, context, gitHubSettings);
    }

    private sealed record Harness(string Root, BunitContext Context, GitHubSettingsStore GitHubSettings) : IDisposable
    {
        public void Dispose()
        {
            Context.Dispose();

            try
            {
                if (Directory.Exists(Root)) Directory.Delete(Root, recursive: true);
            }
            catch (IOException)
            {
            }
        }
    }

    private sealed class EmptySessionSource : IAgentSessionSource
    {
        public Task<AgentSessionCatalog> GetSessionsAsync(AgentSessionQuery query, CancellationToken cancellationToken = default) =>
            GetSessionsAsync(cancellationToken);

        public Task<AgentSessionCatalog> GetSessionsAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(AgentSessionCatalog.Empty);
    }

    private sealed class EmptyRunSource : IDeliveryRunSource
    {
        public Task<DeliveryRunCatalog> GetRunsAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(DeliveryRunCatalog.Empty);
    }

    private sealed class StubAzureFoundryChatClient : IAzureFoundryChatClient
    {
        public Task<AzureFoundryChatResponse> AskAsync(AzureFoundryChatRequest request, CancellationToken cancellationToken = default) =>
            Task.FromResult(new AzureFoundryChatResponse("Not used in this test."));

        public Task<AzureFoundryPlanResponse> DraftPlanAsync(AzureFoundryPlanRequest request, CancellationToken cancellationToken = default) =>
            Task.FromResult(new AzureFoundryPlanResponse("# Not used in this test."));
    }

    private sealed class StubGitHubClient : IGitHubClient
    {
        public Task<GitHubCommittedFile> CommitFileAsync(GitHubRepositoryRef repository, string path, byte[] content, string commitMessage, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
        public Task<GitHubIssue> CreateIssueAsync(
            GitHubRepositoryRef repository,
            string title,
            string? body,
            IEnumerable<string>? labels = null,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<GitHubIssueSnapshot> GetIssueAsync(
            GitHubRepositoryRef repository,
            int number,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<GitHubUploadedFile> UploadFileAsync(
            GitHubRepositoryRef repository,
            string path,
            string branch,
            byte[] content,
            string commitMessage,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed class StubProbe : IGitHubConnectionProbe
    {
        public Task<GitHubConnection> DescribeAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new GitHubConnection(false, "Not connected."));

        public void Invalidate()
        {
        }
    }
}
