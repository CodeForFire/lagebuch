namespace LageBuch.Domain.CoMeasurement;

public static class CoSeverityClassifier
{
    public const int ElevatedThresholdPpm = 30;
    public const int DangerousThresholdPpm = 200;
    public const int LethalThresholdPpm = 800;

    /// <summary>Above the range a handheld fire-service CO meter typically reads -- not a hard
    /// domain limit, just the point where a reading is more likely a typo (e.g. 9999 vs 999) than
    /// a real measurement, so the UI can flag it for a second look without rejecting it.</summary>
    public const int ImplausibleThresholdPpm = 2000;

    public static CoSeverity SeverityOf(int? ppm) => ppm switch
    {
        >= LethalThresholdPpm => CoSeverity.Lethal,
        >= DangerousThresholdPpm => CoSeverity.Dangerous,
        >= ElevatedThresholdPpm => CoSeverity.Elevated,
        _ => CoSeverity.Normal,
    };

    public static bool IsImplausible(int? ppm) => ppm is { } v && v > ImplausibleThresholdPpm;
}
