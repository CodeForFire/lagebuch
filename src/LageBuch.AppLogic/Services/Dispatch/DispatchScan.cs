namespace LageBuch.AppLogic.Services.Dispatch;

/// <summary>What one look into the alarm folder found.</summary>
/// <param name="Newest">The most recent alarm still to be answered, or null.</param>
/// <param name="Error">Why the folder could not be read, in German for the Home screen; null when it could.</param>
public sealed record DispatchScan(DispatchAlarm? Newest, string? Error);
