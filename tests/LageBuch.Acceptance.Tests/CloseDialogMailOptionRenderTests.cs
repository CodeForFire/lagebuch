using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using LageBuch.App.Shared.Views;
using LageBuch.AppLogic.ViewModels;

namespace LageBuch.Acceptance.Tests;

// The close dialog's opt-in "PDF exportieren", with "und per E-Mail senden" nested under it (#425).
// Hosted directly, same idiom as PdfExportOptionsRenderTests; the PNGs are the before/after pair for the PR.
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
    public void The_export_option_is_a_labelled_checkbox_that_starts_unticked()
    {
        var vm = new ConfirmDialogViewModel(
            "Einsatz abschließen?", Message, "ABSCHLIESSEN", () => { }, "PDF exportieren");
        var (_, view) = Show(vm);

        var box = view.FindControl<CheckBox>("OptionCheckBox");
        Assert.NotNull(box);
        Assert.True(box.IsVisible);
        Assert.False(box.IsChecked);
        Assert.Equal("PDF exportieren", box.Content);
        Assert.False(view.FindControl<CheckBox>("SubOptionCheckBox")?.IsVisible);
    }

    [AvaloniaFact]
    public void The_mail_option_is_nested_under_the_export_and_enabled_only_once_it_is_ticked()
    {
        var vm = new ConfirmDialogViewModel(
            "Einsatz abschließen?", Message, "ABSCHLIESSEN", () => { }, "PDF exportieren", "und per E-Mail senden");
        var (window, view) = Show(vm);

        var export = view.FindControl<CheckBox>("OptionCheckBox");
        var mail = view.FindControl<CheckBox>("SubOptionCheckBox");
        Assert.NotNull(export);
        Assert.NotNull(mail);
        Assert.True(mail.IsVisible);
        Assert.False(mail.IsEnabled);
        Assert.Equal("und per E-Mail senden", mail.Content);

        export.IsChecked = true;
        Dispatcher.UIThread.RunJobs();
        Assert.True(mail.IsEnabled);

        mail.IsChecked = true;
        Dispatcher.UIThread.RunJobs();
        Assert.True(vm.IsOptionChecked);
        Assert.True(vm.IsSubOptionChecked);
        Save(window, "close-dialog-after.png");
    }
}
