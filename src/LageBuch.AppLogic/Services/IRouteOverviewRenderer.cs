using LageBuch.Domain.Wasserfoerderung;
using LageBuch.Persistence.Wasserfoerderung;

namespace LageBuch.AppLogic.Services;

/// <summary>
/// Renders a small map snapshot (tiles + polyline) of a drawn Wasserförderung route for the PDF
/// (#150 phase 2). Implemented in LageBuch.App.Shared using Avalonia's off-screen rendering —
/// AppLogic and Documents stay Avalonia-free, so this is the one port between them.
/// </summary>
public interface IRouteOverviewRenderer
{
    /// <summary>PNG bytes framing the whole route, or null if it can't be rendered.</summary>
    byte[]? Render(IReadOnlyList<GeoPoint> routePoints, IMapTileSource tiles);
}
