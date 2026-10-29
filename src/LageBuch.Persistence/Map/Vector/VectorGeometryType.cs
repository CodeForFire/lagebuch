namespace LageBuch.Persistence.Map.Vector;

/// <summary>The geometry kind of a <see cref="VectorFeature"/>, numbered as in the MVT spec.</summary>
public enum VectorGeometryType
{
    Unknown = 0,
    Point = 1,
    LineString = 2,
    Polygon = 3,
}
