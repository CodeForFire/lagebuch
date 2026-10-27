namespace LageBuch.AppLogic.Services.Map;

/// <summary>
/// A Kartenpaket could not be listed, downloaded or installed. The message is German and meant to
/// be shown as it is; it never carries a raw exception text (#602).
/// </summary>
public sealed class RegionPackException : Exception
{
    public RegionPackException()
    {
    }

    public RegionPackException(string message)
        : base(message)
    {
    }

    public RegionPackException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
