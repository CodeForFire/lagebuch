using System.Runtime.InteropServices;
using Avalonia.Platform;
using Feuerwehr.AppLogic.Services;

namespace Feuerwehr.App.Services;

/// <summary>
/// Plays a looping alarm tone on Windows (the field deployment target) via winmm's PlaySound,
/// fed from a bundled WAV held in memory. On non-Windows hosts (developer machines, headless
/// test runs) it degrades to a silent no-op so nothing crashes. Both Start/Stop are idempotent.
/// </summary>
public sealed class SystemAlarmService : IAlarmService
{
    private const uint SndAsync = 0x0001;   // play asynchronously
    private const uint SndNodefault = 0x0002; // no default beep if it fails
    private const uint SndMemory = 0x0004;  // pszSound points to in-memory WAV
    private const uint SndLoop = 0x0008;    // loop until the next PlaySound call

    private static readonly Uri AlarmAsset = new("avares://Feuerwehr.App/Assets/alarm.wav");

    private readonly byte[]? _wav;
    private bool _sounding;

    public SystemAlarmService()
    {
        // Load the WAV once. Only needed on Windows; skip the work elsewhere.
        if (OperatingSystem.IsWindows())
        {
            using var stream = AssetLoader.Open(AlarmAsset);
            using var ms = new MemoryStream();
            stream.CopyTo(ms);
            _wav = ms.ToArray();
        }
    }

    public void Start()
    {
        if (_sounding || _wav is null || !OperatingSystem.IsWindows())
            return;
        PlaySound(_wav, IntPtr.Zero, SndAsync | SndMemory | SndLoop | SndNodefault);
        _sounding = true;
    }

    public void Stop()
    {
        if (!_sounding || !OperatingSystem.IsWindows())
            return;
        PlaySound(null, IntPtr.Zero, 0); // null sound stops any current playback
        _sounding = false;
    }

    [DllImport("winmm.dll", CharSet = CharSet.Auto)]
    private static extern bool PlaySound(byte[]? data, IntPtr hModule, uint flags);
}
