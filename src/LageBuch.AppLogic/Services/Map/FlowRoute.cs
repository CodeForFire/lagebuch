using LageBuch.Domain.ValueObjects;

namespace LageBuch.AppLogic.Services.Map;

/// <summary>The way water takes downhill from a point.</summary>
/// <param name="Points">From the start, every 5 m cell it runs through.</param>
/// <param name="LengthMetres">Its length along the ground's plan, in metres.</param>
/// <param name="DropMetres">How far it falls, start to end, in metres.</param>
/// <param name="End">Why it stops.</param>
/// <param name="WaterName">For <see cref="FlowPathEnd.Gewaesser"/> the Gewässer's name, empty for one without; null otherwise.</param>
public sealed record FlowRoute(IReadOnlyList<GeoPoint> Points, double LengthMetres, double DropMetres, FlowPathEnd End, string? WaterName = null);
