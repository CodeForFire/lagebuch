using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using LageBuch.App.Shared.Views;
using LageBuch.AppLogic;
using LageBuch.AppLogic.ViewModels;
using LageBuch.Domain;
using LageBuch.Persistence.MasterData;

namespace LageBuch.Acceptance.Tests;

// #414: a required field used to look like every other one until the press that failed on it
// (#412). The asterisk is part of the shared "field" template, switched on by the "required" class,
// so these tests prove the mechanism and pin a few fields that must -- or must not -- carry it.
public class RequiredFieldMarkerTests
{
    [AvaloniaFact]
    public void Task_dialog_marks_the_task_text_as_required()
    {
        var (window, view) = ShowTaskDialog();

        Assert.True(RequiredMarkerOf(view, "AUFGABE").IsVisible);
        Capture(window, "required-task-dialog.png");
    }

    // The caption style paints every overline grey, and a value set inside a template ranks below
    // it -- so an asterisk coloured in the template came out grey and read as punctuation.
    [AvaloniaFact]
    public void The_asterisk_is_painted_in_the_error_colour_not_the_caption_grey()
    {
        var (_, view) = ShowTaskDialog();

        var expected = Assert.IsAssignableFrom<ISolidColorBrush>(view.FindResource("ErrorBrush"));
        var actual = Assert.IsAssignableFrom<ISolidColorBrush>(RequiredMarkerOf(view, "AUFGABE").Foreground);
        Assert.Equal(expected.Color, actual.Color);
    }

    [AvaloniaFact]
    public void Task_dialog_leaves_the_optional_fields_unmarked()
    {
        var (_, view) = ShowTaskDialog();

        Assert.False(RequiredMarkerOf(view, "TIMER (MIN)").IsVisible);
        Assert.False(RequiredMarkerOf(view, "ZUGETEILT").IsVisible);
    }

    [AvaloniaFact]
    public void A_required_field_tells_assistive_technology_it_is_required()
    {
        var (_, view) = ShowTaskDialog();

        Assert.True(AutomationProperties.GetIsRequiredForForm(view.GetControl<TextBox>("TaskDialogText")));
    }

    // Everything in the Einsatzdaten dialog is optional on purpose -- a first alarm often has no
    // Einsatznummer yet. A marker here would be a lie, so it is guarded rather than just not added.
    [AvaloniaFact]
    public void Einsatzdaten_dialog_marks_no_field_as_required()
    {
        var view = new IncidentDataDialogView { DataContext = new IncidentDataDialogViewModel(Session(), () => { }) };
        var window = new Window { Content = view, Width = 560, Height = 360 };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        var markers = view.GetVisualDescendants()
            .OfType<TextBlock>()
            .Where(text => text.Name == "RequiredMarker")
            .ToList();
        Assert.NotEmpty(markers); // the template part exists ...
        Assert.All(markers, marker => Assert.False(marker.IsVisible)); // ... and stays hidden
    }

    [AvaloniaFact]
    public void Forces_dock_marks_wache_and_funkrufname_but_not_the_rest()
    {
        var vm = new ForcesViewModel(
            Session(),
            new FixedClock(),
            MasterDataSet.Empty with { Vehicles = AnonymizedExampleData.Vehicles },
            () => { });
        var view = new ForcesView { DataContext = vm };
        var window = new Window { Content = view, Width = 1460, Height = 620 };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        Assert.True(RequiredMarkerOf(view, "FEUERWEHR / WACHE").IsVisible);
        Assert.True(RequiredMarkerOf(view, "FUNKRUFNAME").IsVisible);
        Assert.False(RequiredMarkerOf(view, "STÄRKE ZF / GF / MANN").IsVisible);
        Assert.False(RequiredMarkerOf(view, "BEMERKUNG").IsVisible);
        Capture(window, "required-forces.png");
    }

    private static LocalIncidentSession Session() =>
        TestSession.StartNew(
            new FakeStore(),
            new FixedClock(),
            new SessionOperator(AnonymizedExampleData.OperatorSurname, "FFB 12/1"),
            "/x.fwincident",
            Array.Empty<(string, bool)>(),
            Array.Empty<(string, bool)>());

    private static (Window Window, TaskDialogView View) ShowTaskDialog()
    {
        var masterData = MasterDataSet.Empty with { Vehicles = AnonymizedExampleData.Vehicles };
        var vm = new TaskDialogViewModel(Session(), masterData, string.Empty, () => { });

        var view = new TaskDialogView { DataContext = vm };
        var window = new Window { Content = view, Width = 560, Height = 360 };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        return (window, view);
    }

    // The marker belongs to the shared field template, so it is reached through the wrapper whose
    // Header names it, the same way FieldErrorRenderTests reaches the message.
    private static TextBlock RequiredMarkerOf(Visual root, string header) =>
        root.GetVisualDescendants()
            .OfType<HeaderedContentControl>()
            .Single(field => (field.Header as string) == header)
            .GetVisualDescendants()
            .OfType<TextBlock>()
            .Single(text => text.Name == "RequiredMarker");

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
}
