using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CommunityToolkit.Mvvm.Input;
using LageBuch.App.Shared.Behaviors;
using LageBuch.App.Shared.Views;
using LageBuch.AppLogic;
using LageBuch.AppLogic.Services;
using LageBuch.AppLogic.ViewModels;
using LageBuch.Domain;
using LageBuch.Persistence.MasterData;

namespace LageBuch.Acceptance.Tests;

// #538: the flows the overlay contract exists for, end to end, and the Overlay behaviour's own
// rules on a minimal overlay and a minimal inline panel. OverlayContractTests checks the four clauses on every overlay.
public class OverlayKeyboardTests
{
    private static (Window Window, IncidentWorkspaceViewModel Vm) ShowWorkspace(Action<LocalIncidentSession>? seed = null)
    {
        var session = TestSession.StartNew(
            new FakeStore(),
            new FixedClock(),
            new SessionOperator(AnonymizedExampleData.OperatorSurname, "FFB 12/1"),
            "/x.fwincident",
            Array.Empty<(string, bool)>(),
            Array.Empty<(string, bool)>());
        seed?.Invoke(session);
        var vm = new IncidentWorkspaceViewModel(
            session,
            new FixedClock(),
            new NoopTicker(),
            WorkspaceRenderHelper.MasterData(),
            new FakeDialogs(),
            new NoopAlarmService(),
            new NoopIncidentHostController());
        var window = new Window { Content = new IncidentWorkspaceView { DataContext = vm }, Width = 1920, Height = 1032 };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        return (window, vm);
    }

    private static T Named<T>(Window window, string name)
        where T : Control =>
        window.GetVisualDescendants().OfType<T>().Single(c => c.Name == name);

    // The worst case from the audit: focus stayed in the ETB text box behind the TaskDialog, so
    // whatever the Lagebuchführer typed next went into a hidden field.
    [AvaloniaFact]
    public void A_task_is_created_from_the_ETB_by_keyboard_and_focus_returns_to_the_ETB()
    {
        var (window, vm) = ShowWorkspace();
        WorkspaceRenderHelper.SelectTab(window, "ETB");
        Named<TextBox>(window, "EtbTextBox").Focus(NavigationMethod.Tab);
        Dispatcher.UIThread.RunJobs();
        window.Type("Wasserversorgung sicherstellen");
        var opener = Named<Button>(window, "EtbAddAndTaskButton");
        opener.Focus(NavigationMethod.Tab);
        Dispatcher.UIThread.RunJobs();

        window.Press(PhysicalKey.Enter);

        Assert.NotNull(vm.PendingTaskDialog);
        window.AssertFocused(Named<TextBox>(window, "TaskDialogText"));

        window.Press(PhysicalKey.Enter);

        Assert.Null(vm.PendingTaskDialog);
        Assert.Single(vm.Tasks.Rows);
        window.AssertFocused(opener);
    }

    // Contract 1, destructive confirms: focus starts on ABBRECHEN, and Enter activates the focused
    // button, so a reflex Enter after a remove cancels.
    [AvaloniaFact]
    public void Enter_on_a_remove_confirm_removes_nothing()
    {
        var (window, vm) = ShowWorkspace();
        WorkspaceRenderHelper.SelectTab(window, "KRÄFTE");
        vm.Forces.SelectedVehicle = vm.Forces.VehicleOptions[0];
        vm.Forces.AddForceCommand.Execute(null);
        Dispatcher.UIThread.RunJobs();
        vm.Forces.Forces[0].RemoveCommand.Execute(null);
        Dispatcher.UIThread.RunJobs();
        Assert.NotNull(vm.PendingConfirm);

        window.Press(PhysicalKey.Enter);

        Assert.Null(vm.PendingConfirm);
        Assert.Single(vm.Forces.Forces);
    }

    // #538, an inline panel end to end: Enter on a row's edit button opens the panel with focus in
    // the text, Enter saves, and focus goes back to that row's button, which the save kept.
    [AvaloniaFact]
    public void An_ETB_entry_is_edited_by_keyboard_and_focus_returns_to_its_row()
    {
        var (window, vm) = ShowWorkspace();
        WorkspaceRenderHelper.SelectTab(window, "ETB");
        vm.Etb.NewText = "Lagemeldung übermittelt";
        vm.Etb.AddEntryCommand.Execute(null);
        Dispatcher.UIThread.RunJobs();
        var edit = Named<DataGrid>(window, "EtbGrid").GetVisualDescendants().OfType<Button>()
            .First(b => b.IsEffectivelyVisible && AutomationProperties.GetName(b) == "Bearbeiten");
        edit.Focus(NavigationMethod.Tab);
        Dispatcher.UIThread.RunJobs();

        window.Press(PhysicalKey.Enter);

        Assert.True(vm.Etb.IsEditing);
        window.AssertFocused(Named<TextBox>(window, "EditTextBox"));

        window.Type("Lagemeldung an ILS übermittelt");
        window.Press(PhysicalKey.Enter);

        Assert.False(vm.Etb.IsEditing);
        Assert.Contains(vm.Etb.Entries, e => e.Text == "Lagemeldung an ILS übermittelt");
        window.AssertFocused(edit);
    }

    // Contract 1 on an inline confirm: HAUS ENTFERNEN, then a reflex Enter, keeps the house.
    [AvaloniaFact]
    public void Enter_on_the_CO_remove_house_panel_removes_nothing()
    {
        var (window, vm) = ShowWorkspace(s => s.AddCoBuilding("Mehrfamilienhaus A", 2, 2));
        WorkspaceRenderHelper.SelectTab(window, "CO-MESSUNG");
        var remove = Named<Button>(window, "RemoveBuildingButton");
        remove.Focus(NavigationMethod.Tab);
        Dispatcher.UIThread.RunJobs();

        window.Press(PhysicalKey.Enter);
        Assert.True(vm.CoMessprotokoll.IsRemoveBuildingConfirmOpen);
        window.Press(PhysicalKey.Enter);

        Assert.False(vm.CoMessprotokoll.IsRemoveBuildingConfirmOpen);
        Assert.Single(vm.CoMessprotokoll.BuildingOptions);
        window.AssertFocused(remove);
    }

    // Arriving at a module by the rail is not opening its panel: an inline panel that is still open
    // from before does not pull focus in (#542's "the rail doesn't steal focus").
    [AvaloniaFact]
    public void Returning_to_a_module_with_an_open_panel_does_not_pull_focus_into_it()
    {
        var (window, vm) = ShowWorkspace();
        WorkspaceRenderHelper.SelectTab(window, "CO-MESSUNG");
        vm.CoMessprotokoll.AddBuildingCommand.Execute(null);
        Dispatcher.UIThread.RunJobs();
        Assert.True(vm.CoMessprotokoll.IsAddBuildingDialogOpen);

        WorkspaceRenderHelper.SelectTab(window, "KRÄFTE");
        WorkspaceRenderHelper.SelectTab(window, "CO-MESSUNG");

        Assert.True(vm.CoMessprotokoll.IsAddBuildingDialogOpen);
        Assert.False(window.IsFocusWithin(Named<Border>(window, "AddBuildingPanel")), $"focus is on {window.DescribeFocus()}");
    }

    // The busy state disables the whole form, focused button included; focus must not fall out to
    // the page behind, where the next key would land.
    [AvaloniaFact]
    public void Pdf_export_keeps_focus_inside_while_it_is_busy()
    {
        var export = new TaskCompletionSource();
        var vm = new PdfExportOptionsViewModel(_ => export.Task);
        var view = new PdfExportOptionsView { DataContext = vm };
        var behind = new TextBox { Name = "Behind" };
        var window = new Window { Content = new Panel { Children = { behind, view } }, Width = 800, Height = 600 };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        var exportButton = view.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "ExportButton");
        exportButton.Focus(NavigationMethod.Tab);
        Dispatcher.UIThread.RunJobs();

        window.Press(PhysicalKey.Enter);

        Assert.True(vm.IsBusy);
        window.AssertFocusWithin(view);
        window.Tab();
        window.AssertFocusWithin(view);

        export.SetResult();
        Dispatcher.UIThread.RunJobs();
    }

    private sealed record Minimal(Window Window, Border Root, TextBox Field, TextBox Notes, Button Opener, Panel Host);

    private static Minimal ShowMinimal(Action<Border> configure)
    {
        var field = new TextBox { Name = "Field" };
        var notes = new TextBox { Name = "Notes", AcceptsReturn = true };
        var root = new Border { Child = new StackPanel { Children = { field, notes, new Button { Content = "OK" } } } };
        configure(root);
        var opener = new Button { Name = "Opener", Content = "ÖFFNEN" };
        var host = new Panel { Children = { opener } };
        var window = new Window { Content = host, Width = 800, Height = 600 };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        opener.Focus(NavigationMethod.Tab);
        Dispatcher.UIThread.RunJobs();
        host.Children.Add(root);
        Dispatcher.UIThread.RunJobs();
        return new(window, root, field, notes, opener, host);
    }

    private sealed record Inline(Window Window, Border Panel, TextBox Field, Button Opener, TextBox Page, StackPanel Host);

    // An inline panel: in the tree from the start, hidden, beside a page that stays live.
    private static Inline ShowInline(Action<Border>? configure = null)
    {
        var field = new TextBox { Name = "Field" };
        var panel = new Border { IsVisible = false, Child = new StackPanel { Children = { field, new Button { Content = "OK" } } } };
        Overlay.SetIsInline(panel, true);
        Overlay.SetCancelCommand(panel, new RelayCommand(() => panel.IsVisible = false));
        configure?.Invoke(panel);
        var opener = new Button { Name = "Opener", Content = "BEARBEITEN" };
        var page = new TextBox { Name = "Page" };
        var host = new StackPanel { Children = { page, opener, panel } };
        var window = new Window { Content = host, Width = 800, Height = 600 };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        opener.Focus(NavigationMethod.Tab);
        Dispatcher.UIThread.RunJobs();
        return new(window, panel, field, opener, page, host);
    }

    [AvaloniaFact]
    public void An_inline_panel_takes_focus_when_shown_and_gives_it_back_on_Esc()
    {
        var o = ShowInline();

        o.Panel.IsVisible = true;
        Dispatcher.UIThread.RunJobs();
        o.Window.AssertFocused(o.Field);

        o.Window.Press(PhysicalKey.Escape);

        Assert.False(o.Panel.IsVisible);
        o.Window.AssertFocused(o.Opener);
    }

    // Not modal: the page beside it stays live.
    [AvaloniaFact]
    public void Focus_that_leaves_an_inline_panel_is_not_pulled_back_nor_moved_when_it_closes()
    {
        var o = ShowInline();
        o.Panel.IsVisible = true;
        Dispatcher.UIThread.RunJobs();

        o.Page.Focus(NavigationMethod.Pointer);
        Dispatcher.UIThread.RunJobs();
        o.Window.AssertFocused(o.Page);

        o.Panel.IsVisible = false;
        Dispatcher.UIThread.RunJobs();
        o.Window.AssertFocused(o.Page);
    }

    [AvaloniaFact]
    public void Focus_falls_back_when_the_opener_is_gone()
    {
        var o = ShowInline();
        Overlay.SetFallbackFocus(o.Panel, o.Page);
        o.Panel.IsVisible = true;
        Dispatcher.UIThread.RunJobs();

        // Saving rebuilt the row, and its button with it.
        o.Host.Children.Remove(o.Opener);
        o.Window.Press(PhysicalKey.Escape);

        o.Window.AssertFocused(o.Page);
    }

    [AvaloniaFact]
    public void Enter_runs_the_primary_command_but_not_inside_multi_line_text()
    {
        var runs = 0;
        var o = ShowMinimal(r =>
        {
            Overlay.SetCancelCommand(r, new RelayCommand(() => { }));
            Overlay.SetPrimaryCommand(r, new RelayCommand(() => runs++));
        });

        o.Notes.Focus(NavigationMethod.Tab);
        Dispatcher.UIThread.RunJobs();
        o.Window.Press(PhysicalKey.Enter);
        Assert.Equal(0, runs);

        o.Field.Focus(NavigationMethod.Tab);
        Dispatcher.UIThread.RunJobs();
        o.Window.Press(PhysicalKey.Enter);
        Assert.Equal(1, runs);
    }

    [AvaloniaFact]
    public void Initial_focus_goes_to_the_named_control()
    {
        var o = ShowMinimal(r =>
        {
            Overlay.SetCancelCommand(r, new RelayCommand(() => { }));
            Overlay.SetInitialFocus(r, ((StackPanel)r.Child!).Children[1]);
        });

        o.Window.AssertFocused(o.Notes);
    }

    // A view that places focus itself (OperatorPrompt, by stage) keeps that placement.
    [AvaloniaFact]
    public void Initial_focus_leaves_focus_alone_when_the_view_already_put_it_inside()
    {
        var field = new TextBox { Name = "Field" };
        var notes = new TextBox { Name = "Notes" };
        var root = new Border { Child = new StackPanel { Children = { field, notes } } };
        Overlay.SetCancelCommand(root, new RelayCommand(() => { }));
        root.AttachedToVisualTree += (_, _) => Dispatcher.UIThread.Post(() => notes.Focus());
        var window = new Window { Content = root, Width = 800, Height = 600 };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        window.AssertFocused(notes);
    }

    [AvaloniaFact]
    public void Closing_an_overlay_does_not_pull_focus_out_of_the_one_that_replaced_it()
    {
        var o = ShowMinimal(r => Overlay.SetCancelCommand(r, new RelayCommand(() => { })));
        o.Window.AssertFocusWithin(o.Root);

        // Closing one overlay posts focus back to its opener; the overlay that replaces it in the
        // same breath (CloseIncident's confirm chaining into PdfExport) must still end up focused.
        var next = new Border { Child = new TextBox { Name = "Next" } };
        Overlay.SetCancelCommand(next, new RelayCommand(() => { }));
        o.Host.Children.Remove(o.Root);
        o.Host.Children.Add(next);
        Dispatcher.UIThread.RunJobs();

        o.Window.AssertFocusWithin(next);
    }
}
