using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using LageBuch.App.Shared.Views;
using LageBuch.AppLogic.ViewModels;
using LageBuch.Domain;
using LageBuch.Persistence.MasterData;

namespace LageBuch.Acceptance.Tests;

// #412: a required field that is empty used to leave the button grey and say nothing. The message
// now rides in the shared "field" wrapper, so proving it once here proves the mechanism for every
// form that uses the wrapper -- what each form declares as required is its own unit test's job.
public class FieldErrorRenderTests
{
    [AvaloniaFact]
    public void The_button_stays_live_and_the_empty_field_names_itself()
    {
        var (window, view, vm) = ShowTaskDialog(string.Empty);

        var error = FieldErrorOf(view, "AUFGABE");
        Assert.False(error.IsVisible); // quiet until the operator asks

        // The press is the question. A disabled button could not be asked.
        Assert.True(view.GetControl<Button>("TaskDialogSaveButton").IsEnabled);
        vm.SaveCommand.Execute(null);
        Dispatcher.UIThread.RunJobs();

        Assert.True(error.IsVisible);
        Assert.Equal(ValidationMessages.Required, error.Text);
        Capture(window, "field-error-task-dialog.png");
    }

    [AvaloniaFact]
    public void Typing_into_the_field_takes_the_message_away_again()
    {
        var (_, view, vm) = ShowTaskDialog(string.Empty);
        vm.SaveCommand.Execute(null);
        Dispatcher.UIThread.RunJobs();
        var error = FieldErrorOf(view, "AUFGABE");
        Assert.True(error.IsVisible);

        view.GetControl<TextBox>("TaskDialogText").Text = "Riegelstellung setzen";
        Dispatcher.UIThread.RunJobs();

        Assert.False(error.IsVisible);
    }

    [AvaloniaFact]
    public void A_field_with_nothing_wrong_shows_no_message()
    {
        var (_, view, vm) = ShowTaskDialog("Lagemeldung übermittelt");

        vm.SaveCommand.Execute(null);
        Dispatcher.UIThread.RunJobs();

        Assert.False(FieldErrorOf(view, "AUFGABE").IsVisible);
        Assert.False(FieldErrorOf(view, "TIMER (MIN)").IsVisible);
    }

    private static (Window Window, TaskDialogView View, TaskDialogViewModel Vm) ShowTaskDialog(string text)
    {
        var session = TestSession.StartNew(
            new FakeStore(),
            new FixedClock(),
            new SessionOperator(AnonymizedExampleData.OperatorSurname, "FFB 12/1"),
            "/x.fwincident",
            Array.Empty<(string, bool)>(),
            Array.Empty<(string, bool)>());
        var masterData = MasterDataSet.Empty with { Vehicles = AnonymizedExampleData.Vehicles };
        var vm = new TaskDialogViewModel(session, masterData, text, () => { });

        var view = new TaskDialogView { DataContext = vm };
        var window = new Window { Content = view, Width = 560, Height = 360 };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        return (window, view, vm);
    }

    // The message is part of the shared field template, so it is reached through the wrapper it
    // belongs to rather than by a per-view x:Name -- the wrapper's Header is what identifies it.
    private static TextBlock FieldErrorOf(Visual root, string header) =>
        root.GetVisualDescendants()
            .OfType<HeaderedContentControl>()
            .Single(field => (field.Header as string) == header)
            .GetVisualDescendants()
            .OfType<TextBlock>()
            .Single(text => text.Name == "FieldError");

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
