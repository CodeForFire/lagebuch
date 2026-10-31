using Avalonia;
using LageBuch.Domain.ValueObjects;

namespace LageBuch.App.Shared.Controls;

/// <summary>
/// Finds a taktisches Zeichen a place beside its pin that covers no other sign and no pin, so
/// vehicles standing a few metres apart stay readable without anyone dragging them apart. DV 102
/// §1.5 allows exactly this -- a sign drawn aside "aus Gründen der Übersichtlichkeit", tied back
/// by its Verbindungsstrich.
/// </summary>
/// <remarks>
/// Pure screen geometry and deterministic: the same signs at the same zoom land in the same places
/// on every device, and nothing is stored until a sign is dragged.
/// </remarks>
public static class SignPlacement
{
    /// <summary>The room a pin needs: its dot, its finger-sized grip, and a little air.</summary>
    public const double PinClearance = 12;

    // Up and to the right first, as a sign is usually drawn; then the other diagonals, the sides,
    // above and below; each further out once the closer ring is taken.
    private static readonly (double X, double Y)[] Directions =
    [
        (1, -1), (-1, -1), (1, 1), (-1, 1), (1, 0), (-1, 0), (0, -1), (0, 1),
    ];

    private static readonly double[] Distances = [40, 64, 96, 136, 184];

    /// <summary>The box a pin keeps free around <paramref name="at"/>.</summary>
    public static Rect PinBox(Point at) =>
        new(at.X - PinClearance, at.Y - PinClearance, PinClearance * 2, PinClearance * 2);

    /// <summary>Where a sign of <paramref name="size"/>, anchored at <paramref name="anchor"/>, stands at <paramref name="offset"/> from <paramref name="pin"/>.</summary>
    public static Rect Box(Point pin, LabelOffset offset, Size size, Point anchor)
    {
        ArgumentNullException.ThrowIfNull(offset);
        return new Rect(new Point(pin.X + offset.Dx - anchor.X, pin.Y + offset.Dy - anchor.Y), size);
    }

    /// <summary>
    /// The first offset, nearest ring first, whose sign covers nothing in <paramref name="occupied"/>
    /// and whose Verbindungsstrich crosses none of <paramref name="striche"/>: two crossing striche
    /// leave it open which sign belongs to which pin. Where every one is taken -- a crowd no ring
    /// can hold -- the one covering the least, a crossing counting as much as a covered sign.
    /// </summary>
    public static LabelOffset Choose(Point pin, Size size, Point anchor, IReadOnlyList<Rect> occupied, IReadOnlyList<(Point From, Point To)> striche)
    {
        ArgumentNullException.ThrowIfNull(occupied);
        ArgumentNullException.ThrowIfNull(striche);
        LabelOffset? best = null;
        var leastCovered = double.MaxValue;
        var crossingCost = size.Width * size.Height;
        foreach (var distance in Distances)
        {
            foreach (var (x, y) in Directions)
            {
                var offset = Candidate(x, y, distance, size, anchor);
                var end = new Point(pin.X + offset.Dx, pin.Y + offset.Dy);
                var covered = Covered(Box(pin, offset, size, anchor), occupied)
                    + (striche.Count(s => Cross(pin, end, s.From, s.To)) * crossingCost);
                if (covered <= 0)
                {
                    return offset;
                }

                if (covered < leastCovered)
                {
                    leastCovered = covered;
                    best = offset;
                }
            }
        }

        return best ?? Candidate(Directions[0].X, Directions[0].Y, Distances[0], size, anchor);
    }

    // The anchor's offset that puts the sign's near edge `distance` from the pin in that direction:
    // a sign to the left stands with its right edge there, not its middle, so a long Funkrufname
    // never reaches back over its own pin.
    private static LabelOffset Candidate(double x, double y, double distance, Size size, Point anchor)
    {
        var dx = x > 0 ? (distance * x) + anchor.X : x < 0 ? (distance * x) - (size.Width - anchor.X) : 0;
        var dy = y > 0 ? (distance * 0.6 * y) + anchor.Y : y < 0 ? (distance * 0.6 * y) - (size.Height - anchor.Y) : 0;
        return new LabelOffset(
            Math.Clamp(dx, -LabelOffset.MaxPixels, LabelOffset.MaxPixels),
            Math.Clamp(dy, -LabelOffset.MaxPixels, LabelOffset.MaxPixels));
    }

    // Whether segments ab and cd cross at a point inside both. Touching at an end does not count:
    // two signs of one spot share the pin their striche start from.
    private static bool Cross(Point a, Point b, Point c, Point d)
    {
        var d1 = Turn(c, d, a);
        var d2 = Turn(c, d, b);
        var d3 = Turn(a, b, c);
        var d4 = Turn(a, b, d);
        return d1 * d2 < 0 && d3 * d4 < 0;
    }

    // Which side of the line pq the point r lies on: positive left, negative right, 0 on it.
    private static double Turn(Point p, Point q, Point r) => ((q.X - p.X) * (r.Y - p.Y)) - ((q.Y - p.Y) * (r.X - p.X));

    private static double Covered(Rect box, IReadOnlyList<Rect> occupied)
    {
        var area = 0.0;
        foreach (var other in occupied)
        {
            var overlap = box.Intersect(other);
            area += overlap.Width * overlap.Height;
        }

        return area;
    }
}
