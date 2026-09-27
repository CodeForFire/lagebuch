using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using LageBuch.App.Shared.Views;
using LageBuch.AppLogic.ViewModels;

namespace LageBuch.Acceptance.Tests;

// The close dialog's opt-in "PDF erstellen und per E-Mail senden". Hosted directly, same idiom as
// PdfExportOptionsRenderTests; the PNGs are the before/after pair for the PR.
public class CloseDialogMailOptionRenderTests
{
    private const string Message = "Der Einsatz wird unwiderruflich abgeschlossen und schreibgeschützt. Fortfahren?";

    private static (Window Window, ConfirmDialogView View) Show(ConfirmDialogViewModel vm)
    {
        var view = new ConfirmDialogView { DataContext = vm };
        var window = new Window { Content = view, Width = 560, Height = 360 };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        return (window, view);
    }

    private static void Save(Window window, string name)
    {
        var dir = Path.Join(Path.GetTempPath(), "lagebuch-shots");
        Directory.CreateDirectory(dir);
        using var frame = window.CaptureRenderedFrame()!;
        frame.SavePng(Path.Join(dir, name));
    }

    [AvaloniaFact]
    public void A_plain_confirm_shows_no_checkbox()
    {
        var (window, view) = Show(new ConfirmDialogViewModel("Einsatz abschließen?", Message, "ABSCHLIESSEN", () => { }));

        Assert.False(view.FindControl<CheckBox>("OptionCheckBox")?.IsVisible);
        Save(window, "close-dialog-before.png");
    }

    [AvaloniaFact]
    public void The_mail_option_is_a_labelled_checkbox_that_starts_unticked()
    {
        var vm = new ConfirmDialogViewModel(
            "Einsatz abschließen?", Message, "ABSCHLIESSEN", () => { }, "PDF erstellen und per E-Mail senden");
        var (window, view) = Show(vm);

        var box = view.FindControl<CheckBox>("OptionCheckBox");
        Assert.NotNull(box);
        Assert.True(box.IsVisible);
        Assert.False(box.IsChecked);
        Assert.Equal("PDF erstellen und per E-Mail senden", box.Content);

        box.IsChecked = true;
        Dispatcher.UIThread.RunJobs();
        Assert.True(vm.IsOptionChecked);
        Save(window, "close-dialog-after.png");
    }
}
