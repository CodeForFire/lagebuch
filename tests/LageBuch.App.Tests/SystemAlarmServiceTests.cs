using LageBuch.App.Services;
using LageBuch.AppLogic.Services;

namespace LageBuch.App.Tests;

public class SystemAlarmServiceTests
{
    // A cue without a clip plays as silence by design, so a renamed or deleted asset would ship a
    // mute alarm without an error anywhere. Every cue must find its clip.
    // The csproj bundles Assets/*.wav, so a file present there is a clip in the build.
    [Fact]
    public void Every_alarm_sound_has_a_bundled_voice_clip()
    {
        var assets = Path.Join(RepoRoot(), "src", "LageBuch.App", "Assets");

        Assert.All(Enum.GetValues<AlarmSound>(), sound =>
        {
            Assert.True(SystemAlarmService.VoiceAssets.TryGetValue(sound, out var file), $"no voice clip mapped for {sound}");
            Assert.True(File.Exists(Path.Join(assets, file)), $"{file} for {sound} is not in {assets}");
        });
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Join(dir.FullName, "LageBuch.sln")))
        {
            dir = dir.Parent;
        }

        return dir?.FullName ?? throw new InvalidOperationException("LageBuch.sln not found above " + AppContext.BaseDirectory);
    }

    [Fact]
    public void Dispose_removes_the_temp_wav_file_TempFileFor_wrote()
    {
        // TempFileFor is only ever called on non-Windows — Windows plays PlaySound straight from
        // memory, so there's no temp file for Dispose to clean up there.
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        var service = new SystemAlarmService();
        var path = service.TempFileFor(AlarmSound.TaskDue, new byte[] { 1, 2, 3 });

        Assert.NotNull(path);
        Assert.True(File.Exists(path));

        service.Dispose();

        Assert.False(File.Exists(path));
    }
}
