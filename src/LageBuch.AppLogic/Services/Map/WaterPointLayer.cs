using LageBuch.Domain.ValueObjects;

namespace LageBuch.AppLogic.Services.Map;

/// <summary>
/// When the map shows the water points, how much of each, and which one a tap means. The drawing
/// and the tap use the same thresholds and sizes, so a point can be tapped exactly where it is seen.
/// </summary>
public static class WaterPointLayer
{
    /// <summary>Further out, a Landkreis' thousands of hydrants are a red carpet, not information.</summary>
    public const int MinZoom = 15;

    /// <summary>From here a point is a flag with its flow, not a disc.</summary>
    public const int MediumMinZoom = 16;

    /// <summary>From here a flag also says the unit, the DN and Ring or Stich.</summary>
    public const int FullMinZoom = 18;

    /// <summary>How far from a symbol's centre a tap still means it: about a fingertip.</summary>
    public const double HitRadiusPixels = 16;

    /// <summary>How wide a flag's mast is where it meets the body; it narrows to a point at the foot.</summary>
    public const double MastWidth = 4;

    /// <summary>The flag at <see cref="WaterPointDetailLevel.Mittel"/>.</summary>
    public static readonly WaterPointFlag MediumFlag = new(MastLength: 8, BodyHeight: 20, NominalWidth: 64);

    /// <summary>The flag at <see cref="WaterPointDetailLevel.Voll"/>.</summary>
    public static readonly WaterPointFlag FullFlag = new(MastLength: 10, BodyHeight: 32, NominalWidth: 100);

    /// <summary>How much of a marker shows at <paramref name="zoom"/>; null below <see cref="MinZoom"/>, where none does.</summary>
    public static WaterPointDetailLevel? LevelAt(int zoom) => zoom switch
    {
        < MinZoom => null,
        < MediumMinZoom => WaterPointDetailLevel.Punkt,
        < FullMinZoom => WaterPointDetailLevel.Mittel,
        _ => WaterPointDetailLevel.Voll,
    };

    /// <summary>The flag a marker is drawn as at <paramref name="level"/>; null for the disc.</summary>
    public static WaterPointFlag? FlagFor(WaterPointDetailLevel level) => level switch
    {
        WaterPointDetailLevel.Mittel => MediumFlag,
        WaterPointDetailLevel.Voll => FullFlag,
        _ => null,
    };

    /// <summary>
    /// Where <paramref name="point"/>'s marker lies in the stack, lowest first: the one whose card
    /// is open, <paramref name="selected"/>, on top, the others by <see cref="FlowClass"/>, so a
    /// strong source lies over a weak one. The map draws its markers in this order, and a tap on
    /// markers that share a spot means the one on top.
    /// </summary>
    public static int StackRank(WaterPoint point, WaterPoint? selected)
    {
        ArgumentNullException.ThrowIfNull(point);
        var flowClass = (int)WaterPointMarking.Classify(point.FlowLitresPerMinute);
        return ReferenceEquals(point, selected) ? (int)FlowClass.Ab3200 + 1 : flowClass;
    }

    /// <summary>
    /// The water point nearest to <paramref name="tap"/> whose marker the tap lands on, on a map at
    /// <paramref name="zoom"/> turned clockwise by <paramref name="rotation"/> degrees: within
    /// <see cref="HitRadiusPixels"/> of the point itself, or on its flag, which stays upright on a
    /// turned map. Of points on the same spot, the one drawn on top, by <see cref="StackRank"/>.
    /// Null when none is that close, or the map is too far out to show any.
    /// </summary>
    public static WaterPoint? At(IReadOnlyList<WaterPoint> points, GeoPoint tap, int zoom, double rotation = 0, WaterPoint? selected = null)
    {
        ArgumentNullException.ThrowIfNull(points);
        ArgumentNullException.ThrowIfNull(tap);
        if (LevelAt(zoom) is not { } level)
        {
            return null;
        }

        var flag = FlagFor(level);
        var turn = rotation * Math.PI / 180;
        var (cos, sin) = (Math.Cos(turn), Math.Sin(turn));
        var (tapX, tapY) = WebMercator.ToWorldPixel(tap, zoom);
        WaterPoint? nearest = null;
        var nearestSquared = double.MaxValue;
        foreach (var point in points)
        {
            var (x, y) = WebMercator.ToWorldPixel(point.Position, zoom);
            var (dx, dy) = (tapX - x, tapY - y);
            var squared = (dx * dx) + (dy * dy);

            // Where the tap lies from the point on the screen, which the flag is upright on.
            var (screenX, screenY) = ((dx * cos) - (dy * sin), (dx * sin) + (dy * cos));
            var hit = squared <= HitRadiusPixels * HitRadiusPixels || (flag is not null && OnFlag(flag, screenX, screenY));

            // Later markers of the same rank are drawn over earlier ones, so a tie goes to the later.
            var onTop = nearest is not null && point.Position == nearest.Position && StackRank(point, selected) >= StackRank(nearest, selected);
            if (hit && (squared < nearestSquared || onTop))
            {
                nearest = point;
                nearestSquared = squared;
            }
        }

        return nearest;
    }

    // The body above and right of the foot, the mast from it down to the foot, each with a few
    // pixels to spare for a finger. Beside the mast, under the body, is still the map.
    private static bool OnFlag(WaterPointFlag flag, double x, double y)
    {
        const double spare = 4;
        var onBody = x >= -spare && x <= flag.NominalWidth + spare
            && y >= -(flag.MastLength + flag.BodyHeight + spare) && y <= -flag.MastLength + spare;
        var onMast = x >= -spare && x <= MastWidth + spare && y >= -flag.MastLength && y <= spare;
        return onBody || onMast;
    }
}
