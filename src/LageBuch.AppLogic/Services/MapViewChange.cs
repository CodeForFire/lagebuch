namespace LageBuch.AppLogic.Services;

/// <summary>
/// A wheel/pinch-driven map view change from <c>MapCanvasControl</c> (#150 follow-up) — the
/// control->VM counterpart of <c>GeoPoint</c> for <c>WasserfoerderungViewModel.ChangeMapViewCommand</c>,
/// living alongside <see cref="WebMercator"/> (not in a specific ViewModel or the Domain layer)
/// since both the App.Shared control and the AppLogic view model need to reference it.
/// </summary>
public sealed record MapViewChange(double CenterLatitude, double CenterLongitude, int Zoom);
