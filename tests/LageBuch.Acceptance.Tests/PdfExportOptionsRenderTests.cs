using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using LageBuch.App.Shared.Views;
using LageBuch.AppLogic.ViewModels;

namespace LageBuch.Acceptance.Tests;

// #262: the section-selection dialog shown before a PDF export, replacing the previous
// unconditional one-click "render everything" flow. Hosted directly, same idiom as
// TaskDialogRenderTests -- this is the "after" screenshot for the PR (there is no prior UI for
// this brand-new overlay to compare against).
public class PdfExportOptionsRenderTests
{
    [AvaloniaFact]
    public void Dialog_renders_with_all_eight_sections_checked_by_default()
    {
        var dialogVm = new PdfExportOptionsViewModel(_ => Task.CompletedTask);

        var view = new PdfExportOptionsView { DataContext = dialogVm };
        var window = new Window { Content = view, Width = 560, Height = 620 };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(8, dialogVm.Items.Count);
        Assert.All(dialogVm.Items, i => Assert.True(i.IsSelected));

        var dir = Path.Combine(Path.GetTempPath(), "lagebuch-shots");
        Directory.CreateDirectory(dir);
        using var frame = window.CaptureRenderedFrame()!;
        frame.SavePng(Path.Combine(dir, "pdf-export-options.png"));
    }
}
