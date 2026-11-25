namespace Backlog.UI.Components.UnitTests;

/// <summary>
/// The arithmetic behind every pin and pill on a tile map. It has to agree with
/// Leaflet, which draws the tiles under the pins: a pin placed by a different
/// projection would stand a street or a country away from its place.
/// </summary>
public sealed class TileMapProjectionTests
{
    private static readonly MapCoordinate Brussels = new(50.85, 4.35);
    private static readonly MapCoordinate Antwerp = new(51.22, 4.40);
    private static readonly MapCoordinate Oslo = new(59.91, 10.75);
    private static readonly MapCoordinate Lisbon = new(38.72, -9.14);

    /// <summary>Belgium filling a 600 by 400 map, the requirement's own picture.</summary>
    private static TileMapViewport OnBelgium() => new(Brussels, 7, 600, 400);

    [Fact]
    public void The_centre_of_the_view_is_the_middle_of_the_map()
    {
        var position = TileMapProjection.Project(OnBelgium(), Brussels);

        Assert.Equal(300, position.X, 6);
        Assert.Equal(200, position.Y, 6);
    }

    [Fact]
    public void At_zoom_nought_the_world_is_one_tile()
    {
        var world = new TileMapViewport(new MapCoordinate(0, 0), 0, 256, 256);

        Assert.Equal(new TileMapScreenPoint(0, 128), Rounded(TileMapProjection.Project(world, new MapCoordinate(0, -180))));
        Assert.Equal(new TileMapScreenPoint(256, 128), Rounded(TileMapProjection.Project(world, new MapCoordinate(0, 180))));
        Assert.Equal(0, TileMapProjection.Project(world, new MapCoordinate(TileMapProjection.MaxLatitude, 0)).Y, 3);
        Assert.Equal(256, TileMapProjection.Project(world, new MapCoordinate(-TileMapProjection.MaxLatitude, 0)).Y, 3);
    }

    /// <summary>The slippy-map tile numbering every tile server shares: London at
    /// zoom 10 is tile 511, 340. A pixel outside that tile would put the pin on a
    /// different tile from the one the server draws London on.</summary>
    [Fact]
    public void A_point_lands_on_the_tile_the_tile_servers_draw_it_on()
    {
        var origin = new TileMapViewport(new MapCoordinate(0, 0), 10, 0, 0);
        var half = TileMapProjection.TileSize * Math.Pow(2, 10) / 2;

        var position = TileMapProjection.Project(origin, new MapCoordinate(51.5, -0.09));

        Assert.Equal(511, Math.Floor((position.X + half) / TileMapProjection.TileSize));
        Assert.Equal(340, Math.Floor((position.Y + half) / TileMapProjection.TileSize));
    }

    [Fact]
    public void A_latitude_past_the_projections_edge_is_held_at_it()
    {
        var world = new TileMapViewport(new MapCoordinate(0, 0), 0, 256, 256);

        Assert.Equal(0, TileMapProjection.Project(world, new MapCoordinate(90, 0)).Y, 3);
        Assert.Equal(256, TileMapProjection.Project(world, new MapCoordinate(-90, 0)).Y, 3);
    }

    [Fact]
    public void One_zoom_step_doubles_every_distance_from_the_centre()
    {
        var near = TileMapProjection.Project(OnBelgium(), Antwerp);
        var closer = TileMapProjection.Project(OnBelgium() with { Zoom = 8 }, Antwerp);

        Assert.Equal((near.X - 300) * 2, closer.X - 300, 6);
        Assert.Equal((near.Y - 200) * 2, closer.Y - 200, 6);
    }

    [Fact]
    public void North_is_up_and_east_is_right()
    {
        var antwerp = TileMapProjection.Project(OnBelgium(), Antwerp);
        var lisbon = TileMapProjection.Project(OnBelgium(), Lisbon);

        Assert.True(antwerp.Y < 200);
        Assert.True(lisbon.X < 300);
        Assert.True(lisbon.Y > 200);
    }

    [Fact]
    public void A_point_on_the_map_is_in_view_and_one_beyond_it_is_not()
    {
        Assert.True(TileMapProjection.IsInView(OnBelgium(), Antwerp));
        Assert.False(TileMapProjection.IsInView(OnBelgium(), Oslo));
        Assert.False(TileMapProjection.IsInView(OnBelgium(), Lisbon));
    }

    [Fact]
    public void A_map_with_no_size_yet_holds_nothing()
    {
        Assert.False(TileMapProjection.IsInView(OnBelgium() with { Width = 0, Height = 0 }, Brussels));
    }

    /// <summary>The requirement's scenario: the map zoomed on Belgium puts
    /// Oslo's pill on the top edge.</summary>
    [Fact]
    public void Oslo_seen_from_Belgium_sits_on_the_top_edge()
    {
        var placement = TileMapProjection.EdgePlacement(OnBelgium(), Oslo, inset: 16);

        Assert.NotNull(placement);
        Assert.Equal(TileMapEdge.Top, placement.Value.Edge);
        Assert.Equal(16, placement.Value.Position.Y, 6);
        Assert.InRange(placement.Value.Position.X, 300, 600 - 16);
    }

    [Theory]
    [InlineData(50.85, -20, TileMapEdge.Left)]
    [InlineData(50.85, 30, TileMapEdge.Right)]
    [InlineData(30, 4.35, TileMapEdge.Bottom)]
    [InlineData(70, 4.35, TileMapEdge.Top)]
    public void A_point_out_of_view_goes_to_the_edge_in_its_direction(double latitude, double longitude, TileMapEdge edge)
    {
        var placement = TileMapProjection.EdgePlacement(OnBelgium(), new MapCoordinate(latitude, longitude), inset: 16);

        Assert.NotNull(placement);
        Assert.Equal(edge, placement.Value.Edge);
    }

    [Fact]
    public void An_edge_pill_stays_inside_the_map_by_its_inset()
    {
        foreach (var point in new[] { Oslo, Lisbon, new MapCoordinate(-40, 170), new MapCoordinate(80, -170) })
        {
            var placement = TileMapProjection.EdgePlacement(OnBelgium(), point, inset: 20);

            Assert.NotNull(placement);
            Assert.InRange(placement.Value.Position.X, 20 - 1e-9, 580 + 1e-9);
            Assert.InRange(placement.Value.Position.Y, 20 - 1e-9, 380 + 1e-9);
        }
    }

    [Fact]
    public void A_point_in_view_has_no_edge_placement()
    {
        Assert.Null(TileMapProjection.EdgePlacement(OnBelgium(), Antwerp, inset: 16));
    }

    [Fact]
    public void A_map_smaller_than_twice_the_inset_places_no_pill()
    {
        Assert.Null(TileMapProjection.EdgePlacement(OnBelgium() with { Width = 30, Height = 30 }, Oslo, inset: 16));
    }

    [Fact]
    public void A_negative_inset_is_refused()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => TileMapProjection.EdgePlacement(OnBelgium(), Oslo, inset: -1));
    }

    [Fact]
    public void The_bounds_hold_every_point_and_none_is_no_bounds()
    {
        Assert.Null(TileMapProjection.Bounds([]));

        var bounds = TileMapProjection.Bounds([Brussels, Oslo, Lisbon]);

        Assert.Equal(new MapBounds(38.72, -9.14, 59.91, 10.75), bounds);
    }

    [Fact]
    public void One_point_is_a_box_of_no_size()
    {
        Assert.Equal(new MapBounds(50.85, 4.35, 50.85, 4.35), TileMapProjection.Bounds([Brussels]));
    }

    /// <summary>The map's own chrome — the zoom controls down the right, the legend and
    /// attribution along the foot — covers the tiles under it. A point there is not in view:
    /// a pin there could not be seen or picked.</summary>
    [Fact]
    public void A_point_under_the_maps_chrome_is_not_in_view()
    {
        var chromed = OnBelgium() with { Reserved = new TileMapInsets(0, 60, 80, 0) };
        var underFoot = new MapCoordinate(49.75, 4.35);
        var underControls = new MapCoordinate(50.85, 7.35);

        Assert.True(TileMapProjection.IsInView(OnBelgium(), underFoot));
        Assert.True(TileMapProjection.IsInView(OnBelgium(), underControls));
        Assert.InRange(TileMapProjection.Project(chromed, underFoot).Y, 320, 400);
        Assert.InRange(TileMapProjection.Project(chromed, underControls).X, 540, 600);

        Assert.False(TileMapProjection.IsInView(chromed, underFoot));
        Assert.False(TileMapProjection.IsInView(chromed, underControls));
        Assert.True(TileMapProjection.IsInView(chromed, Brussels));
    }

    /// <summary>An edge pill sits on the edge of the part of the map the chrome leaves free,
    /// never under the legend or the controls.</summary>
    [Fact]
    public void An_edge_pill_stays_clear_of_the_maps_chrome()
    {
        var chromed = OnBelgium() with { Reserved = new TileMapInsets(0, 60, 80, 0) };

        foreach (var point in new[] { Oslo, Lisbon, new MapCoordinate(50.85, 30), new MapCoordinate(30, 4.35), new MapCoordinate(49.75, 4.35) })
        {
            var placement = TileMapProjection.EdgePlacement(chromed, point, inset: 24);

            Assert.NotNull(placement);
            Assert.InRange(placement.Value.Position.X, 24 - 1e-9, 600 - 60 - 24 + 1e-9);
            Assert.InRange(placement.Value.Position.Y, 24 - 1e-9, 400 - 80 - 24 + 1e-9);
        }

        Assert.Equal(TileMapEdge.Bottom, TileMapProjection.EdgePlacement(chromed, new MapCoordinate(49.75, 4.35), inset: 24)!.Value.Edge);
    }

    /// <summary>Pills pushed to the same stretch of an edge are spread along it, a press
    /// target apart, in the order their points lie — so each can be picked.</summary>
    [Fact]
    public void Pills_on_the_same_stretch_of_an_edge_are_spread_apart_in_order()
    {
        var pills = TileMapProjection.PlacePills(
            OnBelgium(),
            [("a", new MapCoordinate(30, 4.30)), ("b", new MapCoordinate(30, 4.35)), ("c", new MapCoordinate(30, 4.40))],
            inset: 24,
            spacing: 44);

        Assert.Equal(["a", "b", "c"], pills.OrderBy(pill => pill.Value.Position.X).Select(pill => pill.Key));
        Assert.All(pills.Values, pill => Assert.Equal(TileMapEdge.Bottom, pill.Edge));
        var xs = pills.Values.Select(pill => pill.Position.X).Order().ToList();
        Assert.True(xs[1] - xs[0] >= 44 - 1e-9);
        Assert.True(xs[2] - xs[1] >= 44 - 1e-9);
        Assert.All(pills.Values, pill => Assert.Equal(400 - 24, pill.Position.Y, 6));
    }

    /// <summary>Spread past the end of the free edge, the pills carry on round the corner
    /// instead of running off the map or under the chrome, still a press target apart.</summary>
    [Fact]
    public void Pills_crowded_at_the_end_of_an_edge_stay_inside_and_apart()
    {
        var chromed = OnBelgium() with { Reserved = new TileMapInsets(0, 60, 80, 0) };

        var pills = TileMapProjection.PlacePills(
            chromed,
            [("a", new MapCoordinate(30, 60)), ("b", new MapCoordinate(30.1, 60)), ("c", new MapCoordinate(30.2, 60))],
            inset: 24,
            spacing: 44);

        Assert.Equal(3, pills.Count);
        AssertApart(pills.Values, 44);
        AssertInside(pills.Values, 24, 24, 600 - 60 - 24, 400 - 80 - 24);
    }

    /// <summary>Pills on two edges near the shared corner keep a press target apart too.</summary>
    [Fact]
    public void Pills_either_side_of_a_corner_keep_apart()
    {
        var pills = TileMapProjection.PlacePills(
            OnBelgium(),
            [("ne", new MapCoordinate(62.5, 40)), ("top", new MapCoordinate(75, 20))],
            inset: 24,
            spacing: 44);

        Assert.Equal(2, pills.Count);
        AssertApart(pills.Values, 44);
    }

    /// <summary>
    /// The short maps the narrow layout draws: 390px wide and 128 or 160 tall, the chrome
    /// reserving a strip down the right and a foot. Four close marks out of view are four
    /// pills — none is dropped — each a press target from the next and on the map.
    /// </summary>
    [Theory]
    [InlineData(128)]
    [InlineData(160)]
    public void Four_close_marks_off_a_short_map_are_four_pickable_pills(double height)
    {
        var shortMap = new TileMapViewport(new MapCoordinate(48.0, 2.0), 7, 390, height, new TileMapInsets(0, 44, 70, 0));

        var pills = TileMapProjection.PlacePills(shortMap, CloseMarks(), inset: 24, spacing: 44);

        Assert.Equal(["amsterdam", "hilversum", "nieuwegein", "utrecht"], pills.Keys.Order(StringComparer.Ordinal));
        AssertApart(pills.Values, 44);
        AssertInside(pills.Values, 0, 0, 390 - 44, height - 70);
    }

    /// <summary>Chrome reaching past the whole map leaves no free part: the map is taken
    /// whole rather than holding nothing, so a mark is still a pin or a pill.</summary>
    [Fact]
    public void Chrome_that_covers_the_whole_map_is_not_reserved()
    {
        var covered = new TileMapViewport(new MapCoordinate(52.2, 5.2), 8, 390, 128, new TileMapInsets(0, 44, 140, 0));

        Assert.All(CloseMarks(), mark => Assert.True(TileMapProjection.IsInView(covered, mark.Point)));
        Assert.Empty(TileMapProjection.PlacePills(covered, CloseMarks(), inset: 24, spacing: 44));
    }

    /// <summary>Every out-of-view mark gets a pill, however small the free part is.</summary>
    [Fact]
    public void A_free_part_smaller_than_the_inset_still_places_every_pill()
    {
        var tiny = new TileMapViewport(new MapCoordinate(48.0, 2.0), 7, 60, 40, new TileMapInsets(0, 10, 10, 0));

        var pills = TileMapProjection.PlacePills(tiny, CloseMarks(), inset: 24, spacing: 44);

        Assert.Equal(4, pills.Count);
        AssertInside(pills.Values, 0, 0, 50, 30);
    }

    /// <summary>Utrecht, Nieuwegein, Hilversum and Amsterdam: the re-verify's four.</summary>
    private static (string Id, MapCoordinate Point)[] CloseMarks() =>
    [
        ("utrecht", new MapCoordinate(52.09, 5.12)),
        ("nieuwegein", new MapCoordinate(52.03, 5.09)),
        ("hilversum", new MapCoordinate(52.22, 5.18)),
        ("amsterdam", new MapCoordinate(52.37, 4.90))
    ];

    private static void AssertApart(IEnumerable<TileMapEdgePlacement> pills, double spacing)
    {
        var list = pills.ToList();
        for (var i = 0; i < list.Count; i++)
        {
            for (var j = i + 1; j < list.Count; j++)
            {
                var dx = list[i].Position.X - list[j].Position.X;
                var dy = list[i].Position.Y - list[j].Position.Y;
                Assert.True(Math.Sqrt((dx * dx) + (dy * dy)) >= spacing - 1e-6, $"{list[i]} and {list[j]} are closer than {spacing}.");
            }
        }
    }

    private static void AssertInside(IEnumerable<TileMapEdgePlacement> pills, double left, double top, double right, double bottom)
    {
        Assert.All(pills, pill =>
        {
            Assert.InRange(pill.Position.X, left - 1e-6, right + 1e-6);
            Assert.InRange(pill.Position.Y, top - 1e-6, bottom + 1e-6);
        });
    }

    [Fact]
    public void A_point_in_view_gets_no_pill()
    {
        Assert.Empty(TileMapProjection.PlacePills(OnBelgium(), [("antwerp", Antwerp)], inset: 24, spacing: 44));
    }

    private static TileMapScreenPoint Rounded(TileMapScreenPoint point) =>
        new(Math.Round(point.X, 6), Math.Round(point.Y, 6));
}
