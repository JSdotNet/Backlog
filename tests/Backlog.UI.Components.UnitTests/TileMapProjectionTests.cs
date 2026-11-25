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

    private static TileMapScreenPoint Rounded(TileMapScreenPoint point) =>
        new(Math.Round(point.X, 6), Math.Round(point.Y, 6));
}
