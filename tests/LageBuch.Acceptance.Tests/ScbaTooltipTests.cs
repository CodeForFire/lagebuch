using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using LageBuch.App.Shared.Views;
using LageBuch.AppLogic.ViewModels;

namespace LageBuch.Acceptance.Tests;

public class ScbaTooltipTests
{
    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void Transition_buttons_explain_the_action_and_the_two_pressure_states(bool narrow)
    {
        using var vm = WorkspaceRenderHelper.BuildEditableWorkspaceWithAllBars();
        var scba = vm.Scba;
        scba.NewDesignation = "Angriffstrupp";
        scba.NewTruppfuehrer = "Müller";
        scba.NewTruppmann = "Maier";
        scba.NewZweiterTruppmann = string.Empty;
        scba.AddTruppCommand.Execute(null);
        var row = scba.Trupps[^1];
        vm.IsNarrow = narrow;
        var view = new ScbaView { DataContext = scba };
        var workspace = new Panel { Name = "WorkspaceRoot", Children = { view } };
        workspace.Classes.Set("narrow", narrow);
        var window = new Window { Content = workspace, Width = narrow ? 420 : 1400, Height = 900 };
        window.Show();
        try
        {
            Dispatcher.UIThread.RunJobs();
            AssertTip(view, row, "IM EINSATZ", "Bereitstehenden Trupp in den Einsatz schicken.");
            Capture(window, view, row, narrow);
            row.StartCommand.Execute(null);
            Dispatcher.UIThread.RunJobs();
            AssertTip(view, row, "DRUCK", "Aktuellen Flaschendruck des Trupps im Einsatz erfassen.");
            AssertTip(view, row, "RÜCKZUG", "Rückzug für den Trupp im Einsatz einleiten.");
            row.WithdrawCommand.Execute(null);
            Dispatcher.UIThread.RunJobs();
            AssertTip(view, row, "DRUCK", "Aktuellen Flaschendruck des Trupps auf dem Rückzug erfassen.");
            AssertTip(view, row, "ABGENOMMEN", "Zurückgekehrten Trupp als abgenommen markieren.");
        }
        finally
        {
            window.Close();
        }
    }

    private static void Capture(Window window, ScbaView view, ScbaTruppRow row, bool narrow)
    {
        var output = Environment.GetEnvironmentVariable("RENDER_OUT");
        if (string.IsNullOrWhiteSpace(output))
        {
            return;
        }

        Directory.CreateDirectory(output);
        var button = view.GetVisualDescendants().OfType<Button>()
            .First(b => b.IsEffectivelyVisible && "IM EINSATZ".Equals(b.Content) && ReferenceEquals(b.DataContext, row));
        ToolTip.SetIsOpen(button, true);
        Dispatcher.UIThread.RunJobs();
        using var frame = window.CaptureRenderedFrame();
        Assert.NotNull(frame);
        frame.SavePng(Path.Join(output, narrow ? "scba-narrow.png" : "scba-wide.png"));
        ToolTip.SetIsOpen(button, false);
    }

    private static void AssertTip(ScbaView view, ScbaTruppRow row, string label, string tip)
    {
        var button = Assert.Single(
            view.GetVisualDescendants().OfType<Button>(),
            b => b.IsEffectivelyVisible && label.Equals(b.Content) && ReferenceEquals(b.DataContext, row));
        Assert.Equal(tip, ToolTip.GetTip(button));
    }
}
