using LageBuch.AppLogic.Services;

namespace LageBuch.App.Android.Services;

/// <summary>
/// No-op: Android plays no audio cues yet, so every alarm there is visual only. A real Android
/// alarm (vibration/ringtone) and the spoken cues are an explicit follow-up. Until then
/// <see cref="PlayTestAsync"/> reports that nothing was played, so "Ton testen" says so instead of
/// leaving a crew to count on an alarm that never sounds (#603).
/// </summary>
public sealed class AndroidAlarmService : IAlarmService
{
    public void Play(AlarmSound sound)
    {
    }

    public Task<bool> PlayTestAsync(CancellationToken cancellationToken = default) => Task.FromResult(false);
}
