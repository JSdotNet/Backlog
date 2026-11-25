namespace Backlog.UI.Components.Diagrams;

/// <summary>
/// Where a point stands on a <see cref="TileMap"/>, worked out in C# so the pins,
/// the edge pills and a host's "is it in view" all have one answer that a test can
/// read without a browser.
///
/// <para>The projection is Leaflet's own: spherical Web Mercator (EPSG:3857) on
/// 256-pixel tiles, latitude clamped at Leaflet's 85.0511287798°, longitude not
/// wrapped. A pin placed here therefore lands on the pixel Leaflet's
/// <c>latLngToContainerPoint</c> would give for the viewport it reported.</para>
/// </summary>
public static class TileMapProjection
{
    /// <summary>Leaflet's tile size, which fixes the scale of zoom 0.</summary>
    public const double TileSize = 256;

    /// <summary>Leaflet's <c>SphericalMercator.MAX_LATITUDE</c>: past it the
    /// projection runs to infinity.</summary>
    public const double MaxLatitude = 85.0511287798;

    /// <summary>The point's position on the map, in CSS pixels from its top-left
    /// corner. Off the map is a position outside <c>0..Width</c> or
    /// <c>0..Height</c>, never a refusal.</summary>
    public static TileMapScreenPoint Project(TileMapViewport viewport, MapCoordinate point)
    {
        ArgumentNullException.ThrowIfNull(viewport);

        var scale = TileSize * Math.Pow(2, viewport.Zoom);
        var (pointX, pointY) = World(point, scale);
        var (centerX, centerY) = World(viewport.Center, scale);

        return new TileMapScreenPoint(
            pointX - centerX + (viewport.Width / 2),
            pointY - centerY + (viewport.Height / 2));
    }

    /// <summary>Whether the point is on the part of the map the chrome leaves free
    /// (<see cref="TileMapViewport.Reserved"/>), its edges included. A pin under the
    /// legend or the controls could be neither seen nor picked, so it is not in view.
    /// A viewport with no free part holds nothing.</summary>
    public static bool IsInView(TileMapViewport viewport, MapCoordinate point)
    {
        ArgumentNullException.ThrowIfNull(viewport);

        var (left, top, right, bottom) = Free(viewport);
        if (right <= left || bottom <= top) return false;

        var position = Project(viewport, point);
        return position.X >= left && position.X <= right
            && position.Y >= top && position.Y <= bottom;
    }

    /// <summary>
    /// Where the pill for an out-of-view point goes: on the ray from the centre of
    /// the map's free part towards the point, where it crosses that part's edge
    /// drawn <paramref name="inset"/> pixels inside, so the whole pill stays on the
    /// map and clear of its chrome. The edge is the one that ray crosses, which is
    /// the nearest edge in the point's direction — a point due north is on the top
    /// edge however far away. Null for a point in view, and for a free part too
    /// small to hold the inset.
    /// </summary>
    public static TileMapEdgePlacement? EdgePlacement(TileMapViewport viewport, MapCoordinate point, double inset = 0)
    {
        ArgumentNullException.ThrowIfNull(viewport);
        ArgumentOutOfRangeException.ThrowIfNegative(inset);

        if (IsInView(viewport, point)) return null;

        var (left, top, right, bottom) = Free(viewport);
        var halfWidth = ((right - left) / 2) - inset;
        var halfHeight = ((bottom - top) / 2) - inset;
        if (halfWidth <= 0 || halfHeight <= 0) return null;

        var centerX = (left + right) / 2;
        var centerY = (top + bottom) / 2;
        var position = Project(viewport, point);
        var dx = position.X - centerX;
        var dy = position.Y - centerY;

        var alongX = dx == 0 ? double.PositiveInfinity : halfWidth / Math.Abs(dx);
        var alongY = dy == 0 ? double.PositiveInfinity : halfHeight / Math.Abs(dy);
        var along = Math.Min(alongX, alongY);

        var edge = alongX < alongY
            ? (dx < 0 ? TileMapEdge.Left : TileMapEdge.Right)
            : (dy < 0 ? TileMapEdge.Top : TileMapEdge.Bottom);

        return new TileMapEdgePlacement(
            edge,
            new TileMapScreenPoint(centerX + (dx * along), centerY + (dy * along)));
    }

    /// <summary>
    /// The pills for the out-of-view points, by id — every one of them: a numbered mark
    /// that is not a pin is always a pill. Each starts where the ray from the free part's
    /// centre towards its point crosses the free part drawn <paramref name="inset"/> inside
    /// (less, when the free part is smaller than that), and the pills are then spread so no
    /// two are closer than <paramref name="spacing"/> — a press target apart, so each can be
    /// picked — keeping the order their points lie in.
    /// <para>
    /// They spread along the whole edge, round the corners: a run too long for one edge
    /// carries on along the next. Around the edge they are kept a spacing times √2 apart,
    /// so two either side of a corner are still a spacing apart in a straight line. A free
    /// part narrower than a spacing either way — the short map of the narrow layout — has no
    /// room for pills on two facing edges, so they line up along its long middle instead.
    /// More pills than the edge holds are spaced evenly and overlap; there is nowhere else
    /// on the map to put them, and the list reaches every one.
    /// </para>
    /// </summary>
    public static IReadOnlyDictionary<string, TileMapEdgePlacement> PlacePills(
        TileMapViewport viewport,
        IEnumerable<(string Id, MapCoordinate Point)> points,
        double inset,
        double spacing)
    {
        ArgumentNullException.ThrowIfNull(viewport);
        ArgumentNullException.ThrowIfNull(points);
        ArgumentOutOfRangeException.ThrowIfNegative(inset);
        ArgumentOutOfRangeException.ThrowIfNegative(spacing);

        var (left, top, right, bottom) = Free(viewport);
        var edgeInset = Math.Max(0, Math.Min(inset, Math.Min((right - left) / 2, (bottom - top) / 2)));
        var frame = new Frame(left + edgeInset, top + edgeInset, right - edgeInset, bottom - edgeInset);

        var pills = points
            .Where(point => !IsInView(viewport, point.Point))
            .Select(point => (point.Id, Start: frame.Cross(Project(viewport, point.Point))))
            .ToList();
        if (pills.Count == 0) return new Dictionary<string, TileMapEdgePlacement>(StringComparer.Ordinal);

        return frame.Width >= spacing && frame.Height >= spacing
            ? AroundTheEdge(frame, pills, spacing * Math.Sqrt(2))
            : AlongTheMiddle(frame, pills, spacing);
    }

    /// <summary>The pills on the frame's edge, clockwise from its top-left corner, spread
    /// <paramref name="gap"/> apart along it.</summary>
    private static Dictionary<string, TileMapEdgePlacement> AroundTheEdge(
        Frame frame,
        List<(string Id, (TileMapEdge Edge, TileMapScreenPoint Position) Start)> pills,
        double gap)
    {
        var length = 2 * (frame.Width + frame.Height);
        var spread = SpreadOnLoop(
            pills.Select(pill => (pill.Id, Along: frame.Along(pill.Start.Edge, pill.Start.Position))).ToList(),
            length,
            gap);

        var placed = new Dictionary<string, TileMapEdgePlacement>(StringComparer.Ordinal);
        foreach (var (id, along) in spread)
        {
            var (edge, position) = frame.At(along);
            placed[id] = new TileMapEdgePlacement(edge, position);
        }

        return placed;
    }

    /// <summary>The pills on the frame's long middle line, spread <paramref name="gap"/>
    /// apart along it, each keeping the edge its point lies beyond for its arrow.</summary>
    private static Dictionary<string, TileMapEdgePlacement> AlongTheMiddle(
        Frame frame,
        List<(string Id, (TileMapEdge Edge, TileMapScreenPoint Position) Start)> pills,
        double gap)
    {
        var across = frame.Width >= frame.Height;
        var low = across ? frame.Left : frame.Top;
        var high = across ? frame.Right : frame.Bottom;

        var run = pills
            .OrderBy(pill => across ? pill.Start.Position.X : pill.Start.Position.Y)
            .ThenBy(pill => pill.Id, StringComparer.Ordinal)
            .ToList();
        var along = run.Select(pill => Math.Clamp(across ? pill.Start.Position.X : pill.Start.Position.Y, low, high)).ToArray();

        if (along.Length > 1 && (along.Length - 1) * gap > high - low)
        {
            for (var i = 0; i < along.Length; i++) along[i] = low + (i * (high - low) / (along.Length - 1));
        }
        else
        {
            for (var i = 1; i < along.Length; i++) along[i] = Math.Max(along[i], along[i - 1] + gap);
            if (along.Length > 0) along[^1] = Math.Min(along[^1], high);
            for (var i = along.Length - 2; i >= 0; i--) along[i] = Math.Min(along[i], along[i + 1] - gap);
        }

        var placed = new Dictionary<string, TileMapEdgePlacement>(StringComparer.Ordinal);
        for (var i = 0; i < run.Count; i++)
        {
            var position = across
                ? new TileMapScreenPoint(along[i], frame.CenterY)
                : new TileMapScreenPoint(frame.CenterX, along[i]);
            placed[run[i].Id] = new TileMapEdgePlacement(run[i].Start.Edge, position);
        }

        return placed;
    }

    /// <summary>
    /// Positions on a loop of <paramref name="length"/>, moved as little as the order allows
    /// so that no two neighbours are closer than <paramref name="gap"/>. The loop is cut at
    /// its widest gap and spread forward from there, then pulled back so the run closes the
    /// loop no closer than a gap either. With no room for that, they are spaced evenly.
    /// </summary>
    private static List<(string Id, double Along)> SpreadOnLoop(List<(string Id, double Along)> points, double length, double gap)
    {
        var sorted = points.OrderBy(point => point.Along).ThenBy(point => point.Id, StringComparer.Ordinal).ToList();
        var count = sorted.Count;
        if (count < 2 || length <= 0) return sorted;

        var widest = count - 1;
        var widestGap = sorted[0].Along + length - sorted[^1].Along;
        for (var i = 0; i < count - 1; i++)
        {
            var between = sorted[i + 1].Along - sorted[i].Along;
            if (between > widestGap)
            {
                widestGap = between;
                widest = i;
            }
        }

        var ids = new string[count];
        var along = new double[count];
        for (var j = 0; j < count; j++)
        {
            var index = (widest + 1 + j) % count;
            ids[j] = sorted[index].Id;
            along[j] = sorted[index].Along + (index <= widest && widest != count - 1 ? length : 0);
        }

        if (count * gap > length)
        {
            for (var j = 1; j < count; j++) along[j] = along[0] + (j * length / count);
        }
        else
        {
            for (var j = 1; j < count; j++) along[j] = Math.Max(along[j], along[j - 1] + gap);
            along[^1] = Math.Min(along[^1], along[0] + length - gap);
            for (var j = count - 2; j >= 0; j--) along[j] = Math.Min(along[j], along[j + 1] - gap);
        }

        return [.. ids.Select((id, j) => (id, ((along[j] % length) + length) % length))];
    }

    /// <summary>The rectangle the pills are placed on: the free part of the map, drawn the
    /// inset inside it. Measured clockwise from its top-left corner, its edge runs along the
    /// top, down the right, back along the bottom and up the left.</summary>
    private readonly record struct Frame(double Left, double Top, double Right, double Bottom)
    {
        public double Width => Right - Left;

        public double Height => Bottom - Top;

        public double CenterX => (Left + Right) / 2;

        public double CenterY => (Top + Bottom) / 2;

        /// <summary>Where the ray from the centre towards <paramref name="position"/> crosses
        /// the frame, and which edge it crosses.</summary>
        public (TileMapEdge Edge, TileMapScreenPoint Position) Cross(TileMapScreenPoint position)
        {
            var dx = position.X - CenterX;
            var dy = position.Y - CenterY;
            if (dx == 0 && dy == 0) return (TileMapEdge.Top, new TileMapScreenPoint(CenterX, Top));

            var alongX = dx == 0 ? double.PositiveInfinity : (Width / 2) / Math.Abs(dx);
            var alongY = dy == 0 ? double.PositiveInfinity : (Height / 2) / Math.Abs(dy);
            var along = Math.Min(alongX, alongY);
            var edge = alongX < alongY
                ? (dx < 0 ? TileMapEdge.Left : TileMapEdge.Right)
                : (dy < 0 ? TileMapEdge.Top : TileMapEdge.Bottom);

            return (edge, new TileMapScreenPoint(CenterX + (dx * along), CenterY + (dy * along)));
        }

        /// <summary>How far round the edge, clockwise from the top-left corner, a point on
        /// <paramref name="edge"/> is.</summary>
        public double Along(TileMapEdge edge, TileMapScreenPoint position) => edge switch
        {
            TileMapEdge.Top => position.X - Left,
            TileMapEdge.Right => Width + (position.Y - Top),
            TileMapEdge.Bottom => Width + Height + (Right - position.X),
            _ => (2 * Width) + Height + (Bottom - position.Y)
        };

        /// <summary>The point <paramref name="along"/> round the edge, and its edge.</summary>
        public (TileMapEdge Edge, TileMapScreenPoint Position) At(double along)
        {
            if (along < Width) return (TileMapEdge.Top, new TileMapScreenPoint(Left + along, Top));

            along -= Width;
            if (along < Height) return (TileMapEdge.Right, new TileMapScreenPoint(Right, Top + along));

            along -= Height;
            if (along < Width) return (TileMapEdge.Bottom, new TileMapScreenPoint(Right - along, Bottom));

            along -= Width;
            return (TileMapEdge.Left, new TileMapScreenPoint(Left, Math.Max(Top, Bottom - along)));
        }
    }

    /// <summary>The smallest box holding every point, or null for none. A box
    /// across the antimeridian is not looked for: the points are framed the long
    /// way round, the way Leaflet's own <c>fitBounds</c> would.</summary>
    public static MapBounds? Bounds(IEnumerable<MapCoordinate> points)
    {
        ArgumentNullException.ThrowIfNull(points);

        MapBounds? bounds = null;
        foreach (var point in points)
        {
            bounds = bounds is { } box
                ? new MapBounds(
                    Math.Min(box.South, point.Latitude),
                    Math.Min(box.West, point.Longitude),
                    Math.Max(box.North, point.Latitude),
                    Math.Max(box.East, point.Longitude))
                : new MapBounds(point.Latitude, point.Longitude, point.Latitude, point.Longitude);
        }

        return bounds;
    }

    /// <summary>The part of the map the chrome leaves free: left, top, right, bottom. Chrome
    /// that would leave nothing free is not reserved: the whole map is taken instead, so a
    /// mark is still a pin or a pill rather than nowhere. (The map's script already drops a
    /// reserve that leaves less than a fit's room, in the fit and in the view it reports
    /// alike; this is the guard for a view reported any other way.)</summary>
    private static (double Left, double Top, double Right, double Bottom) Free(TileMapViewport viewport)
    {
        var reserved = viewport.Reserved;
        var right = viewport.Width - reserved.Right;
        var bottom = viewport.Height - reserved.Bottom;

        return right > reserved.Left && bottom > reserved.Top
            ? (reserved.Left, reserved.Top, right, bottom)
            : (0, 0, viewport.Width, viewport.Height);
    }

    private static (double X, double Y) World(MapCoordinate point, double scale)
    {
        var latitude = Math.Clamp(point.Latitude, -MaxLatitude, MaxLatitude) * Math.PI / 180;
        var x = (point.Longitude + 180) / 360 * scale;
        var y = (0.5 - (Math.Log(Math.Tan((Math.PI / 4) + (latitude / 2))) / (2 * Math.PI))) * scale;
        return (x, y);
    }
}
