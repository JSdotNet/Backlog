using Backlog.UI.Components.Diagrams;

using Bunit;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.Time.Testing;

namespace Backlog.Desktop.UI.UnitTests;

/// <summary>
/// The Lobes atlas takeover (<c>.devbook/domain/devbook/features.md#lobes-atlas</c>)
/// over a graph handed in whole: its scope switch, the camera a switch resets, the
/// regions a scope hides, and what a selected node offers.
/// <para>
/// The camera is the renderer's, and it keeps one per layout: a new layout starts
/// from the default view. So "switching scope resets the camera" is proved here as
/// the layout the renderer is handed changing with the scope, which is the only
/// thing C# decides about it.
/// </para>
/// </summary>
public sealed class LobesAtlasTakeoverTests : IDisposable
{
    private const string RenderFunction = "backlogDiagrams.renderLobesAtlas";

    /// <summary>The clock the scope card's "Indexed …" line reads.</summary>
    private static readonly DateTimeOffset Now = new(2026, 10, 22, 12, 0, 0, TimeSpan.Zero);

    private readonly BunitContext _context = new();
    private readonly FixedLobes _lobes = new(SampleGraph());

    public LobesAtlasTakeoverTests()
    {
        _context.JSInterop.Mode = JSRuntimeMode.Loose;
        _context.Services.AddSingleton<IDevbookLobesSource>(_lobes);
        _context.Services.AddSingleton<TimeProvider>(new FakeTimeProvider(Now));
    }

    [Fact]
    public void The_scope_switch_offers_all_the_brain_each_devbook_and_each_ghost_dashed()
    {
        var component = Render();

        component.WaitForAssertion(() => Assert.NotEmpty(component.FindAll("[data-testid='lobes-atlas-scope']")));

        var options = component.FindAll("[data-testid='lobes-atlas-scope']");
        Assert.Equal(["All", "Brain", "backlog", "devbook-stack"], options.Select(option => option.TextContent.Trim()));
        Assert.Equal(["all", "brain", "repo:backlog", "ghost:devbook-stack"], options.Select(option => option.GetAttribute("data-scope")));
        Assert.Contains("lobes-atlas__scope--ghost", options[3].ClassList);
        Assert.DoesNotContain("lobes-atlas__scope--ghost", options[2].ClassList);

        // Opened from the header with no scope, it opens on All.
        Assert.Equal("true", options[0].GetAttribute("aria-pressed"));
        Assert.Equal(LobesAtlasModel.AllLayout, LastModel().Layout);
    }

    [Fact]
    public void Switching_scope_hands_the_renderer_a_new_layout_which_resets_the_camera()
    {
        var component = Render();
        component.WaitForAssertion(() => Assert.Equal(LobesAtlasModel.AllLayout, LastModel().Layout));

        Scope(component, "repo:backlog").Click();

        component.WaitForAssertion(() =>
        {
            Assert.Equal("repo:backlog", LastModel().Layout);
            Assert.Equal("true", Scope(component, "repo:backlog").GetAttribute("aria-pressed"));
            Assert.Equal("false", Scope(component, LobesAtlasModel.AllLayout).GetAttribute("aria-pressed"));
            Assert.Equal("repo:backlog", component.Find("[data-testid='lobes-atlas']").GetAttribute("data-scope"));
            Assert.Contains("backlog devbook", component.Find("[data-testid='lobes-atlas-headline']").TextContent, StringComparison.Ordinal);
            Assert.Equal("Open devbook", component.Find("[data-testid='lobes-atlas-card-action']").TextContent.Trim());
        });

        // The index lists only that scope's marks.
        Assert.All(component.FindAll("[data-testid='graph-atlas-index-option']"),
            option => Assert.StartsWith("repo:backlog|", option.GetAttribute("data-node-id"), StringComparison.Ordinal));

        Scope(component, "ghost:devbook-stack").Click();
        component.WaitForAssertion(() =>
        {
            Assert.Equal("ghost:devbook-stack", LastModel().Layout);
            Assert.Equal("Register repository", component.Find("[data-testid='lobes-atlas-card-action']").TextContent.Trim());
        });
    }

    [Fact]
    public void A_scope_handed_in_opens_on_that_scope_and_an_unknown_one_opens_all()
    {
        var component = Render("brain");
        component.WaitForAssertion(() =>
        {
            Assert.Equal("brain", LastModel().Layout);
            Assert.Equal("true", Scope(component, "brain").GetAttribute("aria-pressed"));
            Assert.Equal("Anatomical regions", component.Find(".lobes-atlas__panel-title").TextContent.Trim());
            Assert.Equal("Open in the pane", component.Find("[data-testid='lobes-atlas-card-action']").TextContent.Trim());
        });

        var unknown = Render("repo:nowhere");
        unknown.WaitForAssertion(() => Assert.Equal("true", Scope(unknown, LobesAtlasModel.AllLayout).GetAttribute("aria-pressed")));
    }

    /// <summary>A ghost handed back after its alias was registered — the graph
    /// holds that devbook now — opens on the devbook.</summary>
    [Fact]
    public void A_ghost_handed_in_whose_alias_is_registered_since_opens_its_devbook()
    {
        var component = Render("ghost:backlog");

        component.WaitForAssertion(() =>
        {
            Assert.Equal("repo:backlog", LastModel().Layout);
            Assert.Equal("true", Scope(component, "repo:backlog").GetAttribute("aria-pressed"));
        });
    }

    /// <summary>Register repository tells the host which ghost it left on before
    /// it leaves for the settings.</summary>
    [Fact]
    public void Register_repository_hands_the_host_the_ghosts_scope_before_it_leaves()
    {
        string? heard = null;
        string? uriWhenHeard = null;
        var navigation = _context.Services.GetRequiredService<NavigationManager>();
        var component = _context.Render<LobesAtlasTakeover>(parameters => parameters
            .Add(atlas => atlas.Scope, "ghost:devbook-stack")
            .Add(atlas => atlas.OnRegisterRepository, (string scope) =>
            {
                heard = scope;
                uriWhenHeard = navigation.Uri;
            }));

        component.WaitForAssertion(() =>
            Assert.Equal("Register repository", component.Find("[data-testid='lobes-atlas-card-action']").TextContent.Trim()));
        component.Find("[data-testid='lobes-atlas-card-action']").Click();

        Assert.Equal("ghost:devbook-stack", heard);
        Assert.DoesNotContain("settings/repositories", uriWhenHeard, StringComparison.Ordinal);
        Assert.EndsWith("/settings/repositories", navigation.Uri, StringComparison.Ordinal);
    }

    [Fact]
    public void A_region_row_hides_that_region_of_the_scope_on_screen()
    {
        var component = Render("repo:backlog");
        component.WaitForAssertion(() => Assert.NotEmpty(component.FindAll("[data-testid='lobes-atlas-region']")));

        var arc42 = component.Find("[data-testid='lobes-atlas-region'][data-lobe='frontal']");
        Assert.Equal("true", arc42.GetAttribute("aria-pressed"));
        arc42.Click();

        component.WaitForAssertion(() =>
        {
            Assert.Contains(new LobesAtlasRegionKey("repo:backlog", LobesAtlasMark.Frontal), LastModel().HiddenLobes);
            Assert.Equal("false", component.Find("[data-testid='lobes-atlas-region'][data-lobe='frontal']").GetAttribute("aria-pressed"));
            Assert.DoesNotContain(component.FindAll("[data-testid='graph-atlas-index-option']"),
                option => option.GetAttribute("data-node-id") == "repo:backlog|.devbook/arc42/01-introduction.md");
        });

        component.Find("[data-testid='lobes-atlas-show-all']").Click();
        component.WaitForAssertion(() => Assert.Empty(LastModel().HiddenLobes));
    }

    [Fact]
    public void A_brain_note_offers_to_open_in_the_pane()
    {
        string? opened = null;
        var component = Render("brain", onOpenNote: path => opened = path);
        component.WaitForAssertion(() => Assert.NotEmpty(component.FindAll("[data-testid='graph-atlas-index-option']")));

        component.Find("[data-testid='graph-atlas-index-option'][data-node-id='brain|projects/plan.md']").Click();
        component.WaitForAssertion(() => component.Find("[data-testid='lobes-atlas-open-note']").Click());

        Assert.Equal("projects/plan.md", opened);
    }

    /// <summary>The keyboard browses the list beside the map without the sheet
    /// stepping in: an arrow moves to a row and leaves the sheet shut, Enter opens
    /// that row's details, and Escape closes them again with the list still on
    /// the row, its tab stop, for the sheet to hand the focus back to.</summary>
    [Fact]
    public void The_keyboard_arrows_through_the_list_without_opening_the_sheet_and_enter_opens_it()
    {
        var component = Render("brain");
        component.WaitForAssertion(() => Assert.NotEmpty(component.FindAll("[data-testid='graph-atlas-index-option']")));

        component.Find("[data-testid='lobes-atlas-graph-index']").KeyDown(new KeyboardEventArgs { Key = "ArrowDown" });

        Assert.Equal("false", component.Find("[data-testid='lobes-atlas-sheet']").GetAttribute("data-open"));
        var row = Assert.Single(component.FindAll("[data-testid='graph-atlas-index-option']"), option => option.GetAttribute("tabindex") == "0");
        var rowId = row.GetAttribute("data-node-id");
        Assert.Equal(component.FindAll("[data-testid='graph-atlas-index-option']")[1].GetAttribute("data-node-id"), rowId);

        component.Find("[data-testid='lobes-atlas-graph-index']").KeyDown(new KeyboardEventArgs { Key = "Enter" });

        component.WaitForAssertion(() => Assert.Equal("true", component.Find("[data-testid='lobes-atlas-sheet']").GetAttribute("data-open")));
        Assert.Equal("true", component.Find($"[data-testid='graph-atlas-index-option'][data-node-id='{rowId}']").GetAttribute("aria-selected"));

        component.Find("[data-testid='lobes-atlas-sheet']").KeyDown(new KeyboardEventArgs { Key = "Escape" });

        component.WaitForAssertion(() => Assert.Equal("false", component.Find("[data-testid='lobes-atlas-sheet']").GetAttribute("data-open")));
        Assert.Equal("0", component.Find($"[data-testid='graph-atlas-index-option'][data-node-id='{rowId}']").GetAttribute("tabindex"));
    }

    [Fact]
    public void A_resolved_chapter_opens_its_devbook_and_a_dashed_one_says_why_it_cannot()
    {
        BrainRepoLink? opened = null;
        var component = Render(onOpenChapter: link => opened = link);
        component.WaitForAssertion(() => Assert.NotEmpty(component.FindAll("[data-testid='graph-atlas-index-option']")));

        component.Find("[data-testid='graph-atlas-index-option'][data-node-id='ghost:devbook-stack|ghost:tech/radar.md']").Click();
        component.WaitForAssertion(() =>
            Assert.Contains(BrainRepoLinkResolver.NotOnThisMachine, component.Find("[data-testid='lobes-atlas-repo-link']").TextContent, StringComparison.Ordinal));
        Assert.Empty(component.FindAll("[data-testid='lobes-atlas-repo-link'] button"));

        component.Find("[data-testid='graph-atlas-index-option'][data-node-id='repo:backlog|.devbook/arc42/adr/0001-use-sqlite.md']").Click();
        component.WaitForAssertion(() => component.Find("[data-testid='lobes-atlas-repo-link'] button").Click());

        Assert.NotNull(opened);
        Assert.Equal("backlog", opened.Alias);
        Assert.Equal(".devbook/arc42/adr/0001-use-sqlite.md", opened.Path);
    }

    /// <summary>A chapter a note names in a devbook not indexed yet says that, as
    /// its scope card does, rather than that it is no chapter of the devbook.</summary>
    [Fact]
    public void A_chapter_of_a_devbook_not_indexed_yet_says_so_on_its_chip_as_the_card_does()
    {
        _lobes.Graph = DevbookLobesReader.Read(
            new DevbookLobesBrainSource(
                [new("projects/plan.md", "Plan", "projects", "file", string.Empty, "projects/plan.md", false)],
                [new("related", "projects/plan.md", "repo:backlog/.devbook/arc42/05-building-block-view.md")],
                new Dictionary<string, string>(),
                ["projects"]),
            [DevbookLobesDevbookSource.Missing("backlog", "Its index has not been built yet.")],
            link => new BrainRepoLinkResolution(link, false, BrainRepoLinkResolver.NotADevbookChapter));
        var chapter = Assert.Single(_lobes.Graph.Nodes, node => node.Kind == DevbookLobesNodeKind.GhostChapter);

        var component = Render("repo:backlog");
        component.WaitForAssertion(() => Assert.NotEmpty(component.FindAll("[data-testid='graph-atlas-index-option']")));
        component.Find($"[data-testid='graph-atlas-index-option'][data-node-id='{chapter.Id}']").Click();

        component.WaitForAssertion(() =>
        {
            var chip = component.Find("[data-testid='lobes-atlas-repo-link']").TextContent;
            Assert.Contains(DevbookLobesReader.NotIndexedYet, chip, StringComparison.Ordinal);
            Assert.DoesNotContain(BrainRepoLinkResolver.NotADevbookChapter, chip, StringComparison.Ordinal);
        });
        Assert.Empty(component.FindAll("[data-testid='lobes-atlas-repo-link'] button"));
        Assert.Contains("Not indexed yet", component.Find("[data-testid='lobes-atlas-card']").TextContent, StringComparison.Ordinal);
    }

    [Fact]
    public void A_devbook_chapter_no_note_links_to_still_opens_in_its_devbook()
    {
        BrainRepoLink? opened = null;
        var component = Render("repo:backlog", onOpenChapter: link => opened = link);
        component.WaitForAssertion(() => Assert.NotEmpty(component.FindAll("[data-testid='graph-atlas-index-option']")));

        component.Find("[data-testid='graph-atlas-index-option'][data-node-id='repo:backlog|.devbook/arc42/01-introduction.md']").Click();
        component.WaitForAssertion(() => component.Find("[data-testid='lobes-atlas-open-chapter']").Click());

        Assert.NotNull(opened);
        Assert.Equal("backlog", opened.Alias);
        Assert.Equal(".devbook/arc42/01-introduction.md", opened.Path);
    }

    [Fact]
    public void The_close_button_and_the_card_raise_the_hosts_callbacks()
    {
        var closed = 0;
        string? openedScope = null;
        var component = _context.Render<LobesAtlasTakeover>(parameters => parameters
            .Add(atlas => atlas.Scope, "repo:backlog")
            .Add(atlas => atlas.OnClose, () => closed++)
            .Add(atlas => atlas.OnOpenScope, (string alias) => openedScope = alias));
        component.WaitForAssertion(() => Assert.NotEmpty(component.FindAll("[data-testid='lobes-atlas-card-action']")));

        component.Find("[data-testid='lobes-atlas-card-action']").Click();
        component.WaitForAssertion(() => Assert.Equal("backlog", openedScope));

        var close = component.Find("[data-testid='lobes-atlas-close']");
        Assert.Equal("Close the atlas", close.GetAttribute("aria-label"));
        close.Click();
        Assert.Equal(1, closed);
    }

    [Fact]
    public void A_read_that_failed_is_said_in_words_with_a_way_back()
    {
        _lobes.Graph = DevbookLobesGraph.Unavailable(DevbookAtlasService.NoBrainFolder);
        var closed = 0;
        var component = _context.Render<LobesAtlasTakeover>(parameters => parameters.Add(atlas => atlas.OnClose, () => closed++));

        component.WaitForAssertion(() =>
            Assert.Contains("No Brain folder is set", component.Find("[data-testid='lobes-atlas-unavailable']").TextContent, StringComparison.Ordinal));
        Assert.Empty(component.FindAll("[data-testid='lobes-atlas-graph']"));
        Assert.Empty(component.FindAll("[data-testid='lobes-atlas-empty']"));
        Assert.Empty(component.FindAll("[data-testid='lobes-atlas-scope']"));

        component.Find("[data-testid='lobes-atlas-back']").Click();
        Assert.Equal(1, closed);
    }

    // ----- The empty state -----------------------------------------------------------

    [Fact]
    public void With_no_brain_index_the_empty_state_offers_to_build_it_and_a_way_back_instead_of_a_map()
    {
        _lobes.Graph = DevbookLobesGraph.MissingBrainIndex(DevbookAtlasService.BrainIndexMissing);
        var closed = 0;
        var component = _context.Render<LobesAtlasTakeover>(parameters => parameters.Add(atlas => atlas.OnClose, () => closed++));

        component.WaitForAssertion(() => Assert.NotEmpty(component.FindAll("[data-testid='lobes-atlas-empty']")));
        var empty = component.Find("[data-testid='lobes-atlas-empty']");
        Assert.Contains("Your Brain isn’t indexed yet", empty.TextContent, StringComparison.Ordinal);
        Assert.Empty(component.FindAll("[data-testid='lobes-atlas-graph']"));
        Assert.Empty(component.FindAll("[data-testid='lobes-atlas-unavailable']"));
        Assert.Empty(component.FindAll("[data-testid='lobes-atlas-scope']"));

        var build = component.Find("[data-testid='lobes-atlas-build']");
        var back = component.Find("[data-testid='lobes-atlas-back']");
        AssertRealButton(build);
        AssertRealButton(back);
        Assert.Equal("Build the index now", build.TextContent.Trim());
        Assert.Equal("Back to notes", back.TextContent.Trim());

        back.Click();
        Assert.Equal(1, closed);
    }

    [Fact]
    public void Build_the_index_now_builds_it_and_the_map_takes_the_empty_states_place()
    {
        _lobes.Graph = DevbookLobesGraph.MissingBrainIndex(DevbookAtlasService.BrainIndexMissing);
        _lobes.OnBuild = () => _lobes.Graph = SampleGraph();
        var component = _context.Render<LobesAtlasTakeover>();
        component.WaitForAssertion(() => Assert.NotEmpty(component.FindAll("[data-testid='lobes-atlas-build']")));

        component.Find("[data-testid='lobes-atlas-build']").Click();

        component.WaitForAssertion(() =>
        {
            Assert.Equal(1, _lobes.Builds);
            Assert.Empty(component.FindAll("[data-testid='lobes-atlas-empty']"));
            Assert.NotEmpty(component.FindAll("[data-testid='lobes-atlas-graph']"));
        });
    }

    [Fact]
    public void A_build_that_fails_or_finds_nothing_says_why_and_keeps_the_offer()
    {
        _lobes.Graph = DevbookLobesGraph.MissingBrainIndex(DevbookAtlasService.BrainIndexMissing);
        _lobes.BuildProblem = "The Brain's index could not be built: disk full";
        var component = _context.Render<LobesAtlasTakeover>();
        component.WaitForAssertion(() => Assert.NotEmpty(component.FindAll("[data-testid='lobes-atlas-build']")));

        component.Find("[data-testid='lobes-atlas-build']").Click();
        component.WaitForAssertion(() =>
            Assert.Equal("The Brain's index could not be built: disk full", component.Find("[data-testid='lobes-atlas-build-problem']").TextContent.Trim()));
        Assert.Equal("alert", component.Find("[data-testid='lobes-atlas-build-problem']").GetAttribute("role"));
        Assert.NotEmpty(component.FindAll("[data-testid='lobes-atlas-build']"));

        // A build that ran but left no index — an empty folder — says so too.
        _lobes.BuildProblem = null;
        component.Find("[data-testid='lobes-atlas-build']").Click();
        component.WaitForAssertion(() =>
            Assert.Equal(LobesAtlasTakeover.NothingToIndex, component.Find("[data-testid='lobes-atlas-build-problem']").TextContent.Trim()));
    }

    [Fact]
    public void A_host_that_cannot_build_the_index_offers_only_the_way_back()
    {
        _lobes.Graph = DevbookLobesGraph.MissingBrainIndex(DevbookAtlasService.BrainIndexMissing);
        _lobes.CanBuildBrainIndex = false;
        var component = _context.Render<LobesAtlasTakeover>();

        component.WaitForAssertion(() => Assert.NotEmpty(component.FindAll("[data-testid='lobes-atlas-back']")));
        Assert.Empty(component.FindAll("[data-testid='lobes-atlas-build']"));
        Assert.Contains("builds in the background", component.Find("[data-testid='lobes-atlas-empty']").TextContent, StringComparison.Ordinal);
    }

    // ----- The left panel ------------------------------------------------------------

    [Fact]
    public void In_all_the_panel_lists_each_scope_with_its_count_and_a_row_opens_it()
    {
        var component = Render();
        component.WaitForAssertion(() => Assert.NotEmpty(component.FindAll("[data-testid='lobes-atlas-row']")));

        Assert.Equal("Scopes", component.Find(".lobes-atlas__panel-title").TextContent.Trim());
        var rows = component.FindAll("[data-testid='lobes-atlas-row']");
        Assert.Equal(["brain", "repo:backlog", "ghost:devbook-stack"], rows.Select(row => row.GetAttribute("data-scope")));
        Assert.Equal(["2", "3", "—"], rows.Select(row => row.QuerySelector(".lobes-atlas__row-count")!.TextContent.Trim()));
        Assert.All(rows, row =>
        {
            AssertRealButton(row);
            // A scope row opens rather than toggles, so it is not a pressed state.
            Assert.Null(row.GetAttribute("aria-pressed"));
        });
        Assert.Equal("Open backlog", rows[1].GetAttribute("aria-label"));

        rows[1].Click();
        component.WaitForAssertion(() => Assert.Equal("repo:backlog", LastModel().Layout));
    }

    [Fact]
    public void In_a_scope_the_panel_lists_its_six_regions_with_what_each_holds_and_its_count()
    {
        var component = Render("brain");
        component.WaitForAssertion(() => Assert.Equal(6, component.FindAll("[data-testid='lobes-atlas-region']").Count));

        Assert.Equal("Anatomical regions", component.Find(".lobes-atlas__panel-title").TextContent.Trim());
        var rows = component.FindAll("[data-testid='lobes-atlas-region']");
        Assert.Equal(["Frontal", "Parietal", "Temporal", "Occipital", "Cerebellum", "Brain stem"],
            rows.Select(row => row.QuerySelector(".lobes-atlas__row-name")!.TextContent.Trim()));
        Assert.Equal("Projects", rows[0].QuerySelector(".lobes-atlas__row-sub")!.TextContent.Trim());
        Assert.Equal(["1", "0", "1", "2", "0", "1"], rows.Select(row => row.QuerySelector(".lobes-atlas__row-count")!.TextContent.Trim()));
        Assert.All(rows, row =>
        {
            AssertRealButton(row);
            Assert.Equal("true", row.GetAttribute("aria-pressed"));
        });

        Scope(component, "repo:backlog").Click();
        component.WaitForAssertion(() => Assert.Equal("Devbook folders", component.Find(".lobes-atlas__panel-title").TextContent.Trim()));
        var folders = component.FindAll("[data-testid='lobes-atlas-region']");
        Assert.Equal(["arc42", "tech", "domain", "design", "ai", "Index"],
            folders.Select(row => row.QuerySelector(".lobes-atlas__row-name")!.TextContent.Trim()));
        Assert.Equal("Structure · Decisions · Debt", folders[0].QuerySelector(".lobes-atlas__row-sub")!.TextContent.Trim());
        Assert.Equal("2", folders[0].QuerySelector(".lobes-atlas__row-count")!.TextContent.Trim());
    }

    // ----- The top right -------------------------------------------------------------

    [Fact]
    public void In_a_scope_the_top_right_has_labels_all_none_and_one_chip_per_region()
    {
        var component = Render("repo:backlog");
        component.WaitForAssertion(() => Assert.Equal(6, component.FindAll("[data-testid='lobes-atlas-chip']").Count));

        var tools = component.Find(".lobes-atlas__tools");
        Assert.Equal(["Labels", "All", "None", "✕"], tools.QuerySelectorAll("button").Select(button => button.TextContent.Trim()));
        // All and None are named by their own words; what they do is the title.
        Assert.Null(component.Find("[data-testid='lobes-atlas-show-all']").GetAttribute("aria-label"));
        Assert.Equal("Show every region", component.Find("[data-testid='lobes-atlas-show-all']").GetAttribute("title"));
        Assert.Equal("Show regions", component.Find("[data-testid='lobes-atlas-chips']").GetAttribute("aria-label"));

        var chips = component.FindAll("[data-testid='lobes-atlas-chip']");
        Assert.Equal(["ARC", "TEC", "DOM", "DES", "AI", "IDX"], chips.Select(chip => chip.TextContent.Trim()));
        // The name starts with the code on the chip, so a speech user can say it.
        Assert.Equal(["ARC arc42", "TEC tech", "DOM domain", "DES design", "AI ai", "IDX Index"], chips.Select(chip => chip.GetAttribute("aria-label")));
        Assert.All(chips, chip =>
        {
            AssertRealButton(chip);
            Assert.Equal("true", chip.GetAttribute("aria-pressed"));
        });

        // A chip hides its region, and the panel's row follows it.
        component.Find("[data-testid='lobes-atlas-chip'][data-lobe='parietal']").Click();
        component.WaitForAssertion(() =>
        {
            Assert.Contains(new LobesAtlasRegionKey("repo:backlog", LobesAtlasMark.Parietal), LastModel().HiddenLobes);
            Assert.Equal("false", component.Find("[data-testid='lobes-atlas-chip'][data-lobe='parietal']").GetAttribute("aria-pressed"));
            Assert.Equal("false", component.Find("[data-testid='lobes-atlas-region'][data-lobe='parietal']").GetAttribute("aria-pressed"));
        });
    }

    [Fact]
    public void In_all_the_chips_are_one_per_scope_and_hide_that_scope()
    {
        var component = Render();
        component.WaitForAssertion(() => Assert.Equal(3, component.FindAll("[data-testid='lobes-atlas-chip']").Count));

        Assert.Equal("Show scopes", component.Find("[data-testid='lobes-atlas-chips']").GetAttribute("aria-label"));
        var chips = component.FindAll("[data-testid='lobes-atlas-chip']");
        Assert.Equal(["brain", "repo:backlog", "ghost:devbook-stack"], chips.Select(chip => chip.GetAttribute("data-scope")));
        Assert.All(chips, AssertRealButton);

        component.Find("[data-testid='lobes-atlas-chip'][data-scope='repo:backlog']").Click();
        component.WaitForAssertion(() =>
        {
            Assert.Contains("repo:backlog", LastModel().HiddenScopes);
            Assert.Equal("false", component.Find("[data-testid='lobes-atlas-chip'][data-scope='repo:backlog']").GetAttribute("aria-pressed"));
        });
    }

    [Fact]
    public void All_and_none_act_on_the_current_scope_only()
    {
        var component = Render("repo:backlog");
        component.WaitForAssertion(() => Assert.NotEmpty(component.FindAll("[data-testid='lobes-atlas-chip']")));

        // A region hidden in backlog…
        component.Find("[data-testid='lobes-atlas-chip'][data-lobe='frontal']").Click();
        component.WaitForAssertion(() => Assert.Single(LastModel().HiddenLobes));

        // …is left alone by None and All on the Brain.
        Scope(component, "brain").Click();
        component.WaitForAssertion(() => Assert.Equal("brain", LastModel().Layout));
        component.Find("[data-testid='lobes-atlas-show-none']").Click();
        component.WaitForAssertion(() =>
        {
            Assert.Equal(7, LastModel().HiddenLobes.Count);
            Assert.Contains(new LobesAtlasRegionKey("repo:backlog", LobesAtlasMark.Frontal), LastModel().HiddenLobes);
            Assert.All(component.FindAll("[data-testid='lobes-atlas-chip']"), chip => Assert.Equal("false", chip.GetAttribute("aria-pressed")));
        });

        component.Find("[data-testid='lobes-atlas-show-all']").Click();
        component.WaitForAssertion(() =>
            Assert.Equal([new LobesAtlasRegionKey("repo:backlog", LobesAtlasMark.Frontal)], LastModel().HiddenLobes));

        // In All, None hides every scope and leaves the regions as they were.
        Scope(component, LobesAtlasModel.AllLayout).Click();
        component.WaitForAssertion(() => Assert.Equal(LobesAtlasModel.AllLayout, LastModel().Layout));
        Assert.Equal("Hide every scope", component.Find("[data-testid='lobes-atlas-show-none']").GetAttribute("title"));
        component.Find("[data-testid='lobes-atlas-show-none']").Click();
        component.WaitForAssertion(() =>
        {
            Assert.Equal(3, LastModel().HiddenScopes.Count);
            Assert.Single(LastModel().HiddenLobes);
        });
    }

    [Fact]
    public void Labels_is_a_toggle_that_reports_its_state()
    {
        var component = Render();
        component.WaitForAssertion(() => Assert.NotEmpty(component.FindAll("[data-testid='lobes-atlas-labels']")));

        var labels = component.Find("[data-testid='lobes-atlas-labels']");
        AssertRealButton(labels);
        Assert.Equal("true", labels.GetAttribute("aria-pressed"));
        Assert.True(LastModel().ShowLabels);

        labels.Click();
        component.WaitForAssertion(() =>
        {
            Assert.Equal("false", component.Find("[data-testid='lobes-atlas-labels']").GetAttribute("aria-pressed"));
            Assert.False(LastModel().ShowLabels);
        });
    }

    // ----- The scope card ------------------------------------------------------------

    [Fact]
    public void The_scope_card_says_when_the_scope_was_last_indexed_and_offers_its_action()
    {
        var graph = SampleGraph();
        _lobes.Graph = graph with
        {
            Scopes =
            [
                .. graph.Scopes.Select(scope => scope.Kind switch
                {
                    DevbookLobesScopeKind.Brain => scope with { IndexedAt = Now.AddMinutes(-4) },
                    DevbookLobesScopeKind.Devbook => scope with { IndexedAt = null },
                    _ => scope
                })
            ]
        };
        var component = Render("brain");
        component.WaitForAssertion(() => Assert.NotEmpty(component.FindAll("[data-testid='lobes-atlas-card']")));

        var card = component.Find("[data-testid='lobes-atlas-card']");
        Assert.Equal("Your notes", card.QuerySelector(".lobes-atlas__card-title")!.TextContent.Trim());
        Assert.Contains(card.QuerySelectorAll(".lobes-atlas__card-line"), line => line.TextContent.Trim() == "Indexed 4 minutes ago");
        Assert.Contains(card.QuerySelectorAll(".lobes-atlas__card-line"), line => line.TextContent.Trim() == "2 notes");
        AssertRealButton(component.Find("[data-testid='lobes-atlas-card-action']"));
        Assert.Equal("Open in the pane", component.Find("[data-testid='lobes-atlas-card-action']").TextContent.Trim());

        Scope(component, "repo:backlog").Click();
        component.WaitForAssertion(() =>
        {
            var lines = component.FindAll(".lobes-atlas__card-line").Select(line => line.TextContent.Trim()).ToList();
            Assert.Contains("3 chapters", lines);
            Assert.Contains("Not indexed yet", lines);
            Assert.Equal("Open devbook", component.Find("[data-testid='lobes-atlas-card-action']").TextContent.Trim());
        });

        // A ghost was never indexed here; its action, a button like the others,
        // opens the repository settings.
        Scope(component, "ghost:devbook-stack").Click();
        component.WaitForAssertion(() =>
            Assert.DoesNotContain(component.FindAll(".lobes-atlas__card-line"), line => line.TextContent.Contains("indexed", StringComparison.OrdinalIgnoreCase)));
        var register = component.Find("[data-testid='lobes-atlas-card-action']");
        AssertRealButton(register);
        register.Click();
        Assert.EndsWith("/settings/repositories", _context.Services.GetRequiredService<NavigationManager>().Uri, StringComparison.Ordinal);
    }

    // ----- The foot ------------------------------------------------------------------

    [Fact]
    public void The_foot_shows_the_hint_with_the_zoom_level_and_the_marker_turns_with_the_camera()
    {
        var component = Render();
        component.WaitForAssertion(() => Assert.NotEmpty(component.FindAll("[data-testid='lobes-atlas-hint']")));

        Assert.Equal("Drag — rotate · Scroll — zoom · 100%", Collapse(component.Find("[data-testid='lobes-atlas-hint']").TextContent));
        var marker = component.Find("[data-testid='lobes-atlas-orientation']");
        Assert.Equal("true", marker.GetAttribute("aria-hidden"));
        Assert.Equal(["S", "A", "L"], marker.QuerySelectorAll("line").Select(line => line.GetAttribute("data-axis")));
        var before = AxisEnd(component, "L");

        // The renderer reports a drag and a zoom through the atlas host.
        var graph = component.FindComponent<GraphAtlas>();
        graph.InvokeAsync(() => graph.Instance.CameraMoved(0.4, 0.3, 1.5));

        component.WaitForAssertion(() =>
        {
            Assert.Equal("150%", component.Find("[data-testid='lobes-atlas-zoom']").TextContent.Trim());
            Assert.Equal("0.4", component.Find("[data-testid='lobes-atlas-orientation']").GetAttribute("data-yaw"));
            Assert.NotEqual(before, AxisEnd(component, "L"));
        });
    }

    // ----- Every control -------------------------------------------------------------

    [Theory]
    [InlineData(LobesAtlasModel.AllLayout)]
    [InlineData("brain")]
    [InlineData("repo:backlog")]
    [InlineData("ghost:devbook-stack")]
    public void Every_control_of_the_atlas_is_a_real_button(string scope)
    {
        var component = Render(scope);
        component.WaitForAssertion(() => Assert.NotEmpty(component.FindAll("[data-testid='lobes-atlas-chip']")));

        AssertEveryControlIsANamedButton(component.FindAll(".lobes-atlas__header button, .lobes-atlas__header a, .lobes-atlas__panel button, .lobes-atlas__panel a"));
    }

    [Fact]
    public void Every_control_of_the_empty_state_is_a_real_button()
    {
        _lobes.Graph = DevbookLobesGraph.MissingBrainIndex(DevbookAtlasService.BrainIndexMissing);
        var component = _context.Render<LobesAtlasTakeover>();
        component.WaitForAssertion(() => Assert.NotEmpty(component.FindAll("[data-testid='lobes-atlas-empty']")));

        var controls = component.FindAll("[data-testid='lobes-atlas'] button, [data-testid='lobes-atlas'] a");
        Assert.Equal(3, controls.Count);
        AssertEveryControlIsANamedButton(controls);
    }

    [Fact]
    public void While_the_index_builds_the_button_is_busy_says_so_and_a_second_press_starts_no_second_build()
    {
        _lobes.Graph = DevbookLobesGraph.MissingBrainIndex(DevbookAtlasService.BrainIndexMissing);
        var finish = new TaskCompletionSource<string?>();
        _lobes.Pending = finish.Task;
        var component = _context.Render<LobesAtlasTakeover>();
        component.WaitForAssertion(() => Assert.NotEmpty(component.FindAll("[data-testid='lobes-atlas-build']")));

        component.Find("[data-testid='lobes-atlas-build']").Click();
        component.WaitForAssertion(() =>
        {
            Assert.Equal("true", component.Find("[data-testid='lobes-atlas-build']").GetAttribute("aria-busy"));
            Assert.StartsWith("Building the index", component.Find("[data-testid='lobes-atlas-footnote']").TextContent.Trim(), StringComparison.Ordinal);
        });

        component.Find("[data-testid='lobes-atlas-build']").Click();
        Assert.Equal(1, _lobes.Builds);

        _lobes.Graph = SampleGraph();
        component.InvokeAsync(() => finish.SetResult(null));
        component.WaitForAssertion(() => Assert.NotEmpty(component.FindAll("[data-testid='lobes-atlas-graph']")));
        Assert.Equal(1, _lobes.Builds);
    }

    [Fact]
    public void An_atlas_opened_on_a_scope_over_the_empty_state_opens_there_once_the_index_is_built()
    {
        _lobes.Graph = DevbookLobesGraph.MissingBrainIndex(DevbookAtlasService.BrainIndexMissing);
        _lobes.OnBuild = () => _lobes.Graph = SampleGraph();
        var component = Render("repo:backlog");
        component.WaitForAssertion(() => Assert.NotEmpty(component.FindAll("[data-testid='lobes-atlas-build']")));

        component.Find("[data-testid='lobes-atlas-build']").Click();

        component.WaitForAssertion(() => Assert.Equal("repo:backlog", LastModel().Layout));
    }

    [Fact]
    public void The_stylesheet_gives_every_atlas_control_a_44px_target_and_a_focus_ring()
    {
        var css = File.ReadAllText(RepositoryRoot.File("src", "App", "Backlog.Desktop.UI", "wwwroot", "app.css")).Replace("\r\n", "\n");

        var target = Rule(css, ".lobes-atlas__header :is(button, a),");
        Assert.Contains(".lobes-atlas__panel :is(button, a),", target, StringComparison.Ordinal);
        Assert.Contains(".lobes-atlas__empty button", target, StringComparison.Ordinal);
        Assert.Contains("min-width: 44px;", target, StringComparison.Ordinal);
        Assert.Contains("min-height: 44px;", target, StringComparison.Ordinal);

        var focus = Rule(css, ".lobes-atlas__row:focus-visible,");
        Assert.Contains(".lobes-atlas__chip:focus-visible", focus, StringComparison.Ordinal);
        Assert.Contains(".lobes-atlas__header .header-group__option:focus-visible", focus, StringComparison.Ordinal);
        Assert.Contains("outline: var(--border-width-2) solid var(--color-border-focus);", focus, StringComparison.Ordinal);
    }

    /// <summary>Found in the live QA pass: at 1440 x 900 the panel scrolled as a
    /// whole, so the scope card's one action sat under its fold. The rows give way
    /// first, down to two; the title and the card keep their height.</summary>
    [Fact]
    public void The_stylesheet_scrolls_only_the_panels_rows_so_the_scope_card_stays_in_view()
    {
        var css = File.ReadAllText(RepositoryRoot.File("src", "App", "Backlog.Desktop.UI", "wwwroot", "app.css")).Replace("\r\n", "\n");

        var rows = Rule(css, ".lobes-atlas__rows {");
        Assert.Contains("flex: 0 1 auto;", rows, StringComparison.Ordinal);
        Assert.Contains("overflow-y: auto;", rows, StringComparison.Ordinal);
        Assert.Contains("min-height: calc(2 * 44px", rows, StringComparison.Ordinal);
        Assert.Contains("padding: var(--spacing-xs);", rows, StringComparison.Ordinal);

        Assert.Contains("flex: none;", Rule(css, ".lobes-atlas__card {"), StringComparison.Ordinal);
        Assert.Contains("flex: none;", Rule(css, ".lobes-atlas__panel-title {"), StringComparison.Ordinal);

        // The rows' own box gives way too, down to the title and two rows, and
        // clips below its content height so the rows cannot run over the card.
        var legend = Rule(css, ".lobes-atlas__legend {");
        Assert.Contains("flex: 0 1 auto;", legend, StringComparison.Ordinal);
        Assert.Contains("min-height: calc(2 * 44px", legend, StringComparison.Ordinal);
        Assert.Contains("overflow: hidden;", legend, StringComparison.Ordinal);
    }

    /// <summary>Found in the live QA pass: at 390 px the fused scope switch clipped
    /// six of thirteen scopes and squeezed the rest until their names overlapped.
    /// The switch scrolls sideways instead and no option shrinks below its name.</summary>
    [Fact]
    public void The_stylesheet_lets_the_scope_switch_scroll_rather_than_clip_or_squeeze_its_options()
    {
        var css = File.ReadAllText(RepositoryRoot.File("src", "App", "Backlog.Desktop.UI", "wwwroot", "app.css")).Replace("\r\n", "\n");

        var scopes = Rule(css, ".lobes-atlas__scopes {");
        Assert.Contains("overflow-x: auto;", scopes, StringComparison.Ordinal);
        Assert.Contains("max-width: 100%;", scopes, StringComparison.Ordinal);

        Assert.Contains("flex: none;", Rule(css, ".lobes-atlas__scopes > .header-group__option {"), StringComparison.Ordinal);
    }

    /// <summary>Lobes.dc.html draws the map full-bleed and floats everything over
    /// it. The takeover put the parts in rows and a grid column instead, and the
    /// map got about 760 x 494 px of a 1440 x 960 window (#1494, #1498, #1500).
    /// Over 60rem the atlas's canvas fills the takeover; the header, the panel and
    /// the foot float above it, taking no pointer where they draw nothing, and
    /// the index floats down the right under the chips.</summary>
    [Fact]
    public void The_stylesheet_floats_every_part_over_a_full_bleed_map()
    {
        var css = AppCss();
        var wide = MediaBlock(css, "@media (width > 60rem) {");

        var graph = NestedRule(wide, ".lobes-atlas__graph {");
        Assert.Contains("position: absolute;", graph, StringComparison.Ordinal);
        Assert.Contains("inset: 0;", graph, StringComparison.Ordinal);
        // No size container, so no stacking context: the detail sheet inside the
        // atlas rises above every floating box, and fills the takeover.
        Assert.Contains("container-type: normal;", Rule(css, ".lobes-atlas__graph {"), StringComparison.Ordinal);
        Assert.Contains("contain: layout;", Rule(css, ".lobes-atlas {"), StringComparison.Ordinal);

        var canvas = NestedRule(wide, ".lobes-atlas__graph .graph-atlas__canvas {");
        Assert.Contains("position: absolute;", canvas, StringComparison.Ordinal);
        Assert.Contains("inset: 0;", canvas, StringComparison.Ordinal);

        var floating = NestedRule(wide, ".lobes-atlas__header,");
        Assert.Contains(".lobes-atlas__panel,", floating, StringComparison.Ordinal);
        Assert.Contains(".lobes-atlas__footer {", floating, StringComparison.Ordinal);
        Assert.Contains("position: absolute;", floating, StringComparison.Ordinal);
        Assert.Contains("z-index: 1;", floating, StringComparison.Ordinal);
        Assert.Contains("pointer-events: none;", floating, StringComparison.Ordinal);

        var controls = NestedRule(wide, ".lobes-atlas__scopes,");
        foreach (var part in new[] { ".lobes-atlas__tools,", ".lobes-atlas__chips,", ".lobes-atlas__sheet {" })
        {
            Assert.Contains(part, controls, StringComparison.Ordinal);
        }

        Assert.Contains("pointer-events: auto;", controls, StringComparison.Ordinal);

        var panel = NestedRule(wide, ".lobes-atlas__panel {");
        Assert.Contains("top: var(--lobes-atlas-left-top);", panel, StringComparison.Ordinal);
        Assert.Contains("width: var(--lobes-atlas-side);", panel, StringComparison.Ordinal);
        Assert.Contains("max-height: calc(100% - var(--lobes-atlas-left-top)", panel, StringComparison.Ordinal);

        var index = NestedRule(wide, ".lobes-atlas__graph .graph-atlas-index {");
        Assert.Contains("position: absolute;", index, StringComparison.Ordinal);
        Assert.Contains("top: var(--lobes-atlas-right-top);", index, StringComparison.Ordinal);
        Assert.Contains("right: var(--lobes-atlas-inset);", index, StringComparison.Ordinal);
        Assert.Contains("var(--lobes-atlas-foot)", index, StringComparison.Ordinal);
    }

    /// <summary>The floating parts are the takeover's own children, siblings of
    /// the atlas rather than inside its canvas — the renderer listens on the
    /// canvas alone, so a drag or a wheel on a control never reaches the camera
    /// (proved live, since bUnit runs no renderer). And they come in the order
    /// the eye meets them in both layouts, which is the order the keyboard
    /// does (accessibility.md, Logical order).</summary>
    [Fact]
    public void The_parts_are_siblings_of_the_atlas_in_the_order_the_eye_meets_them()
    {
        var component = Render("brain");
        component.WaitForAssertion(() => Assert.NotEmpty(component.FindAll("[data-testid='lobes-atlas-chip']")));

        var root = component.Find("[data-testid='lobes-atlas']");
        Assert.Equal(
            ["lobes-atlas__header", "lobes-atlas__panel", "graph-atlas", "lobes-atlas__footer"],
            root.Children.Select(child => child.ClassList.First()));
        Assert.Equal(
            ["lobes-atlas__lead", "lobes-atlas__controls"],
            root.QuerySelector(".lobes-atlas__header")!.Children.Select(child => child.ClassName));
    }

    /// <summary>At 390 px the map sat low under several rows of wrapped chips, and
    /// the foot overlapped the index list (#1500). Under 60rem nothing floats: the
    /// parts stack in one column, the chips scroll sideways in one row, the panel
    /// and the card fold into a sheet behind one disclosure, and the map takes
    /// what is left with its list under it.</summary>
    [Fact]
    public void The_stylesheet_stacks_a_narrow_window_in_one_column_with_the_panel_in_a_sheet()
    {
        var css = AppCss();
        var narrow = MediaBlock(css, "@media (width <= 60rem) {");

        Assert.DoesNotContain("position: absolute;", narrow, StringComparison.Ordinal);
        Assert.Contains("overflow-y: auto;", NestedRule(narrow, ".lobes-atlas {"), StringComparison.Ordinal);
        Assert.Contains("display: flex;", NestedRule(narrow, ".lobes-atlas__sheet-toggle {"), StringComparison.Ordinal);
        Assert.Contains("display: none;", NestedRule(narrow, ".lobes-atlas:not(.lobes-atlas--sheet-open) .lobes-atlas__sheet {"), StringComparison.Ordinal);
        Assert.Contains("flex: 1 0 26rem;", NestedRule(narrow, ".lobes-atlas__graph {"), StringComparison.Ordinal);
        Assert.Contains("grid-template-rows: minmax(12rem, 1fr) minmax(8rem, 0.75fr);", NestedRule(narrow, ".lobes-atlas__graph > .graph-atlas__stage {"), StringComparison.Ordinal);

        // Over the map the disclosure is not there at all.
        Assert.Contains("display: none;", Rule(css, ".lobes-atlas__sheet-toggle {"), StringComparison.Ordinal);

        var chips = Rule(css, ".lobes-atlas__chips {");
        Assert.Contains("flex-wrap: nowrap;", chips, StringComparison.Ordinal);
        Assert.Contains("overflow-x: auto;", chips, StringComparison.Ordinal);
        Assert.Contains("padding: var(--spacing-xs);", chips, StringComparison.Ordinal);
        Assert.Contains("flex: none;", Rule(css, ".lobes-atlas__chip {"), StringComparison.Ordinal);
    }

    [Fact]
    public void The_panels_disclosure_names_the_panel_and_the_card_and_opens_the_sheet()
    {
        var component = Render("repo:backlog");
        component.WaitForAssertion(() => Assert.NotEmpty(component.FindAll("[data-testid='lobes-atlas-sheet-toggle']")));

        var toggle = component.Find("[data-testid='lobes-atlas-sheet-toggle']");
        AssertRealButton(toggle);
        Assert.Equal("false", toggle.GetAttribute("aria-expanded"));
        Assert.Equal(component.Find("[data-testid='lobes-atlas-panel-sheet']").Id, toggle.GetAttribute("aria-controls"));
        Assert.Equal("Devbook folders backlog", Collapse(toggle.TextContent));
        Assert.DoesNotContain("lobes-atlas--sheet-open", component.Find("[data-testid='lobes-atlas']").ClassList);

        toggle.Click();

        component.WaitForAssertion(() =>
        {
            Assert.Equal("true", component.Find("[data-testid='lobes-atlas-sheet-toggle']").GetAttribute("aria-expanded"));
            Assert.Contains("lobes-atlas--sheet-open", component.Find("[data-testid='lobes-atlas']").ClassList);
        });

        // The panel and the card are in the sheet, so the sheet is what folds.
        var sheet = component.Find("[data-testid='lobes-atlas-panel-sheet']");
        Assert.NotNull(sheet.QuerySelector("[data-testid='lobes-atlas-rows']"));
        Assert.NotNull(sheet.QuerySelector("[data-testid='lobes-atlas-card']"));
    }

    /// <summary>The index's mark for a Brain note wears the Brain's accent, as the
    /// mark on the map does.</summary>
    [Fact]
    public void The_component_stylesheet_paints_a_brain_mark_in_the_brains_accent()
    {
        var css = File.ReadAllText(RepositoryRoot.File("src", "Core", "Backlog.UI.Components", "wwwroot", "components.css")).Replace("\r\n", "\n");

        Assert.Contains(
            ".graph-atlas-index__mark[data-tone=\"brain\"] { background: var(--color-band-2); }",
            css,
            StringComparison.Ordinal);
    }

    [Fact]
    public void A_change_reads_the_atlas_again()
    {
        var component = Render();
        component.WaitForAssertion(() => Assert.Equal(4, component.FindAll("[data-testid='lobes-atlas-scope']").Count));

        var graph = SampleGraph();
        _lobes.Graph = graph with { Scopes = [.. graph.Scopes.Where(scope => scope.Kind != DevbookLobesScopeKind.Ghost)] };
        _lobes.RaiseChanged();

        component.WaitForAssertion(() => Assert.Equal(3, component.FindAll("[data-testid='lobes-atlas-scope']").Count));
    }

    /// <summary>On a ghost when its alias gets registered — the devbook built into
    /// the graph after the reader came back — the reread moves to that devbook
    /// rather than dropping to All.</summary>
    [Fact]
    public void A_reread_after_the_ghosts_alias_is_registered_moves_to_its_devbook()
    {
        var component = Render("ghost:devbook-stack");
        component.WaitForAssertion(() => Assert.Equal("ghost:devbook-stack", LastModel().Layout));

        var graph = SampleGraph();
        _lobes.Graph = graph with
        {
            Scopes = [.. graph.Scopes.Select(scope => scope.Kind == DevbookLobesScopeKind.Ghost
                ? scope with { Key = "repo:devbook-stack", Kind = DevbookLobesScopeKind.Devbook }
                : scope)]
        };
        _lobes.RaiseChanged();

        component.WaitForAssertion(() => Assert.Equal("true", Scope(component, "repo:devbook-stack").GetAttribute("aria-pressed")));
    }

    // ----- Fixtures ------------------------------------------------------------------

    private IRenderedComponent<LobesAtlasTakeover> Render(
        string? scope = null,
        Action<string>? onOpenNote = null,
        Action<BrainRepoLink>? onOpenChapter = null) =>
        _context.Render<LobesAtlasTakeover>(parameters => parameters
            .Add(atlas => atlas.Scope, scope)
            .Add(atlas => atlas.OnOpenNote, (string path) => onOpenNote?.Invoke(path))
            .Add(atlas => atlas.OnOpenRepositoryChapter, (BrainRepoLink link) => onOpenChapter?.Invoke(link)));

    /// <summary>A real, named button: the element, not a div made to act as one.</summary>
    private static void AssertRealButton(AngleSharp.Dom.IElement element)
    {
        Assert.Equal("BUTTON", element.TagName);
        Assert.Equal("button", element.GetAttribute("type"));
    }

    private static void AssertEveryControlIsANamedButton(IEnumerable<AngleSharp.Dom.IElement> controls)
    {
        Assert.NotEmpty(controls);
        Assert.All(controls, control =>
        {
            AssertRealButton(control);
            Assert.False(string.IsNullOrWhiteSpace(control.GetAttribute("aria-label") ?? control.TextContent), "Every control has a name.");
        });
    }

    private static string Collapse(string text) => string.Join(' ', text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

    private static string AxisEnd(IRenderedComponent<LobesAtlasTakeover> component, string axis)
    {
        var line = component.Find($"[data-testid='lobes-atlas-orientation'] line[data-axis='{axis}']");
        return $"{line.GetAttribute("x2")},{line.GetAttribute("y2")}";
    }

    /// <summary>The rule whose selector list opens with <paramref name="opening"/>,
    /// selectors and body.</summary>
    private static string Rule(string css, string opening)
    {
        var start = css.IndexOf("\n" + opening, StringComparison.Ordinal);
        Assert.True(start >= 0, $"{opening} should exist.");
        return css[start..css.IndexOf('}', start)];
    }

    private static string AppCss() =>
        File.ReadAllText(RepositoryRoot.File("src", "App", "Backlog.Desktop.UI", "wwwroot", "app.css")).Replace("\r\n", "\n");

    /// <summary>The body of the atlas's own top-level at-rule that opens with
    /// <paramref name="opening"/>, up to its closing brace in the first column:
    /// the first such block that styles the atlas, since other screens share the
    /// same breakpoint.</summary>
    private static string MediaBlock(string css, string opening)
    {
        for (var start = css.IndexOf("\n" + opening, StringComparison.Ordinal);
             start >= 0;
             start = css.IndexOf("\n" + opening, start + 1, StringComparison.Ordinal))
        {
            var block = css[start..css.IndexOf("\n}", start, StringComparison.Ordinal)];
            if (block.Contains("\n    .lobes-atlas", StringComparison.Ordinal)) return block;
        }

        Assert.Fail($"{opening} should open a block that styles the atlas.");
        return string.Empty;
    }

    /// <summary>A rule one level into an at-rule's body, found as
    /// <see cref="Rule"/> finds a top-level one.</summary>
    private static string NestedRule(string block, string opening)
    {
        var start = block.IndexOf("\n    " + opening, StringComparison.Ordinal);
        Assert.True(start >= 0, $"{opening} should exist in the block.");
        return block[start..block.IndexOf('}', start)];
    }

    private static AngleSharp.Dom.IElement Scope(IRenderedComponent<LobesAtlasTakeover> component, string key) =>
        component.Find($"[data-testid='lobes-atlas-scope'][data-scope='{key}']");

    /// <summary>The model the renderer was last handed.</summary>
    private LobesAtlasModel LastModel() =>
        Assert.IsType<LobesAtlasModel>(_context.JSInterop.Invocations
            .Where(invocation => invocation.Identifier == RenderFunction)
            .Last()
            .Arguments[2]);

    private static DevbookLobesGraph SampleGraph()
    {
        const string brain = "brain";
        const string backlog = "repo:backlog";
        const string stack = "ghost:devbook-stack";

        DevbookLobesNode[] nodes =
        [
            new($"{brain}|projects/plan.md", "Plan", brain, DevbookLobe.Frontal, DevbookLobesNodeKind.Note, "projects/plan.md"),
            new($"{brain}|people/ann.md", "Ann", brain, DevbookLobe.Temporal, DevbookLobesNodeKind.Note, "people/ann.md"),
            new($"{brain}|index:CONVENTIONS.md", "CONVENTIONS.md", brain, DevbookLobe.Stem, DevbookLobesNodeKind.IndexHub, string.Empty),
            new($"{brain}|hub:{backlog}", "backlog", brain, DevbookLobe.Occipital, DevbookLobesNodeKind.RepositoryHub, string.Empty),
            new($"{brain}|hub:{stack}", "devbook-stack", brain, DevbookLobe.Occipital, DevbookLobesNodeKind.RepositoryHub, string.Empty, Dashed: true),
            new($"{backlog}|.devbook/arc42/01-introduction.md", "Introduction", backlog, DevbookLobe.Frontal, DevbookLobesNodeKind.Chapter, ".devbook/arc42/01-introduction.md"),
            new($"{backlog}|.devbook/arc42/adr/0001-use-sqlite.md", "Use SQLite", backlog, DevbookLobe.Frontal, DevbookLobesNodeKind.Chapter, ".devbook/arc42/adr/0001-use-sqlite.md",
                RepositoryLink: new DevbookAtlasRepositoryLink("backlog", "repo:backlog/.devbook/arc42/adr/0001-use-sqlite.md", true, null)),
            new($"{backlog}|.devbook/tech/docker.md", "Docker", backlog, DevbookLobe.Parietal, DevbookLobesNodeKind.Chapter, ".devbook/tech/docker.md"),
            new($"{stack}|ghost:tech/radar.md", "tech/radar.md", stack, DevbookLobe.Parietal, DevbookLobesNodeKind.GhostChapter, "tech/radar.md", true,
                new DevbookAtlasRepositoryLink("devbook-stack", "repo:devbook-stack/tech/radar.md", false, BrainRepoLinkResolver.NotOnThisMachine))
        ];

        DevbookLobesEdge[] edges =
        [
            new($"{brain}|projects/plan.md", $"{brain}|people/ann.md", "related"),
            new($"{brain}|hub:{backlog}", $"{backlog}|.devbook/arc42/adr/0001-use-sqlite.md", DevbookLobesReader.BundleEdgeKind),
            new($"{brain}|hub:{stack}", $"{stack}|ghost:tech/radar.md", DevbookLobesReader.BundleEdgeKind, Dashed: true)
        ];

        DevbookLobesScope Summary(string key, string label, DevbookLobesScopeKind kind, string? alias, int linking) =>
            new(key, label, kind, alias, kind != DevbookLobesScopeKind.Ghost, null,
                Enum.GetValues<DevbookLobe>().ToDictionary(lobe => lobe, lobe => nodes.Count(node => node.ScopeKey == key && node.Lobe == lobe)),
                nodes.Count(node => node.ScopeKey == key && node.Kind is DevbookLobesNodeKind.Note or DevbookLobesNodeKind.Chapter or DevbookLobesNodeKind.GhostChapter),
                linking);

        return new DevbookLobesGraph(
            true,
            null,
            [
                Summary(brain, "Brain", DevbookLobesScopeKind.Brain, null, 1),
                Summary(backlog, "backlog", DevbookLobesScopeKind.Devbook, "backlog", 1),
                Summary(stack, "devbook-stack", DevbookLobesScopeKind.Ghost, "devbook-stack", 1)
            ],
            nodes,
            edges);
    }

    public void Dispose() => _context.Dispose();

    /// <summary>A Lobes source over a graph the test sets.</summary>
    internal sealed class FixedLobes(DevbookLobesGraph graph) : IDevbookLobesSource
    {
        public DevbookLobesGraph Graph { get; set; } = graph;

        public bool CanBuildBrainIndex { get; set; } = true;

        /// <summary>What a build answers: null when it built.</summary>
        public string? BuildProblem { get; set; }

        /// <summary>What a build does to the graph, if anything.</summary>
        public Action? OnBuild { get; set; }

        public int Builds { get; private set; }

        public event Action? Changed;

        public Task<DevbookLobesGraph> ReadAsync(CancellationToken cancellationToken = default) => Task.FromResult(Graph);

        /// <summary>A build that has not finished yet, when set: the build
        /// answers what it completes with.</summary>
        public Task<string?>? Pending { get; set; }

        public async Task<string?> BuildBrainIndexAsync(CancellationToken cancellationToken = default)
        {
            Builds++;
            OnBuild?.Invoke();
            return Pending is { } pending ? await pending : BuildProblem;
        }

        public void RaiseChanged() => Changed?.Invoke();
    }

    /// <summary>The sample graph, for the shell and pane tests.</summary>
    internal static DevbookLobesGraph Sample() => SampleGraph();
}
