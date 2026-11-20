using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using LageBuch.App.Shared.Views;
using LageBuch.AppLogic.ViewModels;
using LageBuch.Persistence.MasterData;

namespace LageBuch.Acceptance.Tests;

// #994: a Strahlenschutztrupp's row offers STRAHLENSCHUTZ and says what is still owed; the dialog
// it opens has one block of fields per crew member and asks before a dosimeter is declared missing.
public class RadiationDialogRenderTests
{
    private static (IncidentWorkspaceViewModel Vm, Window Window) Workspace()
    {
        var vm = WorkspaceRenderHelper.BuildEditableWorkspaceWithAllBars();
        vm.Scba.NewDesignation = "Strahlenschutztrupp";
        vm.Scba.NewCallSign = "HLF 1";
        vm.Scba.NewTruppfuehrer = AnonymizedExampleData.OperatorSurname;
        vm.Scba.NewTruppmann = AnonymizedExampleData.OperatorSurnameAlt;
        vm.Scba.NewZweiterTruppmann = AnonymizedExampleData.PersonLastName;
        vm.Scba.AddTruppCommand.Execute(null);

        var window = new Window
        {
            Content = new IncidentWorkspaceView { DataContext = vm },
            Width = 1920,
            Height = 1032,
        };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        WorkspaceRenderHelper.SelectTab(window, "ATEMSCHUTZ");
        Dispatcher.UIThread.RunJobs();
        return (vm, window);
    }

    [AvaloniaFact]
    public void A_strahlenschutztrupp_row_offers_the_dialog_and_holds_im_einsatz_back()
    {
        var (vm, window) = Workspace();
        var row = vm.Scba.Trupps[^1];

        Assert.True(row.IsRadiationMonitored);
        Assert.Equal("Dosimeter-Nr. fehlen", row.RadiationHint);
        Assert.False(row.StartCommand.CanExecute(null));

        var truppColumn = window.GetVisualDescendants().OfType<DataGrid>().Single()
            .Columns.Single(c => (string?)c.Header == "TRUPP");
        var cell = truppColumn.GetCellContent(row)!;
        var button = Assert.Single(cell.GetVisualDescendants().OfType<Button>(), b => b.Name == "RadiationButton");
        Assert.True(button.IsVisible);
        Assert.Contains(cell.GetVisualDescendants().OfType<TextBlock>(), t => t.Name == "RadiationHintText" && t.IsVisible);

        // The Angriffstrupp the helper registered has neither.
        var ordinary = truppColumn.GetCellContent(vm.Scba.Trupps[0])!;
        Assert.DoesNotContain(ordinary.GetVisualDescendants().OfType<Button>(), b => b.Name == "RadiationButton" && b.IsVisible);
        window.Close();
    }

    [AvaloniaFact]
    public void The_dialog_has_a_block_per_crew_member_and_asks_before_nicht_vorhanden()
    {
        var (vm, window) = Workspace();
        vm.Scba.Trupps[^1].OpenRadiationCommand.Execute(null);
        Dispatcher.UIThread.RunJobs();

        var view = Assert.Single(window.GetVisualDescendants().OfType<RadiationDialogView>());
        var dialog = Assert.IsType<RadiationDialogViewModel>(vm.PendingRadiationDialog);
        Assert.Equal(3, dialog.Members.Count);
        Assert.Equal(
            3, view.GetVisualDescendants().OfType<TextBox>().Count(t => AutomationProperties.GetName(t) == "OSL-Dosimeter Nummer"));
        Assert.Equal(3, view.GetVisualDescendants().OfType<ComboBox>().Count(c => AutomationProperties.GetName(c) == "Einheit"));
        Assert.All(
            view.GetVisualDescendants().OfType<ComboBox>().Where(c => AutomationProperties.GetName(c) == "Einheit"),
            c => Assert.Equal("mSv", c.SelectedItem));

        var panel = view.GetVisualDescendants().OfType<Border>().Single(b => b.Name == "MissingWarningPanel");
        Assert.False(panel.IsVisible);

        dialog.Members[1].DeclareAlarmMissingCommand.Execute(null);
        Dispatcher.UIThread.RunJobs();

        Assert.True(panel.IsVisible);

        var dir = Environment.GetEnvironmentVariable("RENDER_OUT");
        if (!string.IsNullOrWhiteSpace(dir))
        {
            Directory.CreateDirectory(dir);
            using var frame = window.CaptureRenderedFrame()!;
            frame.SavePng(Path.Join(dir, "strahlenschutz-dialog.png"));
        }

        window.Close();
    }
}
