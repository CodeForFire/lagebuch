using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using LageBuch.App.Shared.Views;
using LageBuch.AppLogic;
using LageBuch.AppLogic.ViewModels;
using LageBuch.Domain;

namespace LageBuch.Acceptance.Tests;

// #424: the MESSREIHE list in the editor sidebar. Practitioners reported a changed CO value as
// "not documented" -- it was written, but only to a hidden ETB line. The series now sits where
// the value is edited, so the Verlauf is visible without knowing about any checkbox.
//
// Controls are found by x:Name, never by a layout value: AGENTS.md forbids the latter because
// matching on a double is cs/equality-on-floats wearing a LINQ predicate.
public class CoMessprotokollMessreiheTests
{
    private static (Window Window, CoMessprotokollView View, CoMessprotokollViewModel Vm, LocalIncidentSession Session, FixedClock Clock) Show()
    {
        var clock = new FixedClock();
        var session = TestSession.StartNew(
            new FakeStore(),
            clock,
            new SessionOperator("Huber", "FFB 12/1"),
            "/x.fwincident",
            Array.Empty<(string, bool)>(),
            Array.Empty<(string, bool)>());
        session.AddCoBuilding("Haus A", 1, 1);
        var vm = new CoMessprotokollViewModel(session, clock, () => { });
        var view = new CoMessprotokollView { DataContext = vm };
        var window = new Window { Content = view, Width = 1200, Height = 700 };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        return (window, view, vm, session, clock);
    }

    // Doubles as the PR's screenshot source, the convention the other render tests use: a plain
    // test run captures nothing, `RENDER_OUT=<dir>` writes the PNG.
    private static void Capture(Window window, string name)
    {
        var dir = Environment.GetEnvironmentVariable("RENDER_OUT");
        if (string.IsNullOrWhiteSpace(dir))
        {
            return;
        }

        Directory.CreateDirectory(dir);
        using var frame = window.CaptureRenderedFrame()!;
        frame.SavePng(Path.Join(dir, name));
    }

    private static void OpenEg(CoMessprotokollViewModel vm)
    {
        vm.MatrixRows.Single(r => r.Ordinal == 0).Cells[0].OpenEditorCommand.Execute(null);
        Dispatcher.UIThread.RunJobs();
    }

    [AvaloniaFact]
    public void The_sidebar_lists_every_committed_reading()
    {
        var (window, view, vm, session, clock) = Show();
        var haus = session.Incident.Buildings[0].Id;

        session.RecordCoValue(haus, 0, 1, 250);
        clock.Now = clock.Now.AddMinutes(27);
        session.RecordCoValue(haus, 0, 1, 40);
        OpenEg(vm);

        Assert.True(view.GetControl<StackPanel>("MessreihePanel").IsVisible);
        Assert.Equal(2, view.GetControl<ItemsControl>("MessreiheList").ItemCount);
        Capture(window, "co-messreihe-nachher.png");
    }

    [AvaloniaFact]
    public void The_sidebar_hides_the_series_for_an_unmeasured_Wohnung()
    {
        var (_, view, vm, _, _) = Show();

        OpenEg(vm);

        Assert.False(view.GetControl<StackPanel>("MessreihePanel").IsVisible);
    }

    // A single reading still gets its row here: the sidebar is opened for one Wohnung on purpose,
    // so the time and the crew are worth showing even once. The two-readings rule belongs to the
    // tile tooltip and the PDF, where the value is already on screen a line above.
    [AvaloniaFact]
    public void A_single_reading_still_shows_a_row()
    {
        var (_, view, vm, session, _) = Show();

        session.RecordCoValue(session.Incident.Buildings[0].Id, 0, 1, 45);
        OpenEg(vm);

        Assert.True(view.GetControl<StackPanel>("MessreihePanel").IsVisible);
        Assert.Equal(1, view.GetControl<ItemsControl>("MessreiheList").ItemCount);
    }

    // Typing is not measuring: ABBRECHEN must leave the series exactly as it was.
    [AvaloniaFact]
    public void Cancelling_an_edit_adds_no_row()
    {
        var (_, view, vm, session, _) = Show();
        session.RecordCoValue(session.Incident.Buildings[0].Id, 0, 1, 45);
        OpenEg(vm);

        vm.Editor!.CoValue = 999;
        Dispatcher.UIThread.RunJobs();
        vm.CloseEditorCommand.Execute(null);
        Dispatcher.UIThread.RunJobs();

        OpenEg(vm);
        Assert.Equal(1, view.GetControl<ItemsControl>("MessreiheList").ItemCount);
    }
}
