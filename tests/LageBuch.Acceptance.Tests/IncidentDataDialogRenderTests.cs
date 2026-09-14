using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using LageBuch.App.Shared.Views;
using LageBuch.AppLogic;
using LageBuch.AppLogic.ViewModels;
using LageBuch.Domain;
using LageBuch.Domain.ValueObjects;
using LageBuch.Persistence.MasterData;

namespace LageBuch.Acceptance.Tests;

// The "Einsatzdaten" overlay (Stichwort, Einsatznummer, Straße, Ortsteil). Hosted directly, same
// idiom as TaskDialogRenderTests; the saved PNG is the PR's "after" screenshot.
public class IncidentDataDialogRenderTests
{
    private static LocalIncidentSession Session()
    {
        var session = LocalIncidentSession.StartNew(
            new FakeStore(),
            new FixedClock(),
            new SessionOperator(AnonymizedExampleData.OperatorSurname, "FFB 12/1"),
            "/x.fwincident",
            Array.Empty<(string, bool)>(),
            Array.Empty<(string, bool)>(),
            new IncidentNumber("B 1.2 260715 123"),
            keyword: "B3P");
        session.SetAddress("Hauptstr. 12", "FFB");
        return session;
    }

    private static T Find<T>(Window window, string name)
        where T : Control =>
        window.GetVisualDescendants().OfType<T>().Single(c => c.Name == name);

    [AvaloniaFact]
    public void Dialog_renders_with_all_four_fields_prefilled()
    {
        var dialogVm = new IncidentDataDialogViewModel(Session(), () => { });

        var view = new IncidentDataDialogView { DataContext = dialogVm };
        var window = new Window { Content = view, Width = 560, Height = 480 };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        Assert.Equal("B3P", Find<TextBox>(window, "IncidentDataKeywordBox").Text);
        Assert.Equal("B 1.2 260715 123", Find<TextBox>(window, "IncidentDataNumberBox").Text);
        Assert.Equal("Hauptstr. 12", Find<TextBox>(window, "IncidentDataStreetBox").Text);
        Assert.Equal("FFB", Find<TextBox>(window, "IncidentDataDistrictBox").Text);
        Assert.True(Find<Button>(window, "IncidentDataSaveButton").IsEnabled);

        var dir = Environment.GetEnvironmentVariable("LAGEBUCH_SHOT_DIR") ?? Path.Combine(Path.GetTempPath(), "lagebuch-shots");
        Directory.CreateDirectory(dir);
        using var frame = window.CaptureRenderedFrame()!;
        frame.SavePng(Path.Combine(dir, "incident-data-dialog.png"));
    }

    [AvaloniaFact]
    public void Dialog_focuses_the_stichwort_field_on_open()
    {
        var dialogVm = new IncidentDataDialogViewModel(Session(), () => { });
        var window = new Window { Content = new IncidentDataDialogView { DataContext = dialogVm }, Width = 560, Height = 480 };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        Assert.Same(Find<TextBox>(window, "IncidentDataKeywordBox"), TopLevel.GetTopLevel(window)!.FocusManager!.GetFocusedElement());
    }

    [AvaloniaFact]
    public void Escape_cancels_and_enter_saves()
    {
        var closed = 0;
        var session = Session();
        var dialogVm = new IncidentDataDialogViewModel(session, () => { });
        dialogVm.Closed += (_, _) => closed++;
        var window = new Window { Content = new IncidentDataDialogView { DataContext = dialogVm }, Width = 560, Height = 480 };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        var street = Find<TextBox>(window, "IncidentDataStreetBox");
        street.Focus();
        window.KeyPressQwerty(PhysicalKey.Escape, RawInputModifiers.None);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(1, closed);

        street.Text = "Nebenstr. 1";
        window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(2, closed);
        Assert.Equal("Nebenstr. 1", session.Incident.Street);
    }
}
