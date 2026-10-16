using System.Diagnostics;
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

    [Fact]
    public void RunPlayer_kills_a_player_once_playback_is_cancelled()
    {
        // The desktop players are CLI processes only off Windows; PlaySound cannot be killed.
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var sw = Stopwatch.StartNew();

        // A hung player stands in as a process that would run for 30 s unless it is killed.
        var completed = SystemAlarmService.RunPlayer(new ProcessStartInfo("sleep", "30"), cts.Token);

        sw.Stop();
        Assert.False(completed);
        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(10), $"the player was not killed (took {sw.Elapsed})");
    }

    [Fact]
    public void RunPlayer_reports_a_player_that_ran_to_its_end()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        Assert.True(SystemAlarmService.RunPlayer(new ProcessStartInfo("true"), CancellationToken.None));
    }

    [Fact]
    public void RunPlayer_stays_silent_when_the_player_binary_is_missing()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        Assert.False(SystemAlarmService.RunPlayer(
            new ProcessStartInfo("lagebuch-no-such-audio-player") { UseShellExecute = false },
            CancellationToken.None));
    }
}
