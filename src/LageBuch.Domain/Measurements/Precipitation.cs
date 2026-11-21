namespace LageBuch.Domain.Measurements;

/// <summary>Niederschlag at a Wetterbeobachtung (#930). Persisted by value.</summary>
public enum Precipitation
{
    /// <summary>"kein Niederschlag".</summary>
    None = 0,

    /// <summary>"Regen".</summary>
    Rain = 1,

    /// <summary>"Schnee".</summary>
    Snow = 2,

    /// <summary>"Nebel".</summary>
    Fog = 3,
}
