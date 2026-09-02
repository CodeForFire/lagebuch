using LageBuch.Domain.Wasserfoerderung;

namespace LageBuch.Persistence.Wasserfoerderung;

/// <summary>
/// Reads the custom flat binary heightmap format (#150, Plan B, data-prep contract — see the
/// implementation plan for the exact byte layout) and samples elevation along a drawn route.
///
/// Format: 40-byte little-endian header (magic "FWDM", format version, origin lat/lon, cell size
/// in degrees, rows, cols), then a row-major Int16 body in meters (row 0 = north, col 0 = west;
/// <see cref="NoData"/> marks a missing cell).
/// </summary>
public sealed class DemFileElevationSampler : IElevationSampler
{
    private const short NoData = short.MinValue;
    private const double EarthRadiusMeters = 6371000;

    private readonly double _originLatitude;
    private readonly double _originLongitude;
    private readonly double _cellSizeDegrees;
    private readonly int _rows;
    private readonly int _cols;
    private readonly short[] _body;
    private readonly double _sampleIntervalMeters;

    public DemFileElevationSampler(string demFilePath, double sampleIntervalMeters = 20.0)
    {
        _sampleIntervalMeters = sampleIntervalMeters;

        using var stream = File.OpenRead(demFilePath);
        using var reader = new BinaryReader(stream);

        var magic = System.Text.Encoding.ASCII.GetString(reader.ReadBytes(4));
        if (magic != "FWDM")
        {
            throw new InvalidDataException($"'{demFilePath}' ist keine gültige DEM-Datei (Magic '{magic}').");
        }

        _ = reader.ReadInt32(); // format version, currently always 1
        _originLatitude = reader.ReadDouble();
        _originLongitude = reader.ReadDouble();
        _cellSizeDegrees = reader.ReadDouble();
        _rows = reader.ReadInt32();
        _cols = reader.ReadInt32();

        _body = new short[_rows * _cols];
        for (var i = 0; i < _body.Length; i++)
        {
            _body[i] = reader.ReadInt16();
        }
    }

    public IReadOnlyList<ElevationProfileSample> Sample(IReadOnlyList<GeoPoint> polyline)
    {
        ArgumentNullException.ThrowIfNull(polyline);
        if (polyline.Count < 2)
        {
            throw new ArgumentException("Die Route braucht mindestens zwei Punkte.", nameof(polyline));
        }

        var cumulative = new double[polyline.Count];
        for (var i = 1; i < polyline.Count; i++)
        {
            cumulative[i] = cumulative[i - 1] + HaversineMeters(polyline[i - 1], polyline[i]);
        }

        var totalLength = cumulative[^1];

        var samples = new List<ElevationProfileSample>();
        var segment = 0;

        // The epsilon keeps a total length that lands almost exactly on a sample boundary (a
        // floating-point hair above it) from producing a near-duplicate of the final-vertex
        // sample appended below.
        for (var d = 0.0; d < totalLength - 1e-6; d += _sampleIntervalMeters)
        {
            while (segment < polyline.Count - 2 && cumulative[segment + 1] < d)
            {
                segment++;
            }

            samples.Add(SampleAt(polyline, cumulative, segment, d));
        }

        samples.Add(SampleAt(polyline, cumulative, polyline.Count - 2, totalLength));
        return samples;
    }

    private ElevationProfileSample SampleAt(
        IReadOnlyList<GeoPoint> polyline, double[] cumulative, int segment, double distance)
    {
        var segStart = cumulative[segment];
        var segEnd = cumulative[segment + 1];
        var t = segEnd > segStart ? (distance - segStart) / (segEnd - segStart) : 0;
        var a = polyline[segment];
        var b = polyline[segment + 1];
        var lat = a.Latitude + (t * (b.Latitude - a.Latitude));
        var lon = a.Longitude + (t * (b.Longitude - a.Longitude));
        return new ElevationProfileSample(distance, ElevationAt(lat, lon));
    }

    private static double HaversineMeters(GeoPoint a, GeoPoint b)
    {
        var dLat = ToRadians(b.Latitude - a.Latitude);
        var dLon = ToRadians(b.Longitude - a.Longitude);
        var lat1 = ToRadians(a.Latitude);
        var lat2 = ToRadians(b.Latitude);
        var sinDLat = Math.Sin(dLat / 2);
        var sinDLon = Math.Sin(dLon / 2);
        var h = (sinDLat * sinDLat) + (Math.Cos(lat1) * Math.Cos(lat2) * sinDLon * sinDLon);
        return 2 * EarthRadiusMeters * Math.Atan2(Math.Sqrt(h), Math.Sqrt(1 - h));
    }

    private static double ToRadians(double degrees) => degrees * Math.PI / 180.0;

    private double ElevationAt(double lat, double lon)
    {
        var rowF = (_originLatitude - lat) / _cellSizeDegrees;
        var colF = (lon - _originLongitude) / _cellSizeDegrees;

        var r0 = Math.Clamp((int)Math.Floor(rowF), 0, _rows - 1);
        var r1 = Math.Clamp(r0 + 1, 0, _rows - 1);
        var c0 = Math.Clamp((int)Math.Floor(colF), 0, _cols - 1);
        var c1 = Math.Clamp(c0 + 1, 0, _cols - 1);
        var fr = Math.Clamp(rowF - r0, 0, 1);
        var fc = Math.Clamp(colF - c0, 0, 1);

        var topLeft = CellAt(r0, c0);
        var topRight = CellAt(r0, c1);
        var bottomLeft = CellAt(r1, c0);
        var bottomRight = CellAt(r1, c1);
        ResolveNoData(ref topLeft, ref topRight, ref bottomLeft, ref bottomRight);

        var top = (topLeft * (1 - fc)) + (topRight * fc);
        var bottom = (bottomLeft * (1 - fc)) + (bottomRight * fc);
        return (top * (1 - fr)) + (bottom * fr);
    }

    private double CellAt(int row, int col) => _body[(row * _cols) + col];

    /// <summary>
    /// A NoData corner is replaced by the average of the bilinear stencil's other valid corners —
    /// the nearest valid values available to this interpolation, per the DEM edge-case contract.
    /// </summary>
    private static void ResolveNoData(ref double topLeft, ref double topRight, ref double bottomLeft, ref double bottomRight)
    {
        var corners = new[] { topLeft, topRight, bottomLeft, bottomRight };
        var validCorners = corners.Where(v => v != NoData).ToArray();
        if (validCorners.Length == 0 || validCorners.Length == corners.Length)
        {
            return;
        }

        var fallback = validCorners.Average();
        if (topLeft == NoData)
        {
            topLeft = fallback;
        }

        if (topRight == NoData)
        {
            topRight = fallback;
        }

        if (bottomLeft == NoData)
        {
            bottomLeft = fallback;
        }

        if (bottomRight == NoData)
        {
            bottomRight = fallback;
        }
    }
}
