using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using LageBuch.App.Shared.Views;
using LageBuch.AppLogic;
using LageBuch.AppLogic.Services;
using LageBuch.AppLogic.ViewModels;
using LageBuch.Domain;
using LageBuch.Persistence.MasterData;

namespace LageBuch.Acceptance.Tests;

// Issue #76: the reworked "Kräfte" tab — labelled entry dock with vehicle preset, 1/1/2
// strength in grid and header tile, correction editor and Verlauf. Doubles as the PR
// screenshot capture, same idiom as FilesTabRenderTests.
public class ForcesTabRenderTests
{
    private static (Window Window, IncidentWorkspaceViewModel Vm) ShowWorkspace()
    {
        var session = LocalIncidentSession.StartNew(new FakeStore(), new FixedClock(),
            new SessionOperator("Müller", "FFB 12/1"), "/x.fwincident",
            Array.Empty<(string, bool)>(), Array.Empty<(string, bool)>());
        var vm = new IncidentWorkspaceViewModel(session, new FixedClock(), new NoopTicker(),
            MasterData(), new FakeDialogs(), new NoopAlarmService(), new NoopIncidentHostController());
        var window = new Window { Content = new IncidentWorkspaceView { DataContext = vm }, Width = 1920, Height = 1032 };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        return (window, vm);
    }

    private static MasterDataSet MasterData() => MasterDataSet.Empty with
    {
        Brigades = new[] { "FFB Wache 1", "Aich" },
        UnitStatus = new[] { "Alarmiert", "Auf Anfahrt", "Im Einsatz" },
        RadioCallSigns = new[] { "FFB 1/40/1", "FFB 1/44/1", "Aich 42/1" },
        Vehicles = new[]
        {
            new Vehicle("FFB Wache 1", "FFB 1/40/1", 9),
            new Vehicle("FFB Wache 1", "FFB 1/44/1", 6),
            new Vehicle("Aich", "Aich 42/1", 6),
        },
    };

    private static TabControl Tabs(Window window) =>
        ((IncidentWorkspaceView)window.Content!).GetControl<TabControl>("ModuleTabs");

    private static void Capture(Window window, string name)
    {
        var dir = Path.Combine(Path.GetTempPath(), "lagebuch-shots");
        Directory.CreateDirectory(dir);
        using var frame = window.CaptureRenderedFrame()!;
        frame.SavePng(Path.Combine(dir, name));
    }

    [AvaloniaFact]
    public void Vehicle_selection_presets_the_dock_and_the_row_shows_1_1_2_strength()
    {
        var (window, vm) = ShowWorkspace();
        Tabs(window).SelectedIndex = 2; // KRÄFTE
        Dispatcher.UIThread.RunJobs();

        vm.Forces.NewBrigade = "FFB Wache 1";
        Assert.Equal(new[] { "FFB 1/40/1", "FFB 1/44/1" }, vm.Forces.VehicleOptions.Select(v => v.CallSign));

        vm.Forces.SelectedVehicle = vm.Forces.VehicleOptions[0];
        Assert.Equal("FFB 1/40/1", vm.Forces.NewCallSign);
        Assert.Equal(1, vm.Forces.NewOfficerCount);
        Assert.Equal(8, vm.Forces.NewMannschaftCount);
        Assert.Equal(0, vm.Forces.NewScbaCount);

        vm.Forces.AddForceCommand.Execute(null);
        Dispatcher.UIThread.RunJobs();

        // Row renders the GF/Mann/Gesamt format; the header tile mirrors the total.
        var row = Assert.Single(vm.Forces.Forces);
        Assert.Equal("1/8/9", row.StrengthText);
        Assert.Equal("1/8/9", vm.Forces.TotalStrengthText);

        Capture(window, "forces-vehicle-preset.png");
    }

    [AvaloniaFact]
    public void A_strength_correction_is_committed_as_one_edit_and_exposes_the_verlauf()
    {
        var (window, vm) = ShowWorkspace();
        Tabs(window).SelectedIndex = 2;
        vm.Forces.NewBrigade = "Aich";
        vm.Forces.NewMannschaftCount = 6;
        vm.Forces.AddForceCommand.Execute(null);

        var row = Assert.Single(vm.Forces.Forces);
        row.OfficerCount = 1;
        row.MannschaftCount = 7;
        row.ScbaCount = 3;
        row.CommitStrength();
        Dispatcher.UIThread.RunJobs();

        // The rebuilt row carries one history record; its Verlauf line names both states.
        var edited = Assert.Single(vm.Forces.Forces);
        var edit = Assert.Single(edited.Edits);
        Assert.Equal((0, 6, 0), (edit.PreviousOfficerCount, edit.PreviousPersonnelCount, edit.PreviousScbaCount));
        Assert.True(edited.HasHistory);
        Assert.Contains("Stärke 0/6/6 → 1/7/8", Assert.Single(edited.EditLines));

        Capture(window, "forces-strength-verlauf.png");
    }
}
