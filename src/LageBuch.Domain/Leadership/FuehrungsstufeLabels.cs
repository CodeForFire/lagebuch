namespace LageBuch.Domain.Leadership;

/// <summary>
/// How a <see cref="Fuehrungsstufe"/> is spelt wherever a person reads it: on the board, in its
/// picker and in the Bericht. One place, so the three cannot drift. The stored key is
/// <see cref="LeadershipKeys.StufeKey"/>, which happens to be the same letter but is a file format.
/// </summary>
public static class FuehrungsstufeLabels
{
    /// <summary>The Stufe's letter, "A" to "D".</summary>
    public static string Of(Fuehrungsstufe stufe) => stufe switch
    {
        Fuehrungsstufe.B => "B",
        Fuehrungsstufe.C => "C",
        Fuehrungsstufe.D => "D",
        _ => "A",
    };
}
