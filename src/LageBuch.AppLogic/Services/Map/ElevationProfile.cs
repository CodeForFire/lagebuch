using LageBuch.Domain.ValueObjects;
using LageBuch.Persistence.Map.Elevation;

namespace LageBuch.AppLogic.Services.Map;

/// <summary>
/// The ground along a straight line from <see cref="From"/> to <see cref="To"/>, a height every
/// <see cref="Step"/> metres: what a Schlauchleitung laid that way has to climb. The line is the
/// shortest one, not the road the hose will follow; it tells how high, not how long.
/// </summary>
/// <param name="From">Where the line starts: the Wasserentnahmestelle.</param>
/// <param name="To">Where it ends: the Einsatzort.</param>
/// <param name="Distances">Each sample's distance from <see cref="From"/>, in metres.</param>
/// <param name="Heights">Each sample's height above NHN in metres; null where no pack has one.</param>
public sealed record ElevationProfile(GeoPoint From, GeoPoint To, IReadOnlyList<double> Distances, IReadOnlyList<double?> Heights)
{
    /// <summary>One sample per DGM5 cell.</summary>
    public const double Step = 5;

    /// <summary>Lines longer than this, in metres, are sampled more coarsely, so a profile never has more than <see cref="MaxSamples"/> heights.</summary>
    public const int MaxSamples = 2000;

    /// <summary>The line's length in metres.</summary>
    public double Length => Distances[^1];

    /// <summary>The height at the start; null where the start has none.</summary>
    public double? StartHeight => Heights[0];

    /// <summary>The height at the end; null where the end has none.</summary>
    public double? EndHeight => Heights[^1];

    /// <summary>How much higher the end lies than the start; negative when it lies lower.</summary>
    public double? Rise => EndHeight - StartHeight;

    /// <summary>
    /// Every metre gained on the way, summed: the climbs between dips add up even where the end is
    /// no higher. Only a rise or fall of at least <see cref="ClimbThreshold"/> counts, so the DGM's
    /// decimetre ripples over a field do not add up to a hill.
    /// </summary>
    public double Climb
    {
        get
        {
            var climb = 0.0;
            double? reference = null;
            foreach (var h in Heights)
            {
                if (h is not { } height)
                {
                    continue;
                }

                if (reference is not { } from || from - height >= ClimbThreshold)
                {
                    reference = height;
                }
                else if (height - from >= ClimbThreshold)
                {
                    climb += height - from;
                    reference = height;
                }
            }

            return climb;
        }
    }

    /// <summary>The smallest rise or fall, in metres, that <see cref="Climb"/> counts.</summary>
    public const double ClimbThreshold = 1;

    /// <summary>The highest sample and how far along it lies; null when no sample has a height.</summary>
    public (double Height, double Distance)? Highest
    {
        get
        {
            var best = -1;
            var bestHeight = double.NegativeInfinity;
            for (var i = 0; i < Heights.Count; i++)
            {
                if (Heights[i] is { } h && h > bestHeight)
                {
                    (best, bestHeight) = (i, h);
                }
            }

            return best < 0 ? null : (bestHeight, Distances[best]);
        }
    }

    /// <summary>Whether some samples on the way have no height, so the figures may miss part of the climb.</summary>
    public bool HasGaps => Heights.Any(h => h is null);

    /// <summary>
    /// The profile from <paramref name="from"/> to <paramref name="to"/>, interpolated along the
    /// straight line on the EPSG:25832 grid.
    /// </summary>
    public static ElevationProfile Along(ElevationSet elevation, GeoPoint from, GeoPoint to)
    {
        ArgumentNullException.ThrowIfNull(elevation);
        ArgumentNullException.ThrowIfNull(from);
        ArgumentNullException.ThrowIfNull(to);
        var (e0, n0) = Utm32.FromGeo(from);
        var (e1, n1) = Utm32.FromGeo(to);
        var length = Math.Sqrt(((e1 - e0) * (e1 - e0)) + ((n1 - n0) * (n1 - n0)));
        var steps = (int)Math.Clamp(Math.Ceiling(length / Step), 1, MaxSamples - 1);
        var distances = new double[steps + 1];
        var heights = new double?[steps + 1];
        for (var i = 0; i <= steps; i++)
        {
            var t = (double)i / steps;
            distances[i] = t * length;
            heights[i] = elevation.HeightAt(e0 + ((e1 - e0) * t), n0 + ((n1 - n0) * t));
        }

        return new ElevationProfile(from, to, distances, heights);
    }
}
