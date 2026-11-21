namespace LageBuch.Domain.Measurements;

/// <summary>The sky at a Wetterbeobachtung (#930). Persisted by value.</summary>
public enum CloudCover
{
    /// <summary>"klar".</summary>
    Clear = 0,

    /// <summary>"bis 50 % bedeckt".</summary>
    UpToHalf = 1,

    /// <summary>"mehr als 50 % bedeckt".</summary>
    MoreThanHalf = 2,

    /// <summary>"bedeckt".</summary>
    Overcast = 3,
}
