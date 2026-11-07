using LageBuch.Domain.ValueObjects;

namespace LageBuch.AppLogic.Services.Weather;

/// <summary>Where the Lagekarte gets the current weather for a point. Called only on request.</summary>
public interface IWeatherSource
{
    /// <exception cref="WeatherException">The weather could not be loaded; the message says why, in German.</exception>
    Task<WeatherReading> LoadAsync(GeoPoint at, CancellationToken cancellationToken = default);
}
