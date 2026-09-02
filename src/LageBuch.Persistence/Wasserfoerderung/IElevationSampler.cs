using LageBuch.Domain.Wasserfoerderung;

namespace LageBuch.Persistence.Wasserfoerderung;

/// <summary>Samples terrain elevation along a drawn route (#150, Plan B).</summary>
public interface IElevationSampler
{
    IReadOnlyList<ElevationProfileSample> Sample(IReadOnlyList<GeoPoint> polyline);
}
