namespace LageBuch.Domain.Wasserfoerderung;

/// <summary>One sampled terrain point along a drawn route, distance from the route start.</summary>
public sealed record ElevationProfileSample(double DistanceMeters, double ElevationMeters);
