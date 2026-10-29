using LageBuch.Domain.ValueObjects;

namespace LageBuch.AppLogic.Services.Map;

/// <summary>
/// Where the map control has been panned or zoomed to, handed to the view model through a
/// command. The view model owns the view so that it survives a tab switch and can be moved to a
/// position on purpose; the control only reports what the hand did.
/// </summary>
public sealed record MapViewChange(GeoPoint Center, int Zoom);
