using Feuerwehr.AppLogic.Services;

namespace Feuerwehr.App.Android.Services;

/// <summary>
/// No-op, matching <c>SystemAlarmService</c>'s existing behavior on every non-Windows OS — a real
/// Android alarm (vibration/ringtone) is an explicit follow-up, not required for the core port.
/// </summary>
public sealed class AndroidAlarmService : IAlarmService
{
    public void Start() { }
    public void Stop() { }
}
