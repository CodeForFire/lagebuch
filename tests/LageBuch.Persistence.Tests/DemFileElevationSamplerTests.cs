using LageBuch.Domain.Wasserfoerderung;
using LageBuch.Persistence.Wasserfoerderung;

namespace LageBuch.Persistence.Tests;

public class DemFileElevationSamplerTests : IDisposable
{
    private const short NoData = short.MinValue;
    private readonly List<string> _tempFiles = new();

    public void Dispose()
    {
        foreach (var path in _tempFiles)
        {
            File.Delete(path);
        }
    }

    private string WriteDem(double originLat, double originLon, double cellSizeDeg, short[][] rows)
    {
        var rowCount = rows.Length;
        var colCount = rows[0].Length;
        var path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}.dem");
        _tempFiles.Add(path);

        using var stream = File.Create(path);
        using var writer = new BinaryWriter(stream);
        writer.Write("FWDM"u8.ToArray());
        writer.Write(1);
        writer.Write(originLat);
        writer.Write(originLon);
        writer.Write(cellSizeDeg);
        writer.Write(rowCount);
        writer.Write(colCount);
        for (var r = 0; r < rowCount; r++)
        {
            for (var c = 0; c < colCount; c++)
            {
                writer.Write(rows[r][c]);
            }
        }

        return path;
    }

    [Fact]
    public void Elevation_at_an_exact_grid_point_returns_the_stored_value()
    {
        var path = WriteDem(48.0009, 11.0, 0.0005, new[]
        {
            new short[] { 100, 110, 120 },
            new short[] { 200, 210, 220 },
            new[] { (short)300, NoData, (short)320 },
        });
        var sampler = new DemFileElevationSampler(path);

        var samples = sampler.Sample(new[] { new GeoPoint(48.0009, 11.0), new GeoPoint(48.0009, 11.0001) });

        Assert.Equal(100, samples[0].ElevationMeters, 3);
    }

    [Fact]
    public void Elevation_bilinear_interpolates_between_four_surrounding_cells()
    {
        var path = WriteDem(48.0009, 11.0, 0.0005, new[]
        {
            new short[] { 100, 110, 120 },
            new short[] { 200, 210, 220 },
            new[] { (short)300, NoData, (short)320 },
        });
        var sampler = new DemFileElevationSampler(path);

        // Midpoint of the (100,110,200,210) cell -> average of all four corners.
        var samples = sampler.Sample(new[]
        {
            new GeoPoint(48.00065, 11.00025),
            new GeoPoint(48.00065, 11.00026),
        });

        Assert.Equal(155.0, samples[0].ElevationMeters, 3);
    }

    [Fact]
    public void Elevation_substitutes_a_NoData_corner_with_the_average_of_the_other_corners()
    {
        var path = WriteDem(48.0009, 11.0, 0.0005, new[]
        {
            new short[] { 100, 110, 120 },
            new short[] { 200, 210, 220 },
            new[] { (short)300, NoData, (short)320 },
        });
        var sampler = new DemFileElevationSampler(path);

        // Corners: (210, 220, NoData, 320) -> NoData replaced by avg(210,220,320)=250.
        var samples = sampler.Sample(new[]
        {
            new GeoPoint(48.00015, 11.00075),
            new GeoPoint(48.00015, 11.00076),
        });

        Assert.Equal(250.0, samples[0].ElevationMeters, 3);
    }

    [Fact]
    public void Elevation_outside_the_grid_clamps_to_the_nearest_edge_cell()
    {
        var path = WriteDem(48.0009, 11.0, 0.0005, new[]
        {
            new short[] { 100, 110, 120 },
            new short[] { 200, 210, 220 },
            new[] { (short)300, NoData, (short)320 },
        });
        var sampler = new DemFileElevationSampler(path);

        var north = sampler.Sample(new[] { new GeoPoint(49.0, 11.0), new GeoPoint(49.0, 11.0001) });
        var southEast = sampler.Sample(new[] { new GeoPoint(47.0, 12.0), new GeoPoint(47.0, 12.0001) });

        Assert.Equal(100.0, north[0].ElevationMeters, 3);
        Assert.Equal(320.0, southEast[0].ElevationMeters, 3);
    }

    [Fact]
    public void Sample_walks_the_polyline_at_the_configured_interval_ending_exactly_on_the_last_vertex()
    {
        var flat = new short[10][];
        for (var r = 0; r < 10; r++)
        {
            flat[r] = new short[10];
            for (var c = 0; c < 10; c++)
            {
                flat[r][c] = 500;
            }
        }

        var path = WriteDem(48.001, 10.999, 0.0002, flat);
        var sampler = new DemFileElevationSampler(path, sampleIntervalMeters: 20.0);

        // Due-north segment, exactly 100 m (R * Δlat_rad with Δlat_deg = 0.0008993216059187306).
        var start = new GeoPoint(48.0, 11.0);
        var end = new GeoPoint(48.0008993216059187306, 11.0);

        var samples = sampler.Sample(new[] { start, end });

        Assert.Equal(6, samples.Count);
        Assert.Equal(new[] { 0.0, 20, 40, 60, 80, 100 }, samples.Select(s => s.DistanceMeters), new ApproximateComparer());
        Assert.All(samples, s => Assert.Equal(500.0, s.ElevationMeters, 1));
    }

    private sealed class ApproximateComparer : IEqualityComparer<double>
    {
        public bool Equals(double x, double y) => Math.Abs(x - y) < 0.01;

        public int GetHashCode(double obj) => 0;
    }
}
