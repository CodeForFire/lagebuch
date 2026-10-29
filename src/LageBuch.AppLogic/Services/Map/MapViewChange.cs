using LageBuch.Domain.ValueObjects;

namespace LageBuch.AppLogic.Services.Map;

/// <summary>
/// Where the map control has been panned, zoomed or turned to, handed to the view model through a
/// command. The view model owns the view so that it survives a tab switch and can be moved to a
/// position on purpose; the control only reports what the hand did.
/// </summary>
/// <param name="Center">The position in the middle of the map.</param>
/// <param name="Zoom">The zoom level, clamped by the view model.</param>
/// <param name="Rotation">
/// How far the map is turned clockwise, in degrees; null keeps the current turn, so moving the
/// view to a position on purpose never turns the map back north.
/// </param>
public sealed record MapViewChange(GeoPoint Center, int Zoom, double? Rotation = null);
