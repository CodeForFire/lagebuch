namespace LageBuch.AppLogic.Services.Weather;

/// <summary>
/// The weather could not be loaded. The message is German and meant to be shown as it is; it never
/// carries a raw exception text (#602).
/// </summary>
public sealed class WeatherException : Exception
{
    public WeatherException()
    {
    }

    public WeatherException(string message)
        : base(message)
    {
    }

    public WeatherException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
