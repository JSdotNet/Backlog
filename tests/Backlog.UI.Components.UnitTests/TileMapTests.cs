using System.Text.Json;
using Microsoft.JSInterop;

namespace Backlog.UI.Components.UnitTests;

/// <summary>
/// Leaflet draws the tiles in a browser, which bUnit does not run. What is pinned
/// here is everything on this side of the interop: what the map is told, the
/// notes that stand in for the tiles, and the pins, pills and controls drawn from
/// the view the map reports — which the tests report for it.
/// </summary>
public sealed class TileMapTests
{
    private const string Tiles = "https://tiles.test/{z}/{x}/{y}.png?key=secret-key";

    private static readonly MapCoordinate Brussels = new(50.85, 4.35);
    private static readonly MapCoordinate Antwerp = new(51.22, 4.40);
    private static readonly MapCoordinate Ghent = new(51.05, 3.72);
    private static readonly MapCoordinate Oslo = new(59.91, 10.75);
    private static readonly MapCoordinate Lisbon = new(38.72, -9.14);

    private static IReadOnlyList<TileMapMark> Marks() =>
    [
        new("techorama", Antwerp, "1, Techorama, Going, 12 May, Antwerp", "Techorama · 12 May", "1", TileMapMarkTone.Filled),
        new("dotnet-day", Ghent, "2, dotNET Day, Spotted, 3 June, Ghent", "dotNET Day · 3 June", "2", TileMapMarkTone.Dashed),
        new("ndc", Oslo, "3, NDC Oslo, Considering, 9 June, Oslo", "NDC Oslo · 9 June", "3", TileMapMarkTone.Dashed),
        new("old", Lisbon, "Web Summit, Attended, 4 November 2025, Lisbon", "Web Summit · 4 Nov 2025", null, TileMapMarkTone.Muted)
    ];

    private static BunitContext Context()
    {
        var context = new BunitContext();
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        return context;
    }

    private static IRenderedComponent<TileMap> Render(
        BunitContext context,
        Action<ComponentParameterCollectionBuilder<TileMap>>? more = null,
        string? tiles = Tiles,
        IReadOnlyList<TileMapMark>? marks = null) =>
        context.Render<TileMap>(parameters =>
        {
            parameters
                .Add(map => map.TileUrlTemplate, tiles)
                .Add(map => map.Attribution, "© Microsoft, © TomTom")
                .Add(map => map.Marks, marks ?? Marks())
                .Add(map => map.TestId, "map");
            more?.Invoke(parameters);
        });

    /// <summary>Belgium filling a 600 by 400 map: Antwerp and Ghent in view,
    /// Oslo north of it, Lisbon south-west.</summary>
    private static Task OnBelgium(IRenderedComponent<TileMap> map, bool settled = true, double zoom = 7) =>
        map.Instance.ViewMoved(Brussels.Latitude, Brussels.Longitude, zoom, 600, 400, settled);

    private static JsonElement Json(object? value) => JsonSerializer.SerializeToElement(value);

    [Fact]
    public void With_no_tile_source_the_map_says_so_and_loads_nothing()
    {
        using var context = Context();

        var map = Render(context, parameters => parameters
            .Add(m => m.MissingSourceHeading, "The map needs an Azure Maps key.")
            .Add(m => m.MissingSourceText, "Add it in Settings."), tiles: null);

        var note = map.Find("[data-testid='map-missing-source']");
        Assert.Contains("The map needs an Azure Maps key.", note.TextContent);
        Assert.Contains("Add it in Settings.", note.TextContent);
        Assert.Equal("missing-source", map.Find("section").GetAttribute("data-map-state"));
        Assert.Empty(map.FindAll(".tile-map__tiles"));
        Assert.Empty(map.FindAll("button.tile-map__pin"));
        Assert.Empty(map.FindAll(".tile-map__attribution"));
        Assert.DoesNotContain(context.JSInterop.Invocations, invocation => invocation.Identifier.StartsWith("backlogTileMap.", StringComparison.Ordinal));
    }

    [Fact]
    public void The_missing_source_note_offers_the_hosts_way_to_fix_it()
    {
        using var context = Context();
        var opened = 0;

        var map = Render(context, parameters => parameters
            .Add(m => m.MissingSourceActionLabel, "Open Settings")
            .Add(m => m.OnMissingSourceAction, () => opened++), tiles: "  ");

        map.Find("[data-testid='map-missing-source'] button").Click();

        Assert.Equal(1, opened);
    }

    [Fact]
    public void With_a_source_the_map_is_started_on_the_numbered_marks()
    {
        using var context = Context();

        Render(context, parameters => parameters.Add(m => m.FitMaxZoom, 12));

        var invocation = Assert.Single(context.JSInterop.Invocations["backlogTileMap.initialize"]);
        var options = Json(invocation.Arguments[2]);

        Assert.Equal(Tiles, options.GetProperty("tileUrl").GetString());
        Assert.Equal(2, options.GetProperty("minZoom").GetInt32());
        Assert.Equal(18, options.GetProperty("maxZoom").GetInt32());

        var fit = options.GetProperty("fit");
        Assert.Equal(12, fit.GetProperty("maxZoom").GetInt32());
        // Antwerp, Ghent and Oslo are numbered; unnumbered Lisbon is not framed.
        Assert.Equal(
            [51.05, 3.72, 59.91, 10.75],
            fit.GetProperty("bounds").EnumerateArray().Select(value => value.GetDouble()).ToArray());
        Assert.IsType<DotNetObjectReference<TileMap>>(invocation.Arguments[3]);
    }

    [Fact]
    public void With_no_numbered_mark_every_mark_is_framed_and_with_none_the_default_view()
    {
        using var context = Context();

        Render(context, marks: [Marks()[3]]);
        Render(context, parameters => parameters
            .Add(m => m.DefaultCenter, new MapCoordinate(52, 5))
            .Add(m => m.DefaultZoom, 6), marks: []);

        var fits = context.JSInterop.Invocations["backlogTileMap.initialize"]
            .Select(invocation => Json(invocation.Arguments[2]).GetProperty("fit"))
            .ToList();

        Assert.Equal(
            [38.72, -9.14, 38.72, -9.14],
            fits[0].GetProperty("bounds").EnumerateArray().Select(value => value.GetDouble()).ToArray());
        Assert.Equal(JsonValueKind.Null, fits[1].GetProperty("bounds").ValueKind);
        Assert.Equal([52d, 5d], fits[1].GetProperty("center").EnumerateArray().Select(value => value.GetDouble()).ToArray());
        Assert.Equal(6, fits[1].GetProperty("zoom").GetInt32());
    }

    /// <summary>A host that kept the last view — the session's zoom — opens there,
    /// and marks arriving later do not move it.</summary>
    [Fact]
    public void A_view_the_host_kept_opens_the_map_instead_of_a_fit()
    {
        using var context = Context();

        var map = Render(context, parameters => parameters
            .Add(m => m.InitialView, new TileMapView(new MapCoordinate(51, 4), 9)), marks: []);
        map.Render(parameters => parameters.Add(m => m.Marks, Marks()));

        var fit = Json(Assert.Single(context.JSInterop.Invocations["backlogTileMap.initialize"]).Arguments[2]).GetProperty("fit");
        Assert.Equal(JsonValueKind.Null, fit.GetProperty("bounds").ValueKind);
        Assert.Equal([51d, 4d], fit.GetProperty("center").EnumerateArray().Select(value => value.GetDouble()).ToArray());
        Assert.Equal(9, fit.GetProperty("zoom").GetDouble());
        Assert.Empty(context.JSInterop.Invocations["backlogTileMap.fit"]);
    }

    /// <summary>A tile URL can carry the provider's key (ADR 0061 §4); it crosses
    /// the interop and goes nowhere else.</summary>
    [Fact]
    public async Task The_tile_url_never_reaches_the_markup()
    {
        using var context = Context();

        var map = Render(context);
        await map.InvokeAsync(() => OnBelgium(map));

        Assert.DoesNotContain("secret-key", map.Markup, StringComparison.Ordinal);
        Assert.DoesNotContain("tiles.test", map.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void No_pin_is_drawn_before_the_map_has_reported_where_it_is()
    {
        using var context = Context();

        var map = Render(context);

        Assert.Equal("loading", map.Find("section").GetAttribute("data-map-state"));
        Assert.Empty(map.FindAll("button.tile-map__pin"));
    }

    /// <summary>The controls and the foot are drawn while the map starts, so the map can
    /// measure what they cover before it frames its first view; the zoom buttons wait for a
    /// view to zoom.</summary>
    [Fact]
    public void The_chrome_is_drawn_while_the_map_starts_with_the_zoom_waiting()
    {
        using var context = Context();

        var map = Render(context, parameters => parameters.Add(m => m.Legend, builder => builder.AddContent(0, "Going")));

        var chrome = map.Find(".tile-map__chrome");
        Assert.NotNull(chrome.QuerySelector(".tile-map__controls"));
        Assert.NotNull(chrome.QuerySelector(".tile-map__foot [data-testid='map-legend']"));
        Assert.NotNull(chrome.QuerySelector(".tile-map__foot [data-testid='map-attribution']"));
        Assert.True(map.Find("[data-testid='map-zoom-in']").HasAttribute("disabled"));
        Assert.True(map.Find("[data-testid='map-zoom-out']").HasAttribute("disabled"));
    }

    /// <summary>Chrome that comes or goes after the map started — a legend handed in late —
    /// changes what the tiles keep free, so the map measures it again.</summary>
    [Fact]
    public void Chrome_that_changes_after_the_start_is_measured_again()
    {
        using var context = Context();
        var map = Render(context);
        Assert.Empty(context.JSInterop.Invocations["backlogTileMap.measure"]);

        map.Render(parameters => parameters.Add(m => m.Legend, builder => builder.AddContent(0, "Going")));
        map.Render(parameters => parameters.Add(m => m.Legend, builder => builder.AddContent(0, "Going")));

        Assert.Single(context.JSInterop.Invocations["backlogTileMap.measure"]);
    }

    /// <summary>A numbered mark draws above an unnumbered one at the same spot, so a past
    /// event never hides an upcoming one's pin; among numbered marks the lower number draws
    /// above the higher, so pin 2 never covers pin 1. The stylesheet orders the layers from
    /// the class and the rank written here.</summary>
    [Fact]
    public async Task Numbered_marks_are_ranked_so_the_lower_number_draws_on_top()
    {
        using var context = Context();
        var marks = new List<TileMapMark>
        {
            new("past", Antwerp, "Techorama 2025, Attended", "Techorama 2025 · 12 May 2025", null, TileMapMarkTone.Muted),
            new("first", Antwerp, "1, Techorama 2026, Going", "Techorama 2026 · 12 May", "1", TileMapMarkTone.Filled),
            new("second", Antwerp, "2, Meetup, Spotted", "Meetup · 20 May", "2", TileMapMarkTone.Dashed),
            new("far", Oslo, "3, NDC Oslo, Considering", "NDC Oslo · 9 June", "3", TileMapMarkTone.Dashed)
        };
        var map = Render(context, marks: marks);
        await map.InvokeAsync(() => OnBelgium(map));

        var past = map.Find("[data-mark-id='past']");
        var first = map.Find("[data-mark-id='first']");
        var second = map.Find("[data-mark-id='second']");
        var pill = map.Find("[data-mark-id='far']");

        Assert.DoesNotContain("tile-map__mark--numbered", past.ClassList);
        Assert.DoesNotContain("--tile-map-rank", past.GetAttribute("style"), StringComparison.Ordinal);
        Assert.Contains("tile-map__mark--numbered", first.ClassList);
        Assert.Contains("tile-map__mark--numbered", pill.ClassList);
        Assert.True(Rank(first) > Rank(second));
        Assert.True(Rank(second) > Rank(pill));
        Assert.True(Rank(pill) >= 1);
    }

    private static int Rank(AngleSharp.Dom.IElement element) => int.Parse(
        System.Text.RegularExpressions.Regex.Match(element.GetAttribute("style")!, @"--tile-map-rank:\s*(\d+)").Groups[1].Value,
        System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>The map reports what its chrome covers; a pin there could be neither seen
    /// nor picked, so a numbered one is a pill on the free edge instead, and the host's view
    /// carries the same frame so its "in view" agrees.</summary>
    [Fact]
    public async Task A_mark_under_the_chrome_is_a_pill_on_the_free_edge()
    {
        using var context = Context();
        TileMapViewport? heard = null;
        var underFoot = new MapCoordinate(49.75, 4.35);
        var marks = new List<TileMapMark> { new("south", underFoot, "1, South, Going", "South · 1 May", "1") };
        var map = Render(context, parameters => parameters.Add(m => m.ViewChanged, (TileMapViewport view) => heard = view), marks: marks);

        await map.InvokeAsync(() => map.Instance.ViewMoved(Brussels.Latitude, Brussels.Longitude, 7, 600, 400, true, 60, 80));

        Assert.Empty(map.FindAll("button.tile-map__pin"));
        var pill = map.Find("button.tile-map__pill");
        Assert.Contains("tile-map__pill--bottom", pill.ClassList);
        Assert.Equal(new TileMapInsets(0, 60, 80, 0), heard!.Reserved);
        Assert.False(TileMapProjection.IsInView(heard, underFoot));
    }

    /// <summary>On a short map whose chrome is still reserved, four close numbered marks
    /// out of the free part are each drawn — as a pin or a pill — never dropped.</summary>
    [Fact]
    public async Task No_numbered_mark_is_dropped_on_a_short_map()
    {
        using var context = Context();
        var marks = new List<TileMapMark>
        {
            new("utrecht", new MapCoordinate(52.09, 5.12), "1, Utrecht", "Utrecht", "1"),
            new("nieuwegein", new MapCoordinate(52.03, 5.09), "2, Nieuwegein", "Nieuwegein", "2"),
            new("hilversum", new MapCoordinate(52.22, 5.18), "3, Hilversum", "Hilversum", "3"),
            new("amsterdam", new MapCoordinate(52.37, 4.90), "4, Amsterdam", "Amsterdam", "4")
        };
        var map = Render(context, marks: marks);

        foreach (var height in new[] { 128d, 160d })
        {
            await map.InvokeAsync(() => map.Instance.ViewMoved(52.1, 5.1, 9, 390, height, true, 44, 70));

            var drawn = map.FindAll("button.tile-map__pin, button.tile-map__pill")
                .Select(button => button.GetAttribute("data-mark-id"))
                .Order(StringComparer.Ordinal);
            Assert.Equal(["amsterdam", "hilversum", "nieuwegein", "utrecht"], drawn);
        }
    }

    /// <summary>
    /// A short map — under 200px tall, as the phone's 128, 144 (editor open) and 160 are —
    /// has no room for the legend card over its tiles. The legend folds into a "Legend"
    /// button among the controls, which opens it on demand in the same words; a tall map
    /// shows it as before. The attribution stays visible either way.
    /// </summary>
    [Theory]
    [InlineData(128, true)]
    [InlineData(144, true)]
    [InlineData(160, true)]
    [InlineData(256, false)]
    [InlineData(400, false)]
    public async Task A_short_map_folds_its_legend_into_a_button(double height, bool folded)
    {
        using var context = Context();
        var map = Render(context, parameters => parameters
            .Add(m => m.Legend, builder => builder.AddContent(0, "Going / Spotted or considering / Past")));
        var width = height == 400 ? 1440d : 390d;

        await map.InvokeAsync(() => map.Instance.ViewMoved(Brussels.Latitude, Brussels.Longitude, 7, width, height, true));

        Assert.Equal(folded, map.Find("section").ClassList.Contains("tile-map--short"));
        Assert.Single(map.FindAll("[data-testid='map-attribution']"));
        var legend = map.Find("[data-testid='map-legend']");
        Assert.Equal("Going / Spotted or considering / Past", legend.TextContent);
        if (!folded)
        {
            Assert.Empty(map.FindAll("[data-testid='map-legend-toggle']"));
            Assert.False(legend.HasAttribute("hidden"));
            return;
        }

        var toggle = map.Find(".tile-map__controls [data-testid='map-legend-toggle']");
        Assert.Equal("Legend", toggle.GetAttribute("aria-label"));
        Assert.Equal("false", toggle.GetAttribute("aria-expanded"));
        Assert.Equal(legend.Id, toggle.GetAttribute("aria-controls"));
        Assert.True(legend.HasAttribute("hidden"));

        toggle.Click();

        Assert.Equal("true", map.Find("[data-testid='map-legend-toggle']").GetAttribute("aria-expanded"));
        Assert.False(map.Find("[data-testid='map-legend']").HasAttribute("hidden"));

        map.Find("[data-testid='map-legend-toggle']").Click();

        Assert.Equal("false", map.Find("[data-testid='map-legend-toggle']").GetAttribute("aria-expanded"));
        Assert.True(map.Find("[data-testid='map-legend']").HasAttribute("hidden"));
    }

    /// <summary>Folding or unfolding the legend changes what the chrome covers, so the map
    /// measures it again; opening the folded legend on demand does too.</summary>
    [Fact]
    public async Task Folding_and_opening_the_legend_are_measured_again()
    {
        using var context = Context();
        var map = Render(context, parameters => parameters.Add(m => m.Legend, builder => builder.AddContent(0, "Going")));
        await map.InvokeAsync(() => map.Instance.ViewMoved(Brussels.Latitude, Brussels.Longitude, 7, 390, 256, true));
        var before = context.JSInterop.Invocations["backlogTileMap.measure"].Count;

        await map.InvokeAsync(() => map.Instance.ViewMoved(Brussels.Latitude, Brussels.Longitude, 7, 390, 144, true));
        map.Find("[data-testid='map-legend-toggle']").Click();

        Assert.True(context.JSInterop.Invocations["backlogTileMap.measure"].Count >= before + 2);
    }

    /// <summary>A map still on the view its own fit chose — nobody has panned it — is fitted
    /// again when the legend folds, so the marks are framed clear of the smaller chrome. A
    /// view the reader moved is left where it is.</summary>
    [Fact]
    public async Task Folding_the_legend_refits_a_map_still_on_its_own_fit_and_not_one_the_reader_moved()
    {
        using var context = Context();
        var map = Render(context, parameters => parameters.Add(m => m.Legend, builder => builder.AddContent(0, "Going")));
        await map.InvokeAsync(() => map.Instance.ViewMoved(Brussels.Latitude, Brussels.Longitude, 7, 390, 256, true));
        var fits = context.JSInterop.Invocations["backlogTileMap.fit"].Count;

        await map.InvokeAsync(() => map.Instance.ViewMoved(Brussels.Latitude, Brussels.Longitude, 7, 390, 144, true));
        Assert.Equal(fits + 1, context.JSInterop.Invocations["backlogTileMap.fit"].Count);

        await map.InvokeAsync(() => map.Instance.ViewMoved(Brussels.Latitude, Brussels.Longitude, 7, 390, 144, true));
        await map.InvokeAsync(() => map.Instance.ViewMoved(Antwerp.Latitude, Antwerp.Longitude, 8, 390, 144, true));
        await map.InvokeAsync(() => map.Instance.ViewMoved(Antwerp.Latitude, Antwerp.Longitude, 8, 390, 256, true));

        Assert.Equal(fits + 1, context.JSInterop.Invocations["backlogTileMap.fit"].Count);
    }

    /// <summary>A pin under the controls (with the folded legend button) on a short map is
    /// out of view and a pill instead — never a hidden pin.</summary>
    [Fact]
    public async Task A_pin_under_the_short_maps_controls_is_a_pill()
    {
        using var context = Context();
        var underControls = new MapCoordinate(50.85, 5.85);
        var marks = new List<TileMapMark> { new("east", underControls, "1, East", "East", "1") };
        var map = Render(context, marks: marks);

        await map.InvokeAsync(() => map.Instance.ViewMoved(Brussels.Latitude, Brussels.Longitude, 7, 390, 144, true, 86, 30));

        Assert.Empty(map.FindAll("button.tile-map__pin"));
        Assert.Contains("tile-map__pill--right", map.Find("button.tile-map__pill").ClassList);
    }

    /// <summary>Two numbered marks pushed to the same stretch of an edge are spread apart, so
    /// neither pill covers the other.</summary>
    [Fact]
    public async Task Pills_on_the_same_stretch_of_edge_do_not_cover_each_other()
    {
        using var context = Context();
        var marks = new List<TileMapMark>
        {
            new("one", new MapCoordinate(30, 4.30), "1, One", "One", "1"),
            new("two", new MapCoordinate(30, 4.40), "2, Two", "Two", "2")
        };
        var map = Render(context, marks: marks);
        await map.InvokeAsync(() => OnBelgium(map));

        var xs = map.FindAll("button.tile-map__pill")
            .Select(pill => PositionOf(pill).X)
            .Order()
            .ToList();
        Assert.Equal(2, xs.Count);
        Assert.True(xs[1] - xs[0] >= 44 - 0.01, $"{xs[0]} and {xs[1]} overlap.");
    }

    /// <summary>A pin near the free right edge opens its tooltip leftwards, and one near the
    /// top opens it below, so the tooltip stays on the map and clear of the controls.</summary>
    [Fact]
    public async Task A_pin_near_an_edge_turns_its_tooltip_inwards()
    {
        using var context = Context();
        var marks = new List<TileMapMark>
        {
            new("east", new MapCoordinate(50.85, 6.6), "1, East", "Media Park Tech Day · 14 Dec 2026", "1"),
            new("west", new MapCoordinate(50.85, 1.5), "2, West", "Media Park Tech Day · 14 Dec 2026", "2"),
            new("north", new MapCoordinate(52.0, 4.35), "3, North", "Short", "3"),
            new("middle", Brussels, "4, Middle", "Short", "4")
        };
        var map = Render(context, marks: marks);
        await map.InvokeAsync(() => map.Instance.ViewMoved(Brussels.Latitude, Brussels.Longitude, 7, 600, 400, true, 60, 0));

        Assert.Contains("tile-map__pin--tip-end", map.Find("[data-mark-id='east']").ClassList);
        Assert.Contains("tile-map__pin--tip-start", map.Find("[data-mark-id='west']").ClassList);
        Assert.Contains("tile-map__pin--tip-below", map.Find("[data-mark-id='north']").ClassList);
        var middle = map.Find("[data-mark-id='middle']").ClassList;
        Assert.DoesNotContain("tile-map__pin--tip-end", middle);
        Assert.DoesNotContain("tile-map__pin--tip-start", middle);
        Assert.DoesNotContain("tile-map__pin--tip-below", middle);
    }

    private static TileMapScreenPoint PositionOf(AngleSharp.Dom.IElement element)
    {
        var style = element.GetAttribute("style")!;
        double Read(string name) => double.Parse(
            System.Text.RegularExpressions.Regex.Match(style, name + @":\s*([-0-9.]+)px").Groups[1].Value,
            System.Globalization.CultureInfo.InvariantCulture);
        return new TileMapScreenPoint(Read("--tile-map-x"), Read("--tile-map-y"));
    }

    [Fact]
    public async Task The_marks_in_view_are_pins_named_by_the_host_and_placed_on_their_point()
    {
        using var context = Context();
        var map = Render(context, parameters => parameters.Add(m => m.SelectedId, "dotnet-day"));

        await map.InvokeAsync(() => OnBelgium(map));

        var pins = map.FindAll("button.tile-map__pin");
        Assert.Equal(["techorama", "dotnet-day"], pins.Select(pin => pin.GetAttribute("data-mark-id")));

        var techorama = pins[0];
        Assert.Equal("1, Techorama, Going, 12 May, Antwerp", techorama.GetAttribute("aria-label"));
        Assert.Equal("false", techorama.GetAttribute("aria-pressed"));
        Assert.Contains("tile-map__mark--filled", techorama.ClassList);
        Assert.Equal("1", techorama.QuerySelector(".tile-map__label")!.TextContent);
        Assert.Equal("Techorama · 12 May", techorama.QuerySelector(".tile-map__tooltip")!.TextContent);
        Assert.Equal("true", techorama.QuerySelector(".tile-map__tooltip")!.GetAttribute("aria-hidden"));

        var expected = TileMapProjection.Project(new TileMapViewport(Brussels, 7, 600, 400), Antwerp);
        Assert.Equal(
            FormattableString.Invariant($"--tile-map-x: {expected.X:0.##}px; --tile-map-y: {expected.Y:0.##}px; --tile-map-rank: {TileMapRanks.Highest}"),
            techorama.GetAttribute("style"));

        Assert.Equal("true", pins[1].GetAttribute("aria-pressed"));
        Assert.Contains("tile-map__mark--dashed", pins[1].ClassList);
        Assert.Equal("ready", map.Find("section").GetAttribute("data-map-state"));
    }

    [Fact]
    public async Task A_numbered_mark_out_of_view_is_a_pill_on_its_edge_and_an_unnumbered_one_is_nothing()
    {
        using var context = Context();
        var map = Render(context);

        await map.InvokeAsync(() => OnBelgium(map));

        var pill = map.Find("button.tile-map__pill");
        Assert.Equal("ndc", pill.GetAttribute("data-mark-id"));
        Assert.Contains("tile-map__pill--top", pill.ClassList);
        Assert.Contains("tile-map__mark--dashed", pill.ClassList);
        Assert.Equal("3, NDC Oslo, Considering, 9 June, Oslo", pill.GetAttribute("aria-label"));
        Assert.Equal("3", pill.QuerySelector(".tile-map__label")!.TextContent);
        Assert.Single(map.FindAll("button.tile-map__pill"));
        Assert.Empty(map.FindAll("[data-mark-id='old']"));
    }

    [Fact]
    public async Task Picking_a_pin_tells_the_host_which_mark()
    {
        using var context = Context();
        string? picked = null;
        var map = Render(context, parameters => parameters.Add(m => m.SelectedIdChanged, (string? id) => picked = id));
        await map.InvokeAsync(() => OnBelgium(map));

        map.Find("[data-mark-id='dotnet-day']").Click();

        Assert.Equal("dotnet-day", picked);
        Assert.Equal("true", map.Find("[data-mark-id='dotnet-day']").GetAttribute("aria-pressed"));
    }

    [Fact]
    public async Task Picking_a_pill_brings_its_mark_into_view_and_selects_it()
    {
        using var context = Context();
        string? picked = null;
        var map = Render(context, parameters => parameters.Add(m => m.SelectedIdChanged, (string? id) => picked = id));
        await map.InvokeAsync(() => OnBelgium(map));

        map.Find("button.tile-map__pill").Click();

        var pan = Assert.Single(context.JSInterop.Invocations["backlogTileMap.panTo"]);
        Assert.Equal(59.91, pan.Arguments[1]);
        Assert.Equal(10.75, pan.Arguments[2]);
        Assert.Equal("ndc", picked);
    }

    [Fact]
    public async Task The_zoom_buttons_zoom_by_one_step_inside_the_maps_range()
    {
        using var context = Context();
        var map = Render(context, parameters => parameters.Add(m => m.MinZoom, 3).Add(m => m.MaxZoom, 9));
        await map.InvokeAsync(() => OnBelgium(map));

        var zoomIn = map.Find("[data-testid='map-zoom-in']");
        var zoomOut = map.Find("[data-testid='map-zoom-out']");
        Assert.Equal("Zoom in", zoomIn.GetAttribute("aria-label"));
        Assert.Equal("Zoom out", zoomOut.GetAttribute("aria-label"));

        zoomIn.Click();
        map.Find("[data-testid='map-zoom-out']").Click();

        Assert.Equal(
            [1, -1],
            context.JSInterop.Invocations["backlogTileMap.zoomBy"].Select(invocation => (int)invocation.Arguments[1]!));

        await map.InvokeAsync(() => OnBelgium(map, zoom: 9));
        Assert.True(map.Find("[data-testid='map-zoom-in']").HasAttribute("disabled"));

        await map.InvokeAsync(() => OnBelgium(map, zoom: 3));
        Assert.True(map.Find("[data-testid='map-zoom-out']").HasAttribute("disabled"));
    }

    [Fact]
    public async Task Fit_all_frames_every_numbered_mark()
    {
        using var context = Context();
        var map = Render(context, parameters => parameters.Add(m => m.FitLabel, "Fit all events"));
        await map.InvokeAsync(() => OnBelgium(map));

        var fitButton = map.Find("[data-testid='map-fit']");
        Assert.Equal("Fit all events", fitButton.GetAttribute("aria-label"));
        fitButton.Click();

        var fit = Json(Assert.Single(context.JSInterop.Invocations["backlogTileMap.fit"]).Arguments[1]);
        Assert.Equal(
            [51.05, 3.72, 59.91, 10.75],
            fit.GetProperty("bounds").EnumerateArray().Select(value => value.GetDouble()).ToArray());
    }

    [Fact]
    public async Task The_host_hears_of_a_view_only_once_it_has_settled()
    {
        using var context = Context();
        var heard = new List<TileMapViewport>();
        var map = Render(context, parameters => parameters.Add(m => m.ViewChanged, (TileMapViewport view) => heard.Add(view)));

        await map.InvokeAsync(() => OnBelgium(map, settled: false));
        Assert.Empty(heard);
        Assert.NotEmpty(map.FindAll("button.tile-map__pin"));

        await map.InvokeAsync(() => OnBelgium(map, settled: true));
        var view = Assert.Single(heard);
        Assert.Equal(new TileMapViewport(Brussels, 7, 600, 400), view);
        Assert.Equal(view, map.Instance.Viewport);
    }

    [Fact]
    public async Task Tiles_that_cannot_be_reached_show_the_offline_note_in_place_of_the_marks()
    {
        using var context = Context();
        var map = Render(context, parameters => parameters
            .Add(m => m.Legend, builder => builder.AddContent(0, "Going")));
        await map.InvokeAsync(() => OnBelgium(map));

        await map.InvokeAsync(() => map.Instance.TilesReachable(false));

        Assert.Equal("offline", map.Find("section").GetAttribute("data-map-state"));
        Assert.Contains("The map needs a connection.", map.Find("[data-testid='map-offline']").TextContent);
        Assert.Empty(map.FindAll("button.tile-map__pin"));
        Assert.Empty(map.FindAll("button.tile-map__pill"));
        Assert.Empty(map.FindAll(".tile-map__controls"));
        Assert.Empty(map.FindAll(".tile-map__legend"));
        // The tiles' container stays, so the map can recover without starting again.
        Assert.Single(map.FindAll(".tile-map__tiles"));

        map.Find("[data-testid='map-offline'] button").Click();
        Assert.Single(context.JSInterop.Invocations["backlogTileMap.retry"]);

        await map.InvokeAsync(() => map.Instance.TilesReachable(true));

        Assert.Empty(map.FindAll("[data-testid='map-offline']"));
        Assert.NotEmpty(map.FindAll("button.tile-map__pin"));
    }

    /// <summary>A new source over the offline note: the note stays until the map
    /// says the new tiles load, and then goes — it was left up over working tiles
    /// when the source change reset the state with no render.</summary>
    [Fact]
    public async Task A_new_source_whose_tiles_load_clears_the_offline_note()
    {
        using var context = Context();
        var map = Render(context);
        await map.InvokeAsync(() => OnBelgium(map));
        await map.InvokeAsync(() => map.Instance.TilesReachable(false));

        map.Render(parameters => parameters.Add(m => m.TileUrlTemplate, "https://tiles.test/new/{z}/{x}/{y}.png"));
        Assert.Single(context.JSInterop.Invocations["backlogTileMap.setTiles"]);

        await map.InvokeAsync(() => map.Instance.TilesReachable(true));

        Assert.Empty(map.FindAll("[data-testid='map-offline']"));
        Assert.Equal("ready", map.Find("section").GetAttribute("data-map-state"));
    }

    /// <summary>The first map of a session waits for leaflet.js. Marks and a new
    /// source that arrive meanwhile are still handed over — the script holds them
    /// until the map exists (<c>TileMapScriptTests</c>) — rather than being taken
    /// as done and dropped.</summary>
    [Fact]
    public void Marks_and_a_source_that_arrive_while_the_map_is_starting_are_still_handed_over()
    {
        using var context = new BunitContext();
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        var starting = context.JSInterop.SetupVoid("backlogTileMap.initialize", _ => true);
        var map = Render(context, marks: []);

        map.Render(parameters => parameters
            .Add(m => m.Marks, Marks())
            .Add(m => m.TileUrlTemplate, "https://tiles.test/new/{z}/{x}/{y}.png"));

        Assert.Single(context.JSInterop.Invocations["backlogTileMap.fit"]);
        Assert.Single(context.JSInterop.Invocations["backlogTileMap.setTiles"]);

        starting.SetVoidResult();
        map.Render(parameters => parameters.Add(m => m.Marks, Marks().Take(2).ToList()));

        Assert.Single(context.JSInterop.Invocations["backlogTileMap.initialize"]);
        Assert.Single(context.JSInterop.Invocations["backlogTileMap.fit"]);
    }

    /// <summary>A pill picked from the keyboard pans its mark into view, where it is
    /// drawn as a pin — a different button. Focus follows it there instead of
    /// falling to the page.</summary>
    [Fact]
    public async Task Picking_a_pill_moves_the_focus_to_its_pin_once_it_is_in_view()
    {
        using var context = Context();
        var map = Render(context);
        await map.InvokeAsync(() => OnBelgium(map));

        map.Find("button.tile-map__pill").Click();
        Assert.Empty(context.JSInterop.Invocations["backlogFocus"]);

        await map.InvokeAsync(() => map.Instance.ViewMoved(Oslo.Latitude, Oslo.Longitude, 7, 600, 400, true));

        var pin = map.Find("button.tile-map__pin[data-mark-id='ndc']");
        var focus = Assert.Single(context.JSInterop.Invocations["backlogFocus"]);
        Assert.Equal(pin.Id, focus.Arguments[0]);
        Assert.False(string.IsNullOrEmpty(pin.Id));

        await map.InvokeAsync(() => OnBelgium(map));
        Assert.Single(context.JSInterop.Invocations["backlogFocus"]);
    }

    [Fact]
    public async Task Each_pin_has_an_id_of_its_own()
    {
        using var context = Context();
        var map = Render(context);
        await map.InvokeAsync(() => OnBelgium(map));

        var ids = map.FindAll("button.tile-map__pin, button.tile-map__pill").Select(button => button.Id).ToList();

        Assert.All(ids, id => Assert.False(string.IsNullOrEmpty(id)));
        Assert.Equal(ids.Count, ids.Distinct().Count());
    }

    /// <summary>The legend and the attribution share one foot, which wraps rather
    /// than letting the two overlap on a narrow map.</summary>
    [Fact]
    public async Task The_legend_and_the_attribution_share_the_maps_foot()
    {
        using var context = Context();
        var map = Render(context, parameters => parameters
            .Add(m => m.Legend, builder => builder.AddContent(0, "Going")));
        await map.InvokeAsync(() => OnBelgium(map));

        var foot = map.Find(".tile-map__foot");
        Assert.NotNull(foot.QuerySelector("[data-testid='map-legend']"));
        Assert.NotNull(foot.QuerySelector("[data-testid='map-attribution']"));
    }

    [Fact]
    public async Task A_map_library_that_will_not_load_says_the_map_could_not_be_drawn()
    {
        using var context = Context();
        var map = Render(context);

        await map.InvokeAsync(() => map.Instance.MapUnavailable());

        Assert.Equal("unavailable", map.Find("section").GetAttribute("data-map-state"));
        Assert.Contains("The map could not be drawn.", map.Find("[data-testid='map-unavailable']").TextContent);
        Assert.Empty(map.FindAll(".tile-map__attribution"));
    }

    [Fact]
    public async Task The_attribution_and_the_legend_sit_on_the_map()
    {
        using var context = Context();
        var map = Render(context, parameters => parameters
            .Add(m => m.Legend, builder => builder.AddContent(0, "Going / Spotted or considering / Past")));

        Assert.Equal("© Microsoft, © TomTom", map.Find("[data-testid='map-attribution']").TextContent);

        await map.InvokeAsync(() => OnBelgium(map));

        Assert.Equal("Going / Spotted or considering / Past", map.Find("[data-testid='map-legend']").TextContent);
    }

    [Fact]
    public void A_new_tile_source_is_handed_to_the_running_map()
    {
        using var context = Context();
        var map = Render(context);

        map.Render(parameters => parameters.Add(m => m.TileUrlTemplate, "https://tiles.test/other/{z}/{x}/{y}.png"));

        var invocation = Assert.Single(context.JSInterop.Invocations["backlogTileMap.setTiles"]);
        Assert.Equal("https://tiles.test/other/{z}/{x}/{y}.png", invocation.Arguments[1]);
        Assert.Single(context.JSInterop.Invocations["backlogTileMap.initialize"]);
    }

    [Fact]
    public void A_tile_source_taken_away_releases_the_map_and_shows_the_note()
    {
        using var context = Context();
        var map = Render(context);

        map.Render(parameters => parameters.Add(m => m.TileUrlTemplate, null));

        Assert.Single(context.JSInterop.Invocations["backlogTileMap.dispose"]);
        Assert.Single(map.FindAll("[data-testid='map-missing-source']"));
    }

    [Fact]
    public void Marks_that_arrive_after_the_map_started_are_framed_once()
    {
        using var context = Context();
        var map = Render(context, marks: []);

        map.Render(parameters => parameters.Add(m => m.Marks, Marks()));
        map.Render(parameters => parameters.Add(m => m.Marks, Marks().Take(2).ToList()));

        Assert.Single(context.JSInterop.Invocations["backlogTileMap.fit"]);
    }

    [Fact]
    public async Task Disposing_the_map_releases_it()
    {
        using var context = Context();
        Render(context);

        await context.DisposeComponentsAsync();

        Assert.Single(context.JSInterop.Invocations["backlogTileMap.dispose"]);
    }
}
