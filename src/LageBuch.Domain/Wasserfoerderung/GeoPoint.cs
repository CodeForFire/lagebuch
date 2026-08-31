namespace LageBuch.Domain.Wasserfoerderung;

/// <summary>One vertex of a route drawn on the map (#150, Plan B), WGS84 degrees.</summary>
public sealed record GeoPoint(double Latitude, double Longitude);
