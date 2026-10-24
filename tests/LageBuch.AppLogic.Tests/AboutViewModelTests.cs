using LageBuch.AppLogic.Services;
using LageBuch.AppLogic.ViewModels;

namespace LageBuch.AppLogic.Tests;

public class AboutViewModelTests
{
    [Fact]
    public void Shows_the_passed_app_version()
    {
        var vm = new AboutViewModel(new FakeDialogs(), "0.1.0");

        Assert.Equal("0.1.0", vm.Version);
    }

    [Fact]
    public void Points_to_the_CodeForFire_lagebuch_repository()
    {
        var vm = new AboutViewModel(new FakeDialogs(), "0.1.0");

        Assert.Equal("https://github.com/CodeForFire/lagebuch", vm.RepositoryUrl);
    }

    [Fact]
    public void Names_the_MIT_license_and_the_copyright_holder()
    {
        // Must stay in sync with the LICENSE file in the repo root.
        var vm = new AboutViewModel(new FakeDialogs(), "0.1.0");

        Assert.Contains("MIT", vm.LicenseLine, StringComparison.Ordinal);
        Assert.Contains("Thomas Müller", vm.CopyrightLine, StringComparison.Ordinal);
        Assert.Contains("2026", vm.CopyrightLine, StringComparison.Ordinal);
    }

    [Fact]
    public void Close_raises_Closed_so_the_host_clears_the_overlay()
    {
        var vm = new AboutViewModel(new FakeDialogs(), "0.1.0");
        var closed = false;
        vm.Closed += (_, _) => closed = true;

        vm.CloseCommand.Execute(null);

        Assert.True(closed);
    }

    [Fact]
    public async Task OpenRepository_hands_the_repo_url_to_the_dialog_service()
    {
        var dialogs = new FakeDialogs();
        var vm = new AboutViewModel(dialogs, "0.1.0");

        await vm.OpenRepositoryCommand.ExecuteAsync(null);

        Assert.Equal(vm.RepositoryUrl, dialogs.LastOpenedUrl);
        Assert.Null(vm.ErrorMessage);
    }

    [Fact]
    public async Task A_failed_repository_open_is_reported_instead_of_crashing()
    {
        // A minimal OS install can lack any http handler — the dialog must survive it.
        var dialogs = new ThrowingUrlDialogs();
        var vm = new AboutViewModel(dialogs, "0.1.0");

        await vm.OpenRepositoryCommand.ExecuteAsync(null);

        Assert.NotNull(vm.ErrorMessage);
        Assert.Contains("github.com/CodeForFire/lagebuch", vm.ErrorMessage, StringComparison.Ordinal);
    }

    // #603: a crew checks before an Einsatz that the Rückzugsalarm is audible. The app can only
    // tell whether a player ran, so a success still asks the listener to judge.
    [Fact]
    public async Task Testing_the_sound_plays_the_alarm_and_asks_whether_it_was_heard()
    {
        var alarm = new FakeAlarmService();
        var vm = new AboutViewModel(new FakeDialogs(), "0.1.0", alarm);

        await vm.TestSoundCommand.ExecuteAsync(null);

        Assert.Equal(1, alarm.TestsPlayed);
        Assert.False(vm.SoundTestFailed);
        Assert.Contains("Nichts gehört?", vm.SoundTestResult, StringComparison.Ordinal);
    }

    // What the device can see -- no player program, no clip, a platform without audio -- is said
    // plainly, because the Lagebuchführer would otherwise count on an alarm that never sounds.
    [Fact]
    public async Task A_sound_test_that_plays_nothing_says_alarms_are_visual_only()
    {
        var alarm = new FakeAlarmService { TestPlays = false };
        var vm = new AboutViewModel(new FakeDialogs(), "0.1.0", alarm);

        await vm.TestSoundCommand.ExecuteAsync(null);

        Assert.True(vm.SoundTestFailed);
        Assert.Contains("nur auf dem Bildschirm", vm.SoundTestResult, StringComparison.Ordinal);
    }

    // #699: the mute switch lives in ÜBER, next to the sound check.
    [Fact]
    public void Muting_in_Ueber_switches_the_device_alarms_off_and_on()
    {
        var mute = new AlarmMute();
        var vm = new AboutViewModel(new FakeDialogs(), "0.1.0", new FakeAlarmService(), mute);

        Assert.True(vm.CanMuteAlarms);
        Assert.False(vm.IsAlarmMuted);

        vm.IsAlarmMuted = true;
        Assert.True(mute.IsMuted);

        vm.IsAlarmMuted = false;
        Assert.False(mute.IsMuted);
    }

    [Fact]
    public void Ueber_shows_a_mute_set_elsewhere()
    {
        var mute = new AlarmMute();
        mute.Set(true);

        var vm = new AboutViewModel(new FakeDialogs(), "0.1.0", new FakeAlarmService(), mute);

        Assert.True(vm.IsAlarmMuted);
    }

    // The test still plays while muted, but it must not read as "all good": the Einsatz stays
    // silent until the switch goes back off.
    [Fact]
    public async Task A_sound_test_while_muted_says_the_alarms_stay_silent()
    {
        var mute = new AlarmMute();
        mute.Set(true);
        var vm = new AboutViewModel(new FakeDialogs(), "0.1.0", new FakeAlarmService(), mute);

        await vm.TestSoundCommand.ExecuteAsync(null);

        Assert.Contains("stumm", vm.SoundTestResult, StringComparison.Ordinal);
    }

    [Fact]
    public void A_mute_that_cannot_be_saved_is_reported_and_not_shown_as_set()
    {
        var mute = new AlarmMute(Path.Join(Path.GetTempPath(), $"missing-{Guid.NewGuid():N}", "alarm-mute.json"));
        var vm = new AboutViewModel(new FakeDialogs(), "0.1.0", new FakeAlarmService(), mute);

        vm.IsAlarmMuted = true;

        Assert.False(vm.IsAlarmMuted);
        Assert.False(mute.IsMuted);
        Assert.NotNull(vm.ErrorMessage);
    }

    [Fact]
    public void Without_a_mute_switch_there_is_no_checkbox()
    {
        var vm = new AboutViewModel(new FakeDialogs(), "0.1.0", new FakeAlarmService());

        Assert.False(vm.CanMuteAlarms);
    }

    [Fact]
    public void Without_an_alarm_service_there_is_nothing_to_test()
    {
        var vm = new AboutViewModel(new FakeDialogs(), "0.1.0");

        Assert.False(vm.CanTestSound);
        Assert.False(vm.TestSoundCommand.CanExecute(null));
    }

    [Fact]
    public void Seven_quick_logo_taps_request_the_easter_egg()
    {
        var time = new ManualTimeProvider();
        var vm = new AboutViewModel(new FakeDialogs(), "0.1.0", time: time);
        var requests = 0;
        vm.EasterEggRequested += (_, _) => requests++;

        for (var i = 0; i < 6; i++)
        {
            vm.BadgeTappedCommand.Execute(null);
            time.Advance(TimeSpan.FromMilliseconds(400));
        }

        Assert.Equal(0, requests);
        vm.BadgeTappedCommand.Execute(null);
        Assert.Equal(1, requests);
    }

    [Fact]
    public void Slow_logo_taps_do_not_request_the_easter_egg()
    {
        var time = new ManualTimeProvider();
        var vm = new AboutViewModel(new FakeDialogs(), "0.1.0", time: time);
        var requests = 0;
        vm.EasterEggRequested += (_, _) => requests++;

        for (var i = 0; i < 20; i++)
        {
            vm.BadgeTappedCommand.Execute(null);
            time.Advance(TimeSpan.FromMilliseconds(600));
        }

        Assert.Equal(0, requests);
    }

    private sealed class ThrowingUrlDialogs : IFileDialogService
    {
        public Task<string?> PickSaveAsync(string suggestedFileName, string? initialFolder = null) => Task.FromResult<string?>(null);

        public Task<string?> PickOpenAsync() => Task.FromResult<string?>(null);

        public Task<string?> PickExportPdfAsync(string suggestedFileName) => Task.FromResult<string?>(null);

        public Task<string?> PickImportJsonAsync() => Task.FromResult<string?>(null);

        public Task<string?> PickExportJsonAsync(string suggestedFileName) => Task.FromResult<string?>(null);

        public Task<string?> PickAttachmentAsync() => Task.FromResult<string?>(null);

        public Task OpenFileAsync(string path) => Task.CompletedTask;

        public Task OpenUrlAsync(string url) => throw new InvalidOperationException("Kein Handler registriert.");

        public Task OpenMailAsync(string address) => Task.CompletedTask;

        public Task OpenPhoneAsync(string number) => Task.CompletedTask;

        public bool CanShareFiles => false;

        public Task ShareFileAsync(string path, string mimeType) => Task.CompletedTask;
    }
}
