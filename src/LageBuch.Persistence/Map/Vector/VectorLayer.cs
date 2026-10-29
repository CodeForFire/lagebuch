namespace LageBuch.Persistence.Map.Vector;

/// <summary>One named layer of a vector tile, such as Protomaps' <c>roads</c> or <c>water</c>.</summary>
public sealed record VectorLayer(string Name, int Extent, IReadOnlyList<VectorFeature> Features);
