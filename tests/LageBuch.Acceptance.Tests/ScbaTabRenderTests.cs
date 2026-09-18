using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using LageBuch.App.Shared.Views;
using LageBuch.AppLogic.ViewModels;

namespace LageBuch.Acceptance.Tests;

// The Atemschutz tab is the safety-critical heart of the app: this pins that the composite
// state built by WorkspaceRenderHelper (started trupp, pressure control due, Rückzugsalarm
// after 31 min) still renders as the alarm row it must be. Doubles as the README screenshot
// capture (RENDER_OUT), same idiom as FilesTabRenderTests.
public class ScbaTabRenderTests
{
    [AvaloniaFact]
    public void Atemschutz_tab_shows_the_started_trupp_in_rueckzugsalarm()
    {
        var vm = WorkspaceRenderHelper.BuildEditableWorkspaceWithAllBars();
        var window = new Window
        {
            Content = new IncidentWorkspaceView { DataContext = vm },
            Width = 1920,
            Height = 1032,
        };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        WorkspaceRenderHelper.SelectTab(window, "ATEMSCHUTZ");

        var trupp = Assert.Single(vm.Scba.Trupps);
        Assert.True(trupp.IsActive);
        Assert.True(trupp.IsAlarm);

        // #399: the alarming Trupp went under air with nobody standing by, so its Sicherheitstrupp
        // cell warns. Resolved through the row rather than by walking to the TextBlock, because an
        // x:Name inside a DataTemplate is template-scoped and GetControl cannot see it.
        Assert.Equal("kein Sicherheitstrupp", trupp.SafetyTruppHint);
        Assert.True(trupp.HasSafetyTruppHint);
        Assert.Equal(SafetyTruppChoice.NoneDisplay, trupp.SafetyTruppButtonText);

        var truppColumn = window.GetVisualDescendants().OfType<DataGrid>().Single()
            .Columns.Single(c => (string?)c.Header == "TRUPP");
        var cell = truppColumn.GetCellContent(trupp)!;
        Assert.Contains(
            cell.GetVisualDescendants().OfType<TextBlock>(),
            t => t.Name == "SafetyTruppHintText" && t.IsVisible);
        Assert.Contains(
            cell.GetVisualDescendants().OfType<Button>(),
            b => b.Name == "SafetyTruppButton");

        var dir = Environment.GetEnvironmentVariable("RENDER_OUT");
        if (!string.IsNullOrWhiteSpace(dir))
        {
            Directory.CreateDirectory(dir);
            using var frame = window.CaptureRenderedFrame()!;
            frame.SavePng(Path.Join(dir, "atemschutz.png"));
        }
    }
}
