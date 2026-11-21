namespace LageBuch.Domain.Measurements;

/// <summary>Where the wind comes from (#930), read out in German: N, NO, O, SO, S, SW, W, NW.
/// Persisted by value.</summary>
public enum WindDirection
{
    N = 0,
    NE = 1,
    E = 2,
    SE = 3,
    S = 4,
    SW = 5,
    W = 6,
    NW = 7,

    /// <summary>"umlaufend".</summary>
    Variable = 8,

    /// <summary>"Windstille".</summary>
    Calm = 9,
}
