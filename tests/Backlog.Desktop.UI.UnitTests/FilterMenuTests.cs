using Backlog.Modules.Tasks.Abstractions.Connectors;
using Bunit;
using Microsoft.AspNetCore.Components.Web;

namespace Backlog.Desktop.UI.UnitTests;

/// <summary>
/// The narrow list column's Filter menu: below the 48rem step the Source and Type
/// groups leave the bar for one disclosure (<c>interaction-guidelines.md#popover-button</c>),
/// and at or above it they sit inline with no menu. One form is rendered at a time,
/// on components.js's word that the column crossed the step — which a test gives by
/// calling the pane's <c>ListNarrowChangedAsync</c> as the script would.
/// </summary>
[Collection(WorkspaceSettingsCollection.Name)]
public sealed class FilterMenuTests
{
    private const string Trigger = "[data-testid='filter-menu-button']";
    private const string Panel = "[data-testid='filter-menu-panel']";
    private const string SourceChip = "[data-testid='source-filter-option']";
    private const string TypeChip = "[data-testid='type-filter-option']";

    /// <summary>A host with two sources and two types of open work, so both groups
    /// are drawn.</summary>
    private static async Task<TasksPaneHost> BothGroupsAsync()
    {
        var sources = new LinkedTaskSources([new LinkedTaskPaneTests.FakeConnector()], new LinkedTaskPaneTests.InMemoryTargets());
        var host = await TasksPaneHost.CreateAsync(roadmapTags: null, [], linkedSources: sources);

        await host.WriteEntryAsync("# Draft the brief\n`prompt` `!ready`\n");
        var linked = await host.WriteEntryAsync("# Ship the installer\n`task` `!ready`\n");
        await host.State.SelectAsync(null);

        var repository = TasksTestHost.RepositoryFor(new WorkspaceSettingsStore(host.StorageRoot, Path.Combine(host.StorageRoot, "elsewhere.json")));
        var task = await repository.GetAsync(linked.Id!.Value, TestContext.Current.CancellationToken);
        task!.SetSourceRef(new SourceRef(LinkedTaskPaneTests.FakeConnector.Id, "owner/repo", "fake-#1", "https://example.com/fake/1", "#1", null, "open",
            new DateTimeOffset(2026, 10, 1, 9, 0, 0, TimeSpan.Zero)));
        await repository.SaveAsync(task, TestContext.Current.CancellationToken);
        await host.State.ReloadFromStoreAsync();

        return host;
    }

    private static Task NarrowAsync(IRenderedComponent<TasksPane> pane, bool narrow = true) =>
        pane.InvokeAsync(() => pane.Instance.ListNarrowChangedAsync(narrow));

    [Fact]
    public async Task A_wide_column_draws_source_and_type_inline_and_no_filter_menu()
    {
        using var host = await BothGroupsAsync();

        var pane = host.Render();

        Assert.Empty(pane.FindAll(Trigger));
        Assert.NotNull(pane.Find(".filter-bar > .filter-group--source[role='group'][aria-label='Filter by source']"));
        Assert.NotNull(pane.Find(".filter-bar > .filter-group--type[role='group'][aria-label='Filter by type']"));
    }

    [Fact]
    public async Task A_narrow_column_folds_source_and_type_into_one_named_filter_disclosure()
    {
        using var host = await BothGroupsAsync();

        var pane = host.Render();
        await NarrowAsync(pane);

        Assert.Empty(pane.FindAll(".filter-bar > .filter-group--source"));
        Assert.Empty(pane.FindAll(".filter-bar > .filter-group--type"));
        Assert.Empty(pane.FindAll(SourceChip));

        var trigger = pane.Find(Trigger);
        Assert.Equal("Filter", trigger.GetAttribute("aria-label"));
        Assert.Equal("Filter by source and type", trigger.GetAttribute("title"));
        Assert.Equal("false", trigger.GetAttribute("aria-expanded"));

        // The word and the caret are the face, hidden; the name is on the button.
        var face = trigger.QuerySelector(".popover-button__glyph")!;
        Assert.Equal("true", face.GetAttribute("aria-hidden"));
        Assert.NotNull(face.QuerySelector("svg.filter-menu__caret"));
        Assert.Null(face.QuerySelector(".chip__count"));

        await trigger.ClickAsync(new());

        var panel = pane.Find(Panel);
        Assert.Equal("group", panel.GetAttribute("role"));
        Assert.Equal(panel.Id, pane.Find(Trigger).GetAttribute("aria-controls"));
        Assert.NotNull(panel.QuerySelector(".filter-group--source[role='group'][aria-label='Filter by source']"));
        Assert.NotNull(panel.QuerySelector(".filter-group--type[role='group'][aria-label='Filter by type']"));
        Assert.All(panel.QuerySelectorAll($"{SourceChip}, {TypeChip}"), chip => Assert.Equal("false", chip.GetAttribute("aria-pressed")));
    }

    /// <summary>Pressing a chip in the panel narrows the list and leaves the panel
    /// open; the trigger then reads pressed and counts what is pressed in it, so the
    /// narrowed list still says so with the menu shut.</summary>
    [Fact]
    public async Task Pressing_a_chip_in_the_menu_filters_keeps_it_open_and_marks_the_trigger()
    {
        using var host = await BothGroupsAsync();

        var pane = host.Render();
        await NarrowAsync(pane);
        await pane.Find(Trigger).ClickAsync(new());

        await pane.Find($"{TypeChip}[data-type='prompt']").ClickAsync(new());

        Assert.Equal(EntryType.Prompt, host.State.SelectedType);
        Assert.NotNull(pane.Find(Panel));
        Assert.Equal("true", pane.Find($"{TypeChip}[data-type='prompt']").GetAttribute("aria-pressed"));

        var trigger = pane.Find(Trigger);
        Assert.Contains("chip--active", trigger.ClassList);
        Assert.Equal("1", trigger.QuerySelector(".chip__count")!.TextContent.Trim());
        Assert.Equal("Filter by source and type — 1 filter on", trigger.GetAttribute("title"));

        await pane.Find(Panel).KeyDownAsync(new KeyboardEventArgs { Key = "Escape" });

        Assert.Empty(pane.FindAll(Panel));
        Assert.Contains("chip--active", pane.Find(Trigger).ClassList);
    }

    /// <summary>
    /// The column is mostly narrow because an entry is open beside it, and a chip
    /// pressed in the menu can filter that entry out: the selection goes, the split
    /// closes and the script reports the column wide. The menu the reader is working
    /// in stays — the wide report waits for it to close, and lands then.
    /// </summary>
    [Fact]
    public async Task A_wide_report_while_the_menu_is_open_waits_until_it_closes()
    {
        using var host = await BothGroupsAsync();
        await host.State.SelectAsync(host.State.Rows.Single(row => row.SourceRef is not null));

        var pane = host.Render();
        await NarrowAsync(pane);
        await pane.Find(Trigger).ClickAsync(new());

        await pane.Find($"{TypeChip}[data-type='prompt']").ClickAsync(new());
        Assert.Null(host.State.SelectedRow);

        // What the script says once the split has closed and the column widened.
        await NarrowAsync(pane, narrow: false);

        Assert.NotNull(pane.Find(Panel));
        Assert.Equal("true", pane.Find($"{Panel} {TypeChip}[data-type='prompt']").GetAttribute("aria-pressed"));
        Assert.Empty(pane.FindAll(".filter-bar > .filter-group--type"));

        await pane.Find(Panel).KeyDownAsync(new KeyboardEventArgs { Key = "Escape" });

        Assert.Empty(pane.FindAll(Trigger));
        Assert.Equal("true", pane.Find($".filter-bar > .filter-group--type {TypeChip}[data-type='prompt']").GetAttribute("aria-pressed"));
    }

    /// <summary>
    /// Escape on a menu that is holding a wide report: the focus goes back to the
    /// trigger while the trigger is still on the bar, and once the bar has gone wide
    /// it is handed to the chip the reader pressed, now inline — never to the page's
    /// body. The unwatch call is held open here so the close reaches the host in its
    /// own turn, as it does across the interop round trip in the app: the bar then
    /// redraws before anything after that word runs, which is where a focus sent
    /// late found no trigger.
    /// </summary>
    [Fact]
    public async Task Escape_on_a_held_menu_hands_the_focus_on_before_and_after_the_bar_goes_wide()
    {
        using var host = await BothGroupsAsync();
        await host.State.SelectAsync(host.State.Rows.Single(row => row.SourceRef is not null));

        IRenderedComponent<TasksPane>? pane = null;
        var focusSentToATriggerOnTheBar = new List<bool>();
        host.Context.JSInterop.SetupVoid("Blazor._internal.domWrapper.focus", _ =>
        {
            focusSentToATriggerOnTheBar.Add(pane!.FindAll(Trigger).Count == 1);
            return true;
        }).SetVoidResult();

        pane = host.Render();
        await NarrowAsync(pane);
        await pane.Find(Trigger).ClickAsync(new());
        await pane.Find($"{TypeChip}[data-type='prompt']").ClickAsync(new());
        await NarrowAsync(pane, narrow: false);

        var unwatch = host.Context.JSInterop.SetupVoid("backlogPopoverOutside.unwatch", _ => true);
        pane.Find(Panel).KeyDown(new KeyboardEventArgs { Key = "Escape" });
        await pane.InvokeAsync(unwatch.SetVoidResult);

        pane.WaitForAssertion(() => Assert.Empty(pane.FindAll(Trigger)));
        Assert.Equal([true], focusSentToATriggerOnTheBar);

        var chipId = TasksPane.TypeFilterChipId(EntryType.Prompt);
        pane.WaitForAssertion(() =>
        {
            var handOn = Assert.Single(host.Context.JSInterop.Invocations["backlogFocusIfLost"]);
            Assert.Equal(chipId, handOn.Arguments[0]);
            Assert.Equal("[data-testid='filter-menu']", handOn.Arguments[1]);
        });
        Assert.Equal("true", pane.Find($".filter-bar > .filter-group--type #{chipId}").GetAttribute("aria-pressed"));
    }

    /// <summary>
    /// The column is narrow because an entry is open beside it, so the menu is used
    /// beside an open entry: pressing its trigger, and a chip in it that keeps the
    /// entry in view, is the reader narrowing the list, not leaving the entry. The
    /// stand-in for <c>backlogFocusOutside</c> answers against this render's own DOM
    /// with the pressed control as the focused element, so it is the selectors the
    /// pane sends that decide.
    /// </summary>
    [Fact]
    public async Task Using_the_menu_beside_an_open_entry_keeps_the_entry_open()
    {
        using var host = await BothGroupsAsync();
        var entry = host.State.Rows.Single(row => row.SourceRef is not null);
        await host.State.SelectAsync(entry);

        var pane = host.Render();
        await NarrowAsync(pane);

        var focused = Trigger;
        bool FocusIsOutside(JSRuntimeInvocation invocation) =>
            !invocation.Arguments.Cast<string>().Any(selector =>
                pane.FindAll(selector).Any(region => region.Matches(focused) || region.QuerySelector(focused) is not null));

        host.Context.JSInterop.Setup<bool>("backlogFocusOutside", FocusIsOutside).SetResult(true);
        host.Context.JSInterop.Setup<bool>("backlogFocusOutside", invocation => !FocusIsOutside(invocation)).SetResult(false);

        await pane.Find(Trigger).ClickAsync(new());
        await pane.Find("[data-testid='backlog-pane']").TriggerEventAsync("onfocusout", new FocusEventArgs());

        Assert.Equal(entry.Id, host.State.SelectedRow?.Id);

        focused = $"{Panel} {SourceChip}[data-source='{LinkedTaskPaneTests.FakeConnector.Id}']";
        await pane.Find(focused).ClickAsync(new());
        await pane.Find("[data-testid='backlog-pane']").TriggerEventAsync("onfocusout", new FocusEventArgs());

        Assert.Equal(LinkedTaskPaneTests.FakeConnector.Id, host.State.SelectedSource);
        Assert.Equal(entry.Id, host.State.SelectedRow?.Id);
        Assert.NotNull(pane.Find(Panel));
    }

    /// <summary>A column that went wide and came back while the menu was open is
    /// narrow again, so closing the menu leaves the narrow bar.</summary>
    [Fact]
    public async Task Closing_the_menu_applies_the_last_report_not_the_first()
    {
        using var host = await BothGroupsAsync();

        var pane = host.Render();
        await NarrowAsync(pane);
        await pane.Find(Trigger).ClickAsync(new());

        await NarrowAsync(pane, narrow: false);
        await NarrowAsync(pane);
        await pane.Find(Trigger).ClickAsync(new());

        Assert.Empty(pane.FindAll(Panel));
        Assert.NotNull(pane.Find(Trigger));
        Assert.Empty(pane.FindAll(".filter-bar > .filter-group--source"));
    }

    [Fact]
    public async Task Widening_the_column_again_puts_the_groups_back_on_the_bar()
    {
        using var host = await BothGroupsAsync();

        var pane = host.Render();
        await NarrowAsync(pane);
        await NarrowAsync(pane, narrow: false);

        Assert.Empty(pane.FindAll(Trigger));
        Assert.NotNull(pane.Find(".filter-bar > .filter-group--source"));
    }

    /// <summary>A menu with nothing in it is a dead control, so with neither group
    /// drawn there is no trigger either.</summary>
    [Fact]
    public async Task With_no_source_or_type_group_a_narrow_column_draws_no_filter_menu()
    {
        using var host = await TasksPaneHost.CreateAsync();
        await host.WriteEntryAsync("# Water the plants\n`task` `!ready`\n");

        var pane = host.Render();
        await NarrowAsync(pane);

        Assert.Empty(pane.FindAll(Trigger));
    }
}
