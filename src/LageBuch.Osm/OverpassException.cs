namespace LageBuch.Osm;

/// <summary>
/// Overpass could not deliver: unreachable, refused, timed out, or answered with something that is
/// not a usable response. The message is German and meant to be shown as it is.
/// </summary>
public sealed class OverpassException : Exception
{
    public OverpassException()
    {
    }

    public OverpassException(string message)
        : base(message)
    {
    }

    public OverpassException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
