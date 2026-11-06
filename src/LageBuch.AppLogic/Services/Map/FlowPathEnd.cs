namespace LageBuch.AppLogic.Services.Map;

/// <summary>Where a <see cref="FlowPath"/> stops.</summary>
public enum FlowPathEnd
{
    /// <summary>In a hollow deeper than <see cref="FlowPath.MaxFillDepth"/>: there the water collects.</summary>
    Senke,

    /// <summary>At the edge of the heights: past it the Kartenpaket has none.</summary>
    Kartenrand,

    /// <summary>After <see cref="FlowPath.MaxLength"/> metres: further on it is no longer the Einsatzstelle's concern.</summary>
    MaxLength,

    /// <summary>In a Gewässer: where it drifts from there, the ground does not tell.</summary>
    Gewaesser,
}
