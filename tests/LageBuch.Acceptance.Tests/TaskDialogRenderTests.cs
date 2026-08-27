using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using LageBuch.App.Shared.Views;
using LageBuch.AppLogic;
using LageBuch.AppLogic.ViewModels;
using LageBuch.Domain;
using LageBuch.Persistence.MasterData;

namespace LageBuch.Acceptance.Tests;

// #137: labeled-field pass over the "Task erstellen" dialog (previously had no render-test
// coverage at all). Hosted directly, same idiom as ForcesTabRenderTests' strength-editor test.
public class TaskDialogRenderTests
{
    [AvaloniaFact]
    public void Task_dialog_renders_with_prefilled_text()
    {
        var session = LocalIncidentSession.StartNew(new FakeStore(), new FixedClock(),
            new SessionOperator(AnonymizedExampleData.OperatorSurname, "FFB 12/1"), "/x.fwincident",
            Array.Empty<(string, bool)>(), Array.Empty<(string, bool)>());
        var masterData = MasterDataSet.Empty with { RadioCallSigns = AnonymizedExampleData.RadioCallSigns };
        var dialogVm = new TaskDialogViewModel(session, masterData, "Lagemeldung übermittelt", () => { });

        var view = new TaskDialogView { DataContext = dialogVm };
        var window = new Window { Content = view, Width = 560, Height = 360 };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        Assert.Equal("Lagemeldung übermittelt", dialogVm.Text);

        var dir = Path.Combine(Path.GetTempPath(), "lagebuch-shots");
        Directory.CreateDirectory(dir);
        using var frame = window.CaptureRenderedFrame()!;
        frame.SavePng(Path.Combine(dir, "task-dialog.png"));
    }
}
