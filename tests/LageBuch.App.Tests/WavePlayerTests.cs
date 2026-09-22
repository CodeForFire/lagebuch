using LageBuch.App.Services;

namespace LageBuch.App.Tests;

// The temp-file half of playback moved out of SystemAlarmService into WavePlayer when synthesized
// speech arrived, so a bundled clip and a fresh utterance travel the same path.
public class WavePlayerTests
{
    [Fact]
    public void Dispose_removes_the_temp_wav_file_TempFileFor_wrote()
    {
        // TempFileFor is only ever called on non-Windows -- Windows plays PlaySound straight from
        // memory, so there's no temp file for Dispose to clean up there.
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        var player = new WavePlayer();
        var path = player.TempFileFor("TaskDue", [1, 2, 3]);

        Assert.NotNull(path);
        Assert.True(File.Exists(path));

        player.Dispose();

        Assert.False(File.Exists(path));
    }

    // Every cue and every utterance writes through here, so a name reused across calls must not
    // accumulate entries that Dispose then deletes twice.
    [Fact]
    public void The_same_name_reuses_one_temp_file()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        using var player = new WavePlayer();

        var first = player.TempFileFor("RetreatAlarm", [1, 2, 3]);
        var second = player.TempFileFor("RetreatAlarm", [4, 5, 6]);

        Assert.Equal(first, second);
    }
}
