using Backlog.Infrastructure.GitHub;
using Backlog.UI.Components.Diagrams.C4;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;

namespace Backlog.Desktop.UI.UnitTests;

/// <summary>
/// The Architecture pane as one surface while <c>c4-zoom</c> is on (plan
/// c4-zoom-navigation, entry 15b): no header row — the find field heads the left
/// navigation — the zoom bar always there, the left navigation — the C4 views, the chapters that
/// document the open view, the records — and a chapter, an ADR or a TDR opened inside
/// the surface with its own left list and right-hand panel.
/// <para>
/// The folder: chapters 01, 05, 09 and 11; ADR 0003, 0005, 0006 (proposed) and a
/// guideline; TDR 0001 on the mobile app; a workspace with a context and a container
/// view; the views map saying chapter 05 documents the containers view.
/// </para>
/// </summary>
public sealed class Arc42SurfaceTests : IDisposable
{
    private readonly List<string> _roots = [];

    private const string ChapterPath = ".arc42/05-building-block-view.md";

    // ---- the pure reads --------------------------------------------------------

    [Fact]
    public async Task The_chapter_list_is_the_numbered_chapters_with_their_badges()
    {
        var (catalog, c4) = await LoadAsync();

        var chapters = Arc42Surface.Chapters(catalog, c4);

        Assert.Equal(["01", "05", "09", "11"], chapters.Select(chapter => chapter.Number));
        Assert.Equal("Building Block View", chapters[1].Title);
        Assert.Equal(1, chapters[1].Views);
        Assert.Equal(3, chapters[2].Decisions);
        Assert.Equal(1, chapters[3].Debt);
        Assert.All(chapters.Where(chapter => chapter.Number is not "09"), chapter => Assert.Equal(0, chapter.Decisions));
    }

    [Fact]
    public async Task The_decision_log_lists_the_local_records_first_with_the_proposed_marked()
    {
        var (catalog, _) = await LoadAsync();

        var decisions = Arc42Surface.Decisions(catalog);

        Assert.Equal(["0003", "0005", "0006", "0004"], decisions.Select(record => record.Number));
        Assert.True(decisions.Single(record => record.Number == "0006").Proposed);
        Assert.Equal("proposed", decisions.Single(record => record.Number == "0006").Note);
        Assert.True(decisions[^1].Guideline);

        // A record with no state stored stands: accepted, as the log's filter counts it.
        Assert.Equal([true, true, false, true], decisions.Select(record => record.Accepted));

        var debt = Assert.Single(Arc42Surface.Debt(catalog));
        Assert.Equal("High · planned", debt.Note);
    }

    [Fact]
    public async Task A_chapters_links_are_its_views_the_decisions_citing_it_and_the_debt_on_its_elements()
    {
        var (catalog, c4) = await LoadAsync();
        var chapter = catalog.Documents.Single(document => document.Path.EndsWith("05-building-block-view.md", StringComparison.Ordinal));

        var links = Arc42Surface.ChapterLinks(catalog, c4, chapter);

        var view = Assert.Single(links.Views);
        Assert.Equal("containers-backlog", view.View.Key);
        Assert.Equal("Container View", view.Section);

        // 0005 is named by the chapter's related list; 0006 names the chapter.
        Assert.Equal(["0005", "0006"], links.Decisions.Select(record => record.Number));

        // TDR 0001 is on the mobile app, which the containers view draws.
        Assert.Equal(["0001"], links.Debt.Select(record => record.Number));
    }

    [Fact]
    public async Task Find_offers_elements_chapters_and_records_by_name_or_number()
    {
        var (catalog, c4) = await LoadAsync();
        var workspace = c4.Workspaces[0].Workspace;

        var mobile = Assert.Single(Arc42Surface.Find(catalog, workspace, "mobile"), hit => hit.Kind == Arc42FindKind.Element);
        Assert.Equal("backlog.mobile", mobile.Key);
        Assert.NotNull(mobile.ViewKey);

        Assert.Contains(Arc42Surface.Find(catalog, workspace, "building"), hit => hit.Kind == Arc42FindKind.Chapter);
        Assert.Contains(Arc42Surface.Find(catalog, workspace, "ADR 0005"), hit => hit.Kind == Arc42FindKind.Decision && hit.Key.EndsWith("0005-replica.md", StringComparison.Ordinal));
        Assert.Contains(Arc42Surface.Find(catalog, workspace, "offline"), hit => hit.Kind == Arc42FindKind.Debt);
        Assert.Empty(Arc42Surface.Find(catalog, workspace, "m"));
    }

    // ---- the surface, rendered --------------------------------------------------

    [Fact]
    public async Task The_surface_opens_on_the_model_with_the_header_the_bar_and_the_left_navigation()
    {
        await using var harness = CreateHarness();
        var component = harness.Render(null);
        Settle(component);

        // No breadcrumb and no header row: the find field heads the left navigation.
        Assert.Empty(component.FindAll("[data-testid='arc42-surface-crumb']"));
        Assert.Empty(component.FindAll("[data-testid='arc42-surface-header']"));
        Assert.NotNull(component.Find(".arc42-surface__side > [data-testid='arc42-surface-find-box'] + [data-testid='arc42-surface-nav']"));
        Assert.Equal("Find element, chapter, ADR, TDR", component.Find("[data-testid='arc42-surface-find'] input").GetAttribute("placeholder"));
        component.WaitForAssertion(() => Assert.Equal("System context", OpenLine(component)));
        Assert.Single(component.FindAll("[data-testid='c4-zoom-bar']"));
        Assert.Equal("model", component.Find("[data-testid='arc42-surface-nav']").GetAttribute("data-mode"));

        var views = component.FindAll("[data-testid='arc42-surface-view']");
        Assert.Equal(["Systemcontext", "Containers5"], views.Select(view => string.Concat(view.TextContent.Where(character => !char.IsWhiteSpace(character)))));
        Assert.Equal("page", views[0].GetAttribute("aria-current"));

        // Short, level-based names; the view's own title is the tooltip.
        Assert.Equal("System Context", views[0].GetAttribute("title"));
        Assert.Equal("Container Diagram", views[1].GetAttribute("title"));

        Assert.Equal("3 · 1 proposed", component.Find("[data-testid='arc42-surface-decisions-count']").TextContent);
        Assert.Equal("1", component.Find("[data-testid='arc42-surface-debt-count']").TextContent);
        Assert.Equal("All 4 chapters", component.Find("[data-testid='arc42-surface-all-chapters']").TextContent);
    }

    [Fact]
    public async Task A_view_line_opens_its_view_and_lists_the_chapters_that_document_it()
    {
        await using var harness = CreateHarness();
        var component = harness.Render(null);
        Settle(component);

        Assert.Single(component.FindAll("[data-testid='arc42-surface-documents-none']"));

        await component.FindAll("[data-testid='arc42-surface-view']").Single(view => view.GetAttribute("data-view") == "containers-backlog").ClickAsync(new());

        component.WaitForAssertion(() =>
        {
            Assert.Equal("containers-backlog", component.Find("[data-testid='c4-zoom-key']").TextContent);
            var document = Assert.Single(component.FindAll("[data-testid='arc42-surface-document']"));
            Assert.Contains("05", document.TextContent, StringComparison.Ordinal);
            Assert.Contains("documented by 05", component.Find("[data-testid='arc42-surface-documented-by']").TextContent, StringComparison.Ordinal);
            Assert.Equal("Containers", OpenLine(component));
            Assert.Equal("Container Diagram", component.Find("[data-testid='c4-zoom-title']").TextContent);
        });
    }

    [Fact]
    public async Task A_chapter_opens_inside_the_surface_with_its_list_and_its_links_and_Back_returns_to_the_model()
    {
        await using var harness = CreateHarness();
        var asked = new List<DevbookChapterLink>();
        var component = harness.Render(null, asked);
        Settle(component);

        await component.Find("[data-testid='arc42-surface-all-chapters']").ClickAsync(new());

        component.WaitForAssertion(() => Assert.Contains("Goals.", component.Find("[data-testid='arc42-document']").TextContent, StringComparison.Ordinal));
        Assert.Contains(asked, link => link.Path.EndsWith("01-introduction.md", StringComparison.Ordinal));

        // On the context view no chapter documents the view, so the first chapter opens;
        // pick 05 from the chapter list.
        Assert.Equal("chapters", component.Find("[data-testid='arc42-surface-nav']").GetAttribute("data-mode"));
        await component.FindAll("[data-testid='arc42-surface-chapter']").Single(chapter => chapter.TextContent.Contains("05", StringComparison.Ordinal)).ClickAsync(new());

        component.WaitForAssertion(() =>
        {
            Assert.Contains("05", OpenLine(component), StringComparison.Ordinal);
            var open = component.FindAll("[data-testid='arc42-surface-chapter']").Single(chapter => chapter.GetAttribute("aria-current") == "page");
            Assert.Contains("C4 1", open.TextContent, StringComparison.Ordinal);
            Assert.Single(component.FindAll("[data-testid='arc42-chapter-links']"));
            Assert.Single(component.FindAll("[data-testid='arc42-chapter-links-view']"));
            Assert.Equal(2, component.FindAll("[data-testid='arc42-chapter-links-adr']").Count);
            Assert.Single(component.FindAll("[data-testid='arc42-chapter-links-tdr']"));
        });

        // The bar stays, with no step current, and the picture is gone.
        Assert.Single(component.FindAll("[data-testid='c4-zoom-bar']"));
        Assert.Empty(component.FindAll("[data-testid='c4-zoom-frame']"));
        Assert.Empty(component.FindAll("[data-testid='c4-zoom-step'][aria-current]"));
        Assert.Equal("ADR", component.Find("[data-testid='arc42-surface-chapter-adr']").TextContent.Split(' ')[^1]);

        await component.Find("[data-testid='arc42-surface-back']").ClickAsync(new());

        component.WaitForAssertion(() =>
        {
            Assert.Empty(component.FindAll("[data-testid='arc42-document']"));
            Assert.Single(component.FindAll("[data-testid='c4-zoom-frame']"));
            Assert.Equal("model", component.Find("[data-testid='arc42-surface-nav']").GetAttribute("data-mode"));
        });
    }

    [Fact]
    public async Task A_view_the_chapter_documents_opens_in_the_picture()
    {
        await using var harness = CreateHarness();
        var component = harness.Render(ChapterPath);
        Settle(component);

        component.WaitForAssertion(() => Assert.Single(component.FindAll("[data-testid='arc42-chapter-links-view']")));
        await component.Find("[data-testid='arc42-chapter-links-view']").ClickAsync(new());

        component.WaitForAssertion(() =>
        {
            Assert.Empty(component.FindAll("[data-testid='arc42-document']"));
            Assert.Equal("containers-backlog", component.Find("[data-testid='c4-zoom-key']").TextContent);
        });
    }

    /// <summary>A view the chapter embeds inline (`![…](c4:key)`) is drawn once, and its
    /// way across names the C4 model — the surface has no C4 tab — and opens the
    /// picture at that view.</summary>
    [Fact]
    public async Task An_embedded_view_opens_in_the_picture_and_says_the_C4_model()
    {
        await using var harness = CreateHarness(embed: true);
        var component = harness.Render(ChapterPath);
        Settle(component);

        component.WaitForAssertion(() => Assert.Single(component.FindAll("[data-testid='devbook-c4-embed']")));
        var open = component.Find("[data-testid='devbook-c4-embed-open']");
        Assert.Equal("Open in the C4 model", open.TextContent.Trim());

        await open.ClickAsync(new());

        component.WaitForAssertion(() =>
        {
            Assert.Empty(component.FindAll("[data-testid='arc42-document']"));
            Assert.Equal("containers-backlog", component.Find("[data-testid='c4-zoom-key']").TextContent);
        });
    }

    [Fact]
    public async Task Decisions_opens_the_decisions_chapter_beside_the_decision_log_and_a_record_shows_where_it_lands()
    {
        await using var harness = CreateHarness();
        var component = harness.Render(null);
        Settle(component);

        await component.Find("[data-testid='arc42-surface-decisions']").ClickAsync(new());

        component.WaitForAssertion(() =>
        {
            Assert.Equal("decisions", component.Find("[data-testid='arc42-surface-nav']").GetAttribute("data-mode"));
            Assert.Contains("The decision log.", component.Find("[data-testid='arc42-document']").TextContent, StringComparison.Ordinal);
        });

        var records = component.FindAll("[data-testid='arc42-surface-record']");
        Assert.Equal(4, records.Count);
        Assert.Single(component.FindAll("[data-testid='arc42-surface-record-note']"));

        await records.Single(record => record.TextContent.Contains("0005", StringComparison.Ordinal)).ClickAsync(new());

        component.WaitForAssertion(() =>
        {
            Assert.Equal("decisions", component.Find("[data-testid='arc42-surface-nav']").GetAttribute("data-mode"));
            Assert.Single(component.FindAll("[data-testid='arc42-record-model']"));
            Assert.Equal("page", component.FindAll("[data-testid='arc42-surface-record']").Single(record => record.TextContent.Contains("0005", StringComparison.Ordinal)).GetAttribute("aria-current"));
        });
    }

    /// <summary>The design's decision log: All, Accepted or Proposed, each with its
    /// count, the list narrowed to the choice.</summary>
    [Fact]
    public async Task The_decision_log_filters_by_status()
    {
        await using var harness = CreateHarness();
        var component = harness.Render(null);
        Settle(component);

        await component.Find("[data-testid='arc42-surface-decisions']").ClickAsync(new());
        component.WaitForAssertion(() => Assert.Single(component.FindAll("[data-testid='arc42-surface-decision-filter']")));

        string[] Numbers() =>
        [
            .. component.FindAll("[data-testid='arc42-surface-record'] .arc42-surface-nav__number").Select(number => number.TextContent.Trim())
        ];

        var choices = component.FindAll("[data-testid='arc42-surface-decision-filter'] button");
        Assert.Equal(["All 3", "Accepted 2", "Proposed 1"], choices.Select(choice => choice.TextContent.Trim()));
        Assert.Equal("true", choices[0].GetAttribute("aria-checked"));
        Assert.Equal(["0003", "0005", "0006", "0004"], Numbers());

        await component.Find("[data-testid='arc42-surface-decision-filter-proposed']").ClickAsync(new());
        component.WaitForAssertion(() => Assert.Equal(["0006"], Numbers()));

        await component.Find("[data-testid='arc42-surface-decision-filter-accepted']").ClickAsync(new());
        component.WaitForAssertion(() => Assert.Equal(["0003", "0005", "0004"], Numbers()));
        Assert.Equal("true", component.Find("[data-testid='arc42-surface-decision-filter-accepted']").GetAttribute("aria-checked"));
    }

    [Fact]
    public async Task Technical_debt_opens_the_risks_chapter_beside_the_debt()
    {
        await using var harness = CreateHarness();
        var component = harness.Render(null);
        Settle(component);

        await component.Find("[data-testid='arc42-surface-debt']").ClickAsync(new());

        component.WaitForAssertion(() =>
        {
            Assert.Equal("debt", component.Find("[data-testid='arc42-surface-nav']").GetAttribute("data-mode"));
            Assert.Single(component.FindAll("[data-testid='arc42-surface-record']"));
        });

        await component.Find("[data-testid='arc42-surface-record']").ClickAsync(new());

        component.WaitForAssertion(() =>
        {
            Assert.Contains("0001", OpenLine(component), StringComparison.Ordinal);
            Assert.Contains("The debt.", component.Find("[data-testid='arc42-document']").TextContent, StringComparison.Ordinal);
        });
    }

    [Fact]
    public async Task A_bar_step_with_a_document_open_returns_to_the_model()
    {
        await using var harness = CreateHarness();
        var component = harness.Render(ChapterPath);
        Settle(component);

        component.WaitForAssertion(() => Assert.Single(component.FindAll("[data-testid='arc42-document']")));

        await component.FindAll("[data-testid='c4-zoom-step']").Single(step => step.GetAttribute("data-level") == "2").ClickAsync(new());

        component.WaitForAssertion(() =>
        {
            Assert.Empty(component.FindAll("[data-testid='arc42-document']"));
            Assert.Equal("containers-backlog", component.Find("[data-testid='c4-zoom-key']").TextContent);
        });
    }

    [Fact]
    public async Task Finding_an_element_opens_its_view_with_it_selected_and_finding_a_record_opens_it()
    {
        await using var harness = CreateHarness();
        var component = harness.Render(ChapterPath);
        Settle(component);

        var input = component.Find("[data-testid='arc42-surface-find'] input");
        await input.InputAsync(new ChangeEventArgs { Value = "Mobile" });
        var option = component.FindAll("[role='option']").First(candidate => candidate.TextContent.Contains("Mobile App", StringComparison.Ordinal));
        await option.ClickAsync(new());

        component.WaitForAssertion(() =>
        {
            Assert.Empty(component.FindAll("[data-testid='arc42-document']"));
            Assert.Equal("backlog.mobile", component.FindComponent<C4ZoomExplorer>().Instance.SelectedElementId);
        });

        input = component.Find("[data-testid='arc42-surface-find'] input");
        await input.InputAsync(new ChangeEventArgs { Value = "offline" });
        await component.FindAll("[role='option']").Single().ClickAsync(new());

        component.WaitForAssertion(() =>
        {
            Assert.Equal("debt", component.Find("[data-testid='arc42-surface-nav']").GetAttribute("data-mode"));
            Assert.Contains("The debt.", component.Find("[data-testid='arc42-document']").TextContent, StringComparison.Ordinal);
        });
    }

    /// <summary>A views map that still names a chapter since renamed: its line is
    /// offered, and pressing it leaves the picture up rather than showing whichever
    /// chapter was selected under the other one's name.</summary>
    [Fact]
    public async Task A_chapter_the_views_map_names_but_the_folder_lacks_leaves_the_picture_up()
    {
        const string Stale = """
            {
              "views": {
                "containers-backlog": [".arc42/06-renamed-away.md"]
              }
            }
            """;

        await using var harness = CreateHarness(references: Stale);
        var component = harness.Render(null);
        Settle(component);

        await component.FindAll("[data-testid='arc42-surface-view']").Single(view => view.GetAttribute("data-view") == "containers-backlog").ClickAsync(new());
        component.WaitForAssertion(() => Assert.Single(component.FindAll("[data-testid='arc42-surface-document']")));

        await component.Find("[data-testid='arc42-surface-document']").ClickAsync(new());

        Assert.Empty(component.FindAll("[data-testid='arc42-document']"));
        Assert.Equal("containers-backlog", component.Find("[data-testid='c4-zoom-key']").TextContent);
        Assert.Equal("model", component.Find("[data-testid='arc42-surface-nav']").GetAttribute("data-mode"));
    }

    [Fact]
    public async Task With_c4_zoom_off_the_pane_keeps_its_tabs_and_has_no_surface()
    {
        await using var harness = CreateHarness(zoom: false);
        var component = harness.Render(ChapterPath);
        Settle(component);

        Assert.Empty(component.FindAll("[data-testid='arc42-surface']"));
        Assert.Empty(component.FindAll("[data-testid='arc42-surface-nav']"));
        Assert.Equal(["Chapters", "C4"], component.FindAll("[role='tab']").Select(tab => tab.TextContent.Trim()));
    }

    // ---- a record's state, from the subject store (local ADRs 0044 §5 and 0046) ----

    private const string DebtPath = ".arc42/tdr/0001-debt.md";

    [Fact]
    public async Task A_stored_state_wins_over_the_record_file_and_the_file_stands_where_nothing_is_stored()
    {
        var subjects = new SessionDevbookSubjectStateStore();
        await using var harness = CreateHarness(subjects: subjects);
        var catalog = await harness.Context.Services.GetRequiredService<Arc42DevbookStore>().LoadAsync(harness.RepositoryAlias);
        var c4 = await harness.Context.Services.GetRequiredService<C4DevbookStore>().LoadAsync(harness.RepositoryAlias);

        Assert.False(subjects.SetState(harness.RepositoryAlias, DevbookSubject.Debt(DebtPath), "resolved", "test").IsRefused);
        Assert.False(subjects.SetState(harness.RepositoryAlias, DevbookSubject.Decision(".arc42/adr/0006-schema.md", 12), "decided", "test").IsRefused);
        Assert.False(subjects.SetState(harness.RepositoryAlias, DevbookSubject.Decision(".arc42/adr/0003-store.md", 7), "proposed", "test").IsRefused);

        var states = C4RecordStates.From(subjects.List(harness.RepositoryAlias));
        var stated = states.Apply(catalog);

        // The debt record: the store's rung, beside the file's severity.
        Assert.Equal("High · resolved", Assert.Single(Arc42Surface.Debt(stated)).Note);

        // The decisions: 0006's file says proposed and the store says decided; 0003's
        // file says nothing and the store says proposed; 0005 has nothing stored.
        var decisions = Arc42Surface.Decisions(stated);
        Assert.False(decisions.Single(record => record.Number == "0006").Proposed);
        Assert.True(decisions.Single(record => record.Number == "0003").Proposed);
        Assert.False(decisions.Single(record => record.Number == "0005").Proposed);

        // The record panel and the inspector read the same.
        var placement = C4RecordPlacement.For(stated, c4, states.Apply(stated.Documents.Single(document => document.Path.EndsWith("0001-debt.md", StringComparison.Ordinal))));
        Assert.Equal(DevbookDebtLifecycle.Resolved, placement!.Lifecycle);

        var debt = Assert.Single(C4ElementInspection.For(c4, c4.Workspaces[0], catalog.Documents, "backlog.mobile", states).Debt);
        Assert.Equal("resolved", debt.Lifecycle);
        Assert.False(debt.OpenDebt);

        // Nothing stored: the files stand.
        Assert.Equal("High · planned", Assert.Single(Arc42Surface.Debt(C4RecordStates.None.Apply(catalog))).Note);
        Assert.True(Arc42Surface.Decisions(C4RecordStates.None.Apply(catalog)).Single(record => record.Number == "0006").Proposed);
        Assert.Equal("planned", Assert.Single(C4ElementInspection.For(c4, c4.Workspaces[0], catalog.Documents, "backlog.mobile").Debt).Lifecycle);
    }

    [Fact]
    public async Task A_state_set_in_the_store_redraws_the_surface_without_a_reload()
    {
        var subjects = new SessionDevbookSubjectStateStore();
        await using var harness = CreateHarness(subjects: subjects, status: true);
        var component = harness.Render(DebtPath);
        Settle(component);

        component.WaitForAssertion(() =>
        {
            Assert.Equal("Planned", component.Find("[data-testid='arc42-record-lifecycle'] [aria-current='step']").TextContent);
            Assert.Equal("High · planned", component.Find("[data-testid='arc42-surface-record-note']").TextContent);
        });

        // Set the way the header's pick, a tool call over MCP or replication sets it:
        // through the store, which tells the panel it changed.
        Assert.False(subjects.SetState(harness.RepositoryAlias, DevbookSubject.Debt(DebtPath), "in-progress", "test").IsRefused);

        component.WaitForAssertion(() =>
        {
            Assert.Equal("In progress", component.Find("[data-testid='arc42-record-lifecycle'] [aria-current='step']").TextContent);
            Assert.Equal("High · in progress", component.Find("[data-testid='arc42-surface-record-note']").TextContent);
        });
    }

    /// <summary>The inspector and the decision log read the store too, and redraw when
    /// it changes: the selected element's debt rung, and a decision the store says is
    /// proposed counted as proposed.</summary>
    [Fact]
    public async Task The_inspector_and_the_decision_log_redraw_from_the_store()
    {
        var subjects = new SessionDevbookSubjectStateStore();
        await using var harness = CreateHarness(subjects: subjects, status: true);
        var component = harness.Render(null);
        Settle(component);
        component.WaitForAssertion(() => Assert.Single(component.FindAll("[data-testid='arc42-surface-decisions-count']")));

        // Found and typed into in one dispatch: the surface is still settling, and a
        // render between the two would leave the handler stale.
        await component.InvokeAsync(() => component.Find("[data-testid='arc42-surface-find'] input").InputAsync(new ChangeEventArgs { Value = "Mobile" }));
        await component.InvokeAsync(() => component.FindAll("[role='option']").First(candidate => candidate.TextContent.Contains("Mobile App", StringComparison.Ordinal)).ClickAsync(new()));

        component.WaitForAssertion(() =>
        {
            // Debt on the selected element is drawn as its card: the severity, and the rung.
            Assert.Equal("Severity High", component.Find("[data-testid='c4-inspector-debt-severity']").TextContent.Trim());
            Assert.Equal("Planned", component.Find("[data-testid='c4-inspector-debt-lifecycle'] [aria-current='step']").TextContent.Trim());
            Assert.Equal("3 · 1 proposed", component.Find("[data-testid='arc42-surface-decisions-count']").TextContent.Trim());
        });

        Assert.False(subjects.SetState(harness.RepositoryAlias, DevbookSubject.Debt(DebtPath), "resolved", "test").IsRefused);
        Assert.False(subjects.SetState(harness.RepositoryAlias, DevbookSubject.Decision(".arc42/adr/0005-replica.md", 3), "proposed", "test").IsRefused);

        component.WaitForAssertion(() =>
        {
            // Debt on the selected element is drawn as its card: the severity, and the rung.
            Assert.Equal("Severity High", component.Find("[data-testid='c4-inspector-debt-severity']").TextContent.Trim());
            Assert.Equal("Resolved", component.Find("[data-testid='c4-inspector-debt-lifecycle'] [aria-current='step']").TextContent.Trim());
            Assert.Equal("3 · 2 proposed", component.Find("[data-testid='arc42-surface-decisions-count']").TextContent.Trim());
        });
    }

    /// <summary>With the status display off nothing of a subject's state is drawn
    /// (local ADR 0044 §5), so the surface reads the record files as it did.</summary>
    [Fact]
    public async Task With_the_status_display_off_the_surface_reads_the_record_files()
    {
        var subjects = new SessionDevbookSubjectStateStore();
        await using var harness = CreateHarness(subjects: subjects, status: false);
        Assert.False(subjects.SetState(harness.RepositoryAlias, DevbookSubject.Debt(DebtPath), "resolved", "test").IsRefused);

        var component = harness.Render(DebtPath);
        Settle(component);

        component.WaitForAssertion(() =>
        {
            Assert.Equal("Planned", component.Find("[data-testid='arc42-record-lifecycle'] [aria-current='step']").TextContent);
            Assert.Equal("High · planned", component.Find("[data-testid='arc42-surface-record-note']").TextContent);
        });
    }

    // ---- the harness ------------------------------------------------------------

    /// <summary>The left navigation's open line — a view, a chapter or a record — as
    /// its label reads, without the count or tag beside it.</summary>
    private static string OpenLine(IRenderedComponent<Arc42DevbookPanel> component) =>
        component.Find("[data-testid='arc42-surface-nav'] [aria-current='page'] .arc42-surface-nav__label").TextContent.Trim()
        + (component.Find("[data-testid='arc42-surface-nav'] [aria-current='page']").QuerySelector(".arc42-surface-nav__number")?.TextContent is { } number ? " " + number : string.Empty);

    private static void Settle(IRenderedComponent<Arc42DevbookPanel> component) =>
        component.WaitForAssertion(() =>
        {
            Assert.Empty(component.FindAll("[data-testid='arc42-loading']"));
            Assert.True(component.FindAll("[data-testid='arc42-surface-nav']").Count > 0 || component.FindAll("[data-testid='arc42-surface']").Count == 0);
        });

    private const string Workspace = """
        workspace "Test Backlog" "A workspace for the surface" {
            !identifiers hierarchical
            model {
                me = person "ME" "The owner"
                backlog = softwareSystem "Prompt Backlog" "The system" {
                    desktop = container "Desktop App" "Windows client" ".NET MAUI"
                    mobile = container "Mobile App" "Capture" ".NET MAUI"
                    cloud = container "Cloud Service" "Sync" "ASP.NET Core"
                    cloudDb = container "Cloud Database" "Replica" "Cosmos DB" "Database"
                    store = container "Local Task Store" "Canonical" "SQLite" "Database"
                }
                me -> backlog.desktop "Captures work"
                backlog.desktop -> backlog.cloud "Syncs"
                backlog.mobile -> backlog.cloud "Syncs"
                backlog.cloud -> backlog.cloudDb "Stores"
                backlog.desktop -> backlog.store "Reads and writes"
            }
            views {
                systemContext backlog "context-backlog" "System Context" { include * }
                container backlog "containers-backlog" "Container Diagram" { include * }
            }
        }
        """;

    private const string References = """
        {
          "views": {
            "containers-backlog": [".arc42/05-building-block-view.md#container-view"]
          }
        }
        """;

    private async Task<(Arc42DevbookCatalog Catalog, C4Catalog C4)> LoadAsync()
    {
        await using var harness = CreateHarness();
        var store = harness.Context.Services.GetRequiredService<Arc42DevbookStore>();
        var c4 = harness.Context.Services.GetRequiredService<C4DevbookStore>();
        return (await store.LoadAsync(harness.RepositoryAlias), await c4.LoadAsync(harness.RepositoryAlias));
    }

    private Harness CreateHarness(bool zoom = true, string references = References, bool embed = false, IDevbookSubjectStateStore? subjects = null, bool? status = null)
    {
        var root = Path.Combine(Path.GetTempPath(), "backlog-arc42-surface-tests", Guid.NewGuid().ToString("N"));
        _roots.Add(root);

        var arc42 = Path.Combine(root, ".arc42");
        Directory.CreateDirectory(Path.Combine(arc42, "adr", "guidelines"));
        Directory.CreateDirectory(Path.Combine(arc42, "tdr"));
        Directory.CreateDirectory(Path.Combine(arc42, C4DevbookStore.ReferenceDirectory));

        void Write(string relative, string text) =>
            File.WriteAllText(Path.Combine(arc42, relative.Replace('/', Path.DirectorySeparatorChar)), text);

        Write(C4DevbookStore.WorkspaceFile, Workspace);
        Write($"{C4DevbookStore.ReferenceDirectory}/{C4DevbookStore.ReferenceFile}", references);
        Write("01-introduction.md", "# 01. Introduction and Goals\n\nGoals.\n");
        Write("05-building-block-view.md",
            "# 05. Building Block View\n\n```meta\nrelated: [\".arc42/adr/0005-replica.md\"]\n```\n\nThe static decomposition.\n\n## Container View\n\nThe containers.\n"
            + (embed ? "\n![Container Diagram](c4:containers-backlog)\n" : string.Empty));
        Write("09-architecture-decisions.md", "# 09. Architecture Decisions\n\nThe decision log.\n");
        Write("11-risks-and-technical-debt.md", "# 11. Risks and Technical Debt\n\nThe risks.\n");
        Write("adr/guidelines/0004-guide.md", "# ADR 0004: A guideline adopted from elsewhere\n\nThe guideline.\n");
        Write("adr/0003-store.md", "# ADR 0003: SQLite is the canonical store\n\nThe store decision.\n");
        Write("adr/0005-replica.md",
            "# ADR 0005: An Azure-hosted replica carries sync\n\n```meta\nc4: [backlog.cloud, backlog.cloudDb]\n```\n\nThe replica decision.\n");
        Write("adr/0006-schema.md",
            "# ADR 0006: Additive schema bootstrapping\n\n```meta\nstatus: proposed\nrelated: [\".arc42/05-building-block-view.md\"]\n```\n\nThe schema decision.\n");
        Write("tdr/0001-debt.md",
            "# TDR 0001: The mobile app has no offline queue\n\n```meta\nstatus: planned\nseverity: high\nc4: [backlog.mobile]\n```\n\nThe debt.\n");

        var settings = new WorkspaceSettingsStore(Path.Combine(root, "store"));
        var features = new AppFeatureSettingsStore(AppFeatures.All, Path.Combine(root, "features", "features.json"));
        Assert.Null(features.SetEnabled(DevbookFeatures.C4Diagrams, true));
        if (zoom) Assert.Null(features.SetEnabled(DevbookFeatures.C4Zoom, true));
        if (status is { } shown) Assert.Null(features.SetEnabled(DevbookFeatures.Status, shown));

        var gitHub = new GitHubSettingsStore(Path.Combine(root, "github", "github.json"));
        var (repositories, errors) = GitHubSettings.ParseText("JSdotNet/Backlog");
        Assert.Empty(errors);

        var repository = repositories[0] with
        {
            CloneDirectory = root,
            DevbookFolders =
            [
                .. DevbookFolderSetting.Defaults().Select(folder =>
                    string.Equals(folder.Key, ".arc42", StringComparison.OrdinalIgnoreCase)
                        ? folder with { Path = arc42 }
                        : folder)
            ]
        };
        Assert.Null(gitHub.SetRepositories([repository]));

        var context = new BunitContext();
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        context.JSInterop.Setup<bool>("backlogC4Zoom.attach", _ => true).SetResult(true);

        context.Services.AddSingleton(settings);
        context.Services.AddSingleton<IAppFeatureSettings>(features);
        context.Services.AddSingleton(gitHub);
        context.Services.AddSingleton<IDevbookFolderSource>(new DevbookFolderSource(gitHub, settings));
        context.Services.AddSingleton<Arc42DevbookStore>();
        context.Services.AddSingleton<DevbookChapterWriter>();
        context.Services.AddSingleton<C4DevbookStore>();
        if (subjects is not null) context.Services.AddSingleton(subjects);

        // The status display is a Devbook switch; a host that composes no scope shows
        // it, so a test that turns it either way composes one.
        if (status is not null) context.Services.AddSingleton<DevbookScope>();

        return new Harness(context, repository.Alias);
    }

    private sealed record Harness(BunitContext Context, string RepositoryAlias) : IAsyncDisposable
    {
        public IRenderedComponent<Arc42DevbookPanel> Render(string? selectedPath, List<DevbookChapterLink>? asked = null) =>
            Context.Render<Arc42DevbookPanel>(parameters =>
            {
                parameters
                    .Add(panel => panel.RepositoryAlias, RepositoryAlias)
                    .Add(panel => panel.SelectedPath, selectedPath);

                if (asked is not null)
                {
                    parameters.Add(panel => panel.OnNavigateToChapter, EventCallback.Factory.Create<DevbookChapterLink>(this, asked.Add));
                }
            });

        public async ValueTask DisposeAsync() => await Context.DisposeAsync();
    }

    public void Dispose()
    {
        foreach (var root in _roots.Where(Directory.Exists))
        {
            try { Directory.Delete(root, recursive: true); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
    }
}
