using LageBuch.AppLogic.Services;

namespace LageBuch.AppLogic.Tests;

// #699: "Alarmtöne stumm schalten" in ÜBER. A device-local switch, kept in its own small file
// beside recent.json -- never in the Einsatz file, never synced.
public class AlarmMuteTests : IDisposable
{
    private readonly string _path = Path.Join(Path.GetTempPath(), $"alarm-mute-{Guid.NewGuid():N}.json");

    public void Dispose()
    {
        File.Delete(_path);
        File.Delete(_path + ".tmp");
    }

    [Fact]
    public void Muting_silences_every_cue_including_the_Rueckzugsalarm()
    {
        var inner = new FakeAlarmService();
        var mute = new AlarmMute();
        var alarm = new MutedAlarmService(inner, mute);

        mute.Set(true);
        foreach (var sound in Enum.GetValues<AlarmSound>())
        {
            alarm.Play(sound);
        }

        Assert.Empty(inner.Played);
    }

    [Fact]
    public void Unmuted_cues_play_as_before()
    {
        var inner = new FakeAlarmService();
        var mute = new AlarmMute();
        var alarm = new MutedAlarmService(inner, mute);

        alarm.Play(AlarmSound.RetreatAlarm);
        mute.Set(true);
        alarm.Play(AlarmSound.RetreatAlarm);
        mute.Set(false);
        alarm.Play(AlarmSound.TaskDue);

        Assert.Equal(new[] { AlarmSound.RetreatAlarm, AlarmSound.TaskDue }, inner.Played);
    }

    // The sound check is a deliberate tap: it still plays, so a crew can check the speaker
    // before switching the alarms back on.
    [Fact]
    public async Task The_sound_check_still_plays_while_muted()
    {
        var inner = new FakeAlarmService();
        var mute = new AlarmMute();
        mute.Set(true);

        var played = await new MutedAlarmService(inner, mute).PlayTestAsync();

        Assert.True(played);
        Assert.Equal(1, inner.TestsPlayed);
    }

    [Fact]
    public void The_switch_is_kept_on_this_device_and_read_back_on_the_next_start()
    {
        new AlarmMute(_path).Set(true);
        Assert.True(new AlarmMute(_path).IsMuted);

        new AlarmMute(_path).Set(false);
        Assert.False(new AlarmMute(_path).IsMuted);
    }

    [Fact]
    public void A_missing_or_corrupt_file_reads_as_alarms_on()
    {
        Assert.False(new AlarmMute(_path).IsMuted);

        File.WriteAllText(_path, "{ kaputt");
        Assert.False(new AlarmMute(_path).IsMuted);
    }

    [Fact]
    public void Changed_fires_only_on_a_real_change()
    {
        var mute = new AlarmMute();
        var changes = 0;
        mute.Changed += (_, _) => changes++;

        mute.Set(false);
        mute.Set(true);
        mute.Set(true);

        Assert.Equal(1, changes);
    }

    [Fact]
    public void A_switch_that_cannot_be_saved_throws_and_keeps_its_value()
    {
        var mute = new AlarmMute(Path.Join(_path + ".missing-dir", "alarm-mute.json"));

        Assert.ThrowsAny<IOException>(() => mute.Set(true));
        Assert.False(mute.IsMuted);
    }
}
