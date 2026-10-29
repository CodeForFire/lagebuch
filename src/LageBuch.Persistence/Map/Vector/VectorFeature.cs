namespace LageBuch.Persistence.Map.Vector;

/// <summary>
/// One feature of a vector tile. <see cref="Parts"/> holds its points, its lines or its polygon
/// rings in tile coordinates (0 to the layer's extent, beyond it in the buffer); rings are
/// filled even-odd, which matches the spec's winding rule for valid tiles.
/// </summary>
public sealed record VectorFeature(VectorGeometryType Type, IReadOnlyDictionary<string, string> Properties, IReadOnlyList<IReadOnlyList<(int X, int Y)>> Parts)
{
    public string? Get(string key) => Properties.TryGetValue(key, out var value) ? value : null;
}
