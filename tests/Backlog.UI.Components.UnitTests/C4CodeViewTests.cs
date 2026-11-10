namespace Backlog.UI.Components.UnitTests;

/// <summary>
/// The Code level of the C4 zoom tab, as the design's Code screen draws it: a class box
/// per type with its stereotype, name and members, the Public / All toggle, the uses as
/// arrows laid out by <see cref="C4LayoutEngine"/>, − / Fit / + over them, and the type
/// panel — its files, Open in editor, the reference it is held to, the decision records
/// linked to the component, and its technical debt, else the component's.
/// </summary>
public sealed class C4CodeViewTests : IDisposable
{
    private readonly BunitContext _context = new();

    public C4CodeViewTests() => _context.JSInterop.Mode = JSRuntimeMode.Loose;

    public void Dispose() => _context.Dispose();

    internal const string Refresher = "Shop.Data.Refresher";
    internal const string Builder = "Shop.Data.Builder";
    internal const string Database = "Shop.Data.Database";

    /// <summary>Three types: a refresher that names a builder, and a database that
    /// inherits the builder — one solid arrow and one dashed.</summary>
    internal static readonly C4CodeLevel Level = new(
        "Devbook",
        ["src/Data"],
        [
            new(Refresher, "Refresher", "sealed class", "«sealed class» : IDisposable",
                ["src/Data/Refresher.cs"],
                ["+ Request(string root) : void", "+ Dispose() : void"],
                ["+ Request(string root) : void", "+ Dispose() : void", "- _gate : SemaphoreSlim"]),
            new(Builder, "Builder", "static class", "«static class»",
                ["src/Data/Builder.cs"],
                ["+ Build(string root) : bool"],
                ["+ Build(string root) : bool"]),
            new(Database, "Database", "sealed partial class", "«sealed partial class» : Builder",
                ["src/Data/Database.cs", "src/Data/Database.Retrieval.cs"],
                [],
                ["~ Open(string path) : Database"])
        ],
        [
            new(Refresher, Builder, false),
            new(Database, Builder, true)
        ],
        [
            new("0004", "Knowledge index is a generated database", ".devbook/arc42/adr/0004-knowledge-index.md")
        ]);

    private IRenderedComponent<C4CodeView> Render(
        C4CodeLevel? level,
        Func<string, Task<string?>>? open = null,
        Func<C4CodeRecord, Task>? openRecord = null,
        Action? zoomOut = null) =>
        _context.Render<C4CodeView>(parameters =>
        {
            parameters.Add(view => view.Level, level).Add(view => view.Component, "Devbook");
            if (open is not null) parameters.Add(view => view.OpenInEditor, open);
            if (openRecord is not null) parameters.Add(view => view.OpenRecord, openRecord);
            if (zoomOut is not null) parameters.Add(view => view.OnZoomOut, EventCallback.Factory.Create(this, zoomOut));
        });

    private static AngleSharp.Dom.IElement Box(IRenderedComponent<C4CodeView> view, string id) =>
        view.FindAll("[data-testid='c4-code-type']").Single(box => box.GetAttribute("data-type-id") == id);

    private static IReadOnlyList<string> Members(IRenderedComponent<C4CodeView> view, string id) =>
        [.. Box(view, id).QuerySelectorAll("[data-testid='c4-code-member']").Select(member => member.TextContent)];

    [Fact]
    public void Each_type_is_a_box_with_its_stereotype_name_and_public_members()
    {
        var view = Render(Level);

        Assert.Equal(3, view.FindAll("[data-testid='c4-code-type']").Count);

        var refresher = Box(view, Refresher);
        Assert.Equal("«sealed class» : IDisposable", refresher.QuerySelector("[data-testid='c4-code-stereotype']")!.TextContent);
        Assert.Equal("Refresher", refresher.QuerySelector("[data-testid='c4-code-name']")!.TextContent);
        Assert.Equal(["+ Request(string root) : void", "+ Dispose() : void"], Members(view, Refresher));
        Assert.Contains("Generated from", view.Find("[data-testid='c4-code-source']").TextContent, StringComparison.Ordinal);
        Assert.Contains("src/Data", view.Find("[data-testid='c4-code-source']").TextContent, StringComparison.Ordinal);
        Assert.Contains("3 types", view.Find("[data-testid='c4-code-source']").TextContent, StringComparison.Ordinal);
    }

    [Fact]
    public void All_shows_every_member_and_Public_goes_back()
    {
        var view = Render(Level);
        Assert.Equal("true", view.Find("[data-testid='c4-code-members-public']").GetAttribute("aria-checked"));
        Assert.Empty(Members(view, Database));

        view.Find("[data-testid='c4-code-members-all']").Click();

        Assert.True(view.Instance.AllMembers);
        Assert.Equal("true", view.Find("[data-testid='c4-code-members-all']").GetAttribute("aria-checked"));
        Assert.Contains("- _gate : SemaphoreSlim", Members(view, Refresher));
        Assert.Equal(["~ Open(string path) : Database"], Members(view, Database));

        view.Find("[data-testid='c4-code-members-public']").Click();

        Assert.False(view.Instance.AllMembers);
        Assert.DoesNotContain("- _gate : SemaphoreSlim", Members(view, Refresher));
    }

    [Fact]
    public void Uses_are_arrows_solid_for_a_reference_and_dashed_for_inheritance()
    {
        var view = Render(Level);

        var uses = view.FindAll("[data-testid='c4-code-use']");
        Assert.Equal(2, uses.Count);
        Assert.All(uses, use =>
        {
            Assert.False(string.IsNullOrWhiteSpace(use.GetAttribute("d")));
            Assert.StartsWith("url(#", use.GetAttribute("marker-end"), StringComparison.Ordinal);
        });

        var inherits = uses.Single(use => use.GetAttribute("data-from") == Database);
        Assert.Contains("c4-code__use--inherits", inherits.ClassList);
        Assert.DoesNotContain("c4-code__use--inherits", uses.Single(use => use.GetAttribute("data-from") == Refresher).ClassList);
        Assert.Contains(">inherits</text>", view.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void The_panel_follows_the_picked_type_with_its_files_and_the_components_decisions()
    {
        var view = Render(Level);

        // The first type until one is picked.
        Assert.Equal("Refresher", view.Find("[data-testid='c4-code-panel-name']").TextContent);
        Assert.Equal("true", Box(view, Refresher).GetAttribute("aria-pressed"));

        Box(view, Database).Click();

        Assert.Equal("Database", view.Find("[data-testid='c4-code-panel-name']").TextContent);
        Assert.Equal("true", Box(view, Database).GetAttribute("aria-pressed"));
        Assert.Equal("false", Box(view, Refresher).GetAttribute("aria-pressed"));
        Assert.Equal(
            ["src/Data/Database.cs", "src/Data/Database.Retrieval.cs"],
            view.FindAll("[data-testid='c4-code-path']").Select(path => path.TextContent));
        Assert.Contains("Code · sealed partial class", view.Find("[data-testid='c4-code-panel']").TextContent, StringComparison.Ordinal);

        var decisions = view.Find("[data-testid='c4-code-decisions']");
        Assert.Contains("Decisions it implements · 1", decisions.TextContent, StringComparison.Ordinal);
        Assert.Contains("0004", decisions.TextContent, StringComparison.Ordinal);
        Assert.Contains("Knowledge index is a generated database", decisions.TextContent, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Open_in_editor_opens_the_types_first_file_and_says_why_when_it_could_not()
    {
        var opened = new List<string>();
        var view = Render(Level, open: path =>
        {
            opened.Add(path);
            return Task.FromResult<string?>("VS Code is not installed.");
        });

        Box(view, Builder).Click();
        await view.Find("[data-testid='c4-code-open']").ClickAsync(new());

        Assert.Equal(["src/Data/Builder.cs"], opened);
        Assert.Equal("VS Code is not installed.", view.Find("[data-testid='c4-code-open-error']").TextContent.Trim());

        // Picking another type clears what the last open said.
        Box(view, Refresher).Click();
        Assert.Empty(view.FindAll("[data-testid='c4-code-open-error']"));
    }

    [Fact]
    public void With_nothing_to_open_files_with_there_is_no_open_in_editor()
    {
        var view = Render(Level);

        Assert.Empty(view.FindAll("[data-testid='c4-code-open']"));
        Assert.Equal("SPAN", view.Find("[data-testid='c4-code-decision']").TagName);
    }

    [Fact]
    public async Task A_decision_opens_its_record()
    {
        var opened = new List<string>();
        var view = Render(Level, openRecord: record =>
        {
            opened.Add(record.Path);
            return Task.CompletedTask;
        });

        await view.Find("[data-testid='c4-code-decision']").ClickAsync(new());

        Assert.Equal([".devbook/arc42/adr/0004-knowledge-index.md"], opened);
    }

    /// <summary>The level of <see cref="Level"/> with debt: TDR 0001 names the
    /// Refresher, TDR 0002 is the component's alone; and the Builder held to a
    /// reference.</summary>
    internal static readonly C4CodeLevel LevelWithDebt = Level with
    {
        Debt =
        [
            new("0001", "Refresher polls the folder", ".devbook/arc42/tdr/0001-refresher-polls.md"),
            new("0002", "The panels share one cache", ".devbook/arc42/tdr/0002-one-cache.md")
        ],
        Types =
        [
            Level.Types[0] with { Debt = [new("0001", "Refresher polls the folder", ".devbook/arc42/tdr/0001-refresher-polls.md")] },
            Level.Types[1] with { Reference = new C4CodeReference("tools/build.mjs", "BuilderParityTests") },
            Level.Types[2]
        ]
    };

    private static IReadOnlyList<string> DebtNumbers(IRenderedComponent<C4CodeView> view) =>
        [.. view.FindAll("[data-testid='c4-code-debt-record'] .c4-code__record-number").Select(number => number.TextContent)];

    [Fact]
    public void Technical_debt_lists_what_names_the_type_and_falls_back_to_the_components()
    {
        var view = Render(LevelWithDebt);

        // The Refresher is named by TDR 0001: its own debt, no fallback.
        Assert.Contains("Technical debt", view.Find("[data-testid='c4-code-debt']").TextContent, StringComparison.Ordinal);
        Assert.Equal(["0001"], DebtNumbers(view));
        Assert.Empty(view.FindAll("[data-testid='c4-code-debt-fallback']"));

        // Nothing names the Builder: the component's debt, said to be the component's.
        Box(view, Builder).Click();
        Assert.Equal(
            "No TDR names Builder. Its component, Devbook, carries 2 TDRs:",
            view.Find("[data-testid='c4-code-debt-fallback']").TextContent.Trim());
        Assert.Equal(["0001", "0002"], DebtNumbers(view));
    }

    [Fact]
    public void Without_any_debt_the_panel_says_neither_the_type_nor_the_component_carries_any()
    {
        var view = Render(Level);

        Assert.Contains("No TDR names Refresher or Devbook.", view.Find("[data-testid='c4-code-debt']").TextContent, StringComparison.Ordinal);
        Assert.Empty(view.FindAll("[data-testid='c4-code-debt-record']"));
    }

    [Fact]
    public async Task A_debt_record_opens_its_record()
    {
        var opened = new List<string>();
        var view = Render(LevelWithDebt, openRecord: record =>
        {
            opened.Add(record.Path);
            return Task.CompletedTask;
        });

        await view.Find("[data-testid='c4-code-debt-record']").ClickAsync(new());

        Assert.Equal([".devbook/arc42/tdr/0001-refresher-polls.md"], opened);
    }

    [Fact]
    public void Held_to_a_reference_shows_for_a_type_that_has_one_and_only_then()
    {
        var view = Render(LevelWithDebt);
        Assert.Empty(view.FindAll("[data-testid='c4-code-reference']"));

        Box(view, Builder).Click();

        var reference = view.Find("[data-testid='c4-code-reference']");
        Assert.Contains("Held to a reference", reference.TextContent, StringComparison.Ordinal);
        Assert.Contains("tools/build.mjs, compared with it in BuilderParityTests.", reference.TextContent, StringComparison.Ordinal);

        // In the design's order: the reference, then the decisions, then the debt.
        var sections = view.FindAll("[data-testid='c4-code-panel'] > section").Select(section => section.GetAttribute("data-testid"));
        Assert.Equal(["c4-code-reference", "c4-code-decisions", "c4-code-debt"], sections);
    }

    [Fact]
    public void Minus_fit_and_plus_size_the_class_boxes()
    {
        var view = Render(Level);
        string Scale() => view.Find(".c4-code__canvas").GetAttribute("data-scale")!;

        Assert.Equal("100", Scale());
        Assert.DoesNotContain("zoom", view.Find(".c4-code__canvas").GetAttribute("style"), StringComparison.Ordinal);

        view.Find("[data-testid='c4-code-scale-in']").Click();
        Assert.Equal("125", Scale());
        Assert.Contains("zoom: 1.25", view.Find(".c4-code__canvas").GetAttribute("style"), StringComparison.Ordinal);

        view.Find("[data-testid='c4-code-scale-out']").Click();
        view.Find("[data-testid='c4-code-scale-out']").Click();
        view.Find("[data-testid='c4-code-scale-out']").Click();
        Assert.Equal("50", Scale());
        Assert.True(view.Find("[data-testid='c4-code-scale-out']").HasAttribute("disabled"));

        view.Find("[data-testid='c4-code-scale-fit']").Click();
        Assert.Equal("100", Scale());
    }

    /// <summary>Another component's level opens at its own size, as a new view does;
    /// the same component read again keeps the size the reader chose.</summary>
    [Fact]
    public void Another_components_level_opens_at_its_own_size()
    {
        var view = Render(Level);
        string Scale() => view.Find(".c4-code__canvas").GetAttribute("data-scale")!;

        view.Find("[data-testid='c4-code-scale-in']").Click();
        Assert.Equal("125", Scale());

        view.Render(parameters => parameters.Add(c => c.Level, Level with { }));
        Assert.Equal("125", Scale());

        view.Render(parameters => parameters.Add(c => c.Level, Level with { Component = "Tasks" }));
        Assert.Equal("100", Scale());
    }

    [Fact]
    public void Backspace_on_the_picture_asks_to_zoom_out()
    {
        var zoomedOut = 0;
        var view = Render(Level, zoomOut: () => zoomedOut++);

        view.Find("[data-testid='c4-code']").KeyDown(new KeyboardEventArgs { Key = "Enter" });
        Assert.Equal(0, zoomedOut);

        view.Find("[data-testid='c4-code']").KeyDown(new KeyboardEventArgs { Key = "Backspace" });
        Assert.Equal(1, zoomedOut);
    }

    [Fact]
    public void While_it_is_read_it_says_so_and_names_the_component()
    {
        var view = Render(null);

        Assert.Contains("Code — Devbook component", view.Find("[data-testid='c4-code-title']").TextContent, StringComparison.Ordinal);
        Assert.Contains("Reading the source", view.Find("[data-testid='c4-code-loading']").TextContent, StringComparison.Ordinal);
        Assert.Empty(view.FindAll("[data-testid='c4-code-type']"));
    }

    [Fact]
    public void A_folder_with_no_types_says_so_rather_than_drawing_nothing()
    {
        var view = Render(new C4CodeLevel("Devbook", ["src/Empty"], [], [], []));

        Assert.Contains("src/Empty", view.Find("[data-testid='c4-code-empty']").TextContent, StringComparison.Ordinal);
    }

    [Fact]
    public void A_level_that_could_not_be_read_shows_why()
    {
        var view = Render(new C4CodeLevel("Devbook", ["../outside"], [], [], []) { Problem = "'../outside' is not inside the repository." });

        Assert.Contains("not inside the repository", view.Find("[data-testid='c4-code-problem']").TextContent, StringComparison.Ordinal);
    }
}
