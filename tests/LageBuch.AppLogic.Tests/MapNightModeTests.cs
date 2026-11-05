using LageBuch.AppLogic.Services.Map;

namespace LageBuch.AppLogic.Tests;

// The Lagekarte's night colours belong to the device, like the alarm mute: a dark ELW wants them,
// the tablet outside in daylight does not. Kept in a small file of its own, never in the Einsatz.
public class MapNightModeTests : IDisposable
{
    private readonly string _path = Path.Join(Path.GetTempPath(), $"map-night-{Guid.NewGuid():N}.json");

    public void Dispose()
    {
        File.Delete(_path);
        File.Delete(_path + ".tmp");
    }

    [Fact]
    public void A_device_without_the_file_shows_the_day_map()
    {
        Assert.False(new MapNightMode(_path).IsNight);
    }

    [Fact]
    public void The_night_map_is_kept_for_the_next_start()
    {
        new MapNightMode(_path).Set(true);

        Assert.True(new MapNightMode(_path).IsNight);
    }

    [Fact]
    public void A_corrupt_file_reads_as_the_day_map()
    {
        File.WriteAllText(_path, "{ not json");

        Assert.False(new MapNightMode(_path).IsNight);
    }

    [Fact]
    public void Switching_raises_changed_once_and_setting_the_same_value_does_not()
    {
        var mode = new MapNightMode();
        var raised = 0;
        mode.Changed += (_, _) => raised++;

        mode.Set(true);
        mode.Set(true);

        Assert.Equal(1, raised);
    }

    [Fact]
    public void A_file_that_cannot_be_written_keeps_the_old_value()
    {
        var mode = new MapNightMode(Path.Join(_path, "missing-folder", "map-night.json"));

        Assert.ThrowsAny<IOException>(() => mode.Set(true));
        Assert.False(mode.IsNight);
    }
}
