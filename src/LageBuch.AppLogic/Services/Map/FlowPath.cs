using LageBuch.Domain.ValueObjects;
using LageBuch.Persistence.Map.Elevation;

namespace LageBuch.AppLogic.Services.Map;

/// <summary>
/// Where Löschwasser or a spilled Gefahrstoff runs from a point: always to the steepest lower of
/// the eight neighbouring DGM cells. A hollow on the way is filled up to
/// <see cref="MaxFillDepth"/> to find where it spills over -- the DGM shows a road embankment
/// but not the culvert under it -- and a deeper one ends the way as a <see cref="FlowPathEnd.Senke"/>.
/// </summary>
/// <remarks>
/// The surface is bare earth: water also runs into drains, along kerbs and through buildings the
/// DGM does not know. The route is a first estimate for where to look, not where it goes.
/// </remarks>
public static class FlowPath
{
    /// <summary>The DGM5's own spacing, in metres: one step per cell.</summary>
    public const double CellSize = 5;

    /// <summary>Further than this, in metres, the route stops.</summary>
    public const double MaxLength = 3_000;

    /// <summary>A hollow is filled up to this depth, in metres, to find its spill point; a deeper one holds the water.</summary>
    public const double MaxFillDepth = 2;

    /// <summary>At most this many cells are flooded to find a hollow's spill point: about 1.25 km².</summary>
    public const int MaxFillCells = 50_000;

    private static readonly (int Dx, int Dy, double Distance)[] Neighbours =
    [
        (-1, -1, Math.Sqrt(2)), (0, -1, 1), (1, -1, Math.Sqrt(2)), (-1, 0, 1),
        (1, 0, 1), (-1, 1, Math.Sqrt(2)), (0, 1, 1), (1, 1, Math.Sqrt(2)),
    ];

    /// <summary>The route from <paramref name="start"/>, or null where there is no height to start from.</summary>
    public static FlowRoute? Trace(ElevationSet elevation, GeoPoint start)
    {
        ArgumentNullException.ThrowIfNull(elevation);
        ArgumentNullException.ThrowIfNull(start);
        var (east, north) = Utm32.FromGeo(start);

        // Cells are addressed by their index on the 5 m lattice, so a cell is the same cell however it is reached.
        var origin = ((long)Math.Floor(east / CellSize), (long)Math.Floor(north / CellSize));
        double? Height((long X, long Y) cell) => elevation.HeightAt((cell.X + 0.5) * CellSize, (cell.Y + 0.5) * CellSize);

        if (Height(origin) is not { } startHeight)
        {
            return null;
        }

        var cells = new List<(long X, long Y)> { origin };
        var visited = new HashSet<(long X, long Y)> { origin };
        var current = origin;
        var height = startHeight;
        var length = 0.0;
        var end = FlowPathEnd.MaxLength;
        while (length < MaxLength)
        {
            var next = Steepest(current, height, Height, visited, out var nextHeight, out var edge);
            if (edge)
            {
                end = FlowPathEnd.Kartenrand;
                break;
            }

            if (next is { } step)
            {
                length += Distance(current, step);
                (current, height) = (step, nextHeight);
                cells.Add(step);
                visited.Add(step);
                continue;
            }

            if (Spill(current, height, Height, visited) is not { } way)
            {
                end = FlowPathEnd.Senke;
                break;
            }

            foreach (var cell in way.TakeWhile(_ => length < MaxLength))
            {
                length += Distance(current, cell);
                current = cell;
                cells.Add(cell);
                visited.Add(cell);
            }

            height = Height(current) ?? height;
        }

        var points = cells.Select(c => Utm32.ToGeo((c.X + 0.5) * CellSize, (c.Y + 0.5) * CellSize)).ToList();
        return new FlowRoute(points, length, startHeight - height, end);
    }

    // The lowest neighbour by drop per metre that lies below `height`; none in a hollow or on a flat.
    // `edge` when a neighbour has no height: the water leaves the map there.
    private static (long X, long Y)? Steepest((long X, long Y) cell, double height, Func<(long X, long Y), double?> heightOf, HashSet<(long X, long Y)> visited, out double nextHeight, out bool edge)
    {
        (long X, long Y)? best = null;
        var bestSlope = 0.0;
        nextHeight = height;
        edge = false;
        foreach (var (dx, dy, distance) in Neighbours)
        {
            // Stryker disable once Arithmetic : the eight neighbours are symmetric, so subtracting the offsets visits the same cells.
            var neighbour = (cell.X + dx, cell.Y + dy);
            if (heightOf(neighbour) is not { } h)
            {
                edge = true;
                return null;
            }

            var slope = (height - h) / distance;
            if (slope > bestSlope && !visited.Contains(neighbour))
            {
                (best, bestSlope, nextHeight) = (neighbour, slope, h);
            }
        }

        return best;
    }

    /// <summary>
    /// The way out of the hollow at <paramref name="pit"/>: the hollow is flooded lowest cell first
    /// until a cell lower than its bottom turns up beyond the rim, then the way there is followed back.
    /// Null when the water would have to rise more than <see cref="MaxFillDepth"/> or flood more
    /// than <see cref="MaxFillCells"/> cells first.
    /// </summary>
    private static List<(long X, long Y)>? Spill((long X, long Y) pit, double pitHeight, Func<(long X, long Y), double?> heightOf, HashSet<(long X, long Y)> route)
    {
        var queue = new PriorityQueue<(long X, long Y), double>();
        var from = new Dictionary<(long X, long Y), (long X, long Y)>();
        var level = pitHeight;
        queue.Enqueue(pit, pitHeight);
        from[pit] = pit;
        while (queue.TryDequeue(out var cell, out var h))
        {
            if (h < pitHeight && !route.Contains(cell))
            {
                // Lower than the hollow's bottom: the rim is behind, and from here it runs down again.
                var way = new List<(long X, long Y)>();
                for (var c = cell; c != pit; c = from[c])
                {
                    way.Add(c);
                }

                way.Reverse();
                return way;
            }

            level = Math.Max(level, h);

            // Stryker disable once Equality : a hollow exactly MaxFillDepth deep, or of exactly MaxFillCells cells, may go either way.
            if (level - pitHeight > MaxFillDepth || from.Count > MaxFillCells)
            {
                return null;
            }

            foreach (var (dx, dy, _) in Neighbours)
            {
                // Stryker disable once Arithmetic : the eight neighbours are symmetric, so subtracting the offsets visits the same cells.
                var neighbour = (cell.X + dx, cell.Y + dy);
                if (!from.ContainsKey(neighbour) && heightOf(neighbour) is { } nh)
                {
                    from[neighbour] = cell;
                    queue.Enqueue(neighbour, nh);
                }
            }
        }

        return null;
    }

    private static double Distance((long X, long Y) a, (long X, long Y) b)
    {
        double dx = a.X - b.X;
        double dy = a.Y - b.Y;
        return CellSize * Math.Sqrt((dx * dx) + (dy * dy));
    }
}
