using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using LageBuch.App.Shared.Views;
using LageBuch.AppLogic.Services;
using LageBuch.AppLogic.ViewModels;
using LageBuch.Persistence.MasterData;

namespace LageBuch.Acceptance.Tests;

// #695, field report on 0.9.0: "taborder is sometimes unlogic (e.g. ETB on the action buttons on
// the right)". Every module docks its entry form to the bottom, and a DockPanel needs a
// bottom-docked child declared before the list that fills the space above it. Tab followed that
// declaration order: from the header into the form, then from the form's last button back up to
// the row buttons at the list's right edge. The same held one level up, where the action footer
// came before the module. Tab now follows the screen: header, rail, the module top to bottom,
// footer.
public class ReadingOrderTabTests
{
    private static (Window Window, IncidentWorkspaceViewModel Vm) ShowWorkspace()
    {
        var vm = WorkspaceRenderHelper.BuildEditableWorkspaceWithAllBars(withOverdueTask: true);
        vm.Etb.NewFrom = "Florian Musterstadt 1/10";
        vm.Etb.NewText = "Lage erkundet";
        vm.Etb.AddEntryCommand.Execute(null);
        var window = new Window { Content = new IncidentWorkspaceView { DataContext = vm }, Width = 1600, Height = 1000 };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        return (window, vm);
    }

    private static T Named<T>(Visual root, string name)
        where T : Control =>
        root.GetVisualDescendants().OfType<T>().First(c => c.Name == name);

    private static void FocusByTab(InputElement element)
    {
        element.Focus(NavigationMethod.Tab);
        Dispatcher.UIThread.RunJobs();
    }

    [AvaloniaFact]
    public void Etb_tabs_from_the_header_into_the_list_and_from_the_form_on_to_the_footer()
    {
        var (window, _) = ShowWorkspace();
        WorkspaceRenderHelper.SelectTab(window, "ETB");
        var content = WorkspaceRenderHelper.SelectedTabContent(window);

        // The list sits between the header and the form, so it comes between them.
        FocusByTab(Named<CheckBox>(content, "HideSystemCheckBox"));
        window.Tab();
        window.AssertFocusWithin(Named<DataGrid>(content, "EtbGrid"));

        // The reported jump: from the form's last button back up to the row buttons on the right.
        FocusByTab(Named<Button>(content, "EtbAddAndTaskButton"));
        window.Tab();
        window.AssertFocused(Named<Button>(window, "CloseButton"));
    }

    // Seen testing #695: Tab on from the grid to the row's edit button left the ring on the row's
    // ZEIT cell, so two places looked focused. DataGridCell's :focus means "the grid holds focus
    // anywhere and this cell is current", and a row button counts as anywhere.
    [AvaloniaFact]
    public void The_cell_ring_shows_while_the_grid_has_focus_and_leaves_with_it()
    {
        var (window, _) = ShowWorkspace();
        WorkspaceRenderHelper.SelectTab(window, "ETB");
        var content = WorkspaceRenderHelper.SelectedTabContent(window);
        var grid = Named<DataGrid>(content, "EtbGrid");
        bool AnyCellRing() => grid.GetVisualDescendants().OfType<Grid>().Any(g => g.Name == "FocusVisual" && g.IsEffectivelyVisible);

        FocusByTab(Named<CheckBox>(content, "HideSystemCheckBox"));
        window.Tab();
        window.AssertFocused(grid);
        Assert.True(AnyCellRing());

        window.Tab();
        var editButton = Assert.IsType<Button>(window.FocusedElement());
        Assert.Equal("Bearbeiten", AutomationProperties.GetName(editButton));
        Assert.False(AnyCellRing());
    }

    // The entry form is the last thing in every module, so leaving its last control leaves the
    // module for the footer, not for the list above it or the header bars.
    [AvaloniaTheory]
    [InlineData("AUFGABEN", "TaskAddButton")]
    [InlineData("FUNKTIONEN", "AddRoleButton")]
    [InlineData("KRÄFTE", "AddForceButton")]
    [InlineData("BETEILIGTE", "AddInvolvedPartyButton")]
    [InlineData("ATEMSCHUTZ", "AddTruppButton")]
    [InlineData("CO-MESSUNG", "AddBuildingButton")]
    [InlineData("DATEIEN", "AddFileButton")]
    public void Tab_from_the_entry_form_s_last_control_goes_on_to_the_footer(string module, string lastControl)
    {
        var (window, _) = ShowWorkspace();
        WorkspaceRenderHelper.SelectTab(window, module);
        var content = WorkspaceRenderHelper.SelectedTabContent(window);

        FocusByTab(Named<Button>(content, lastControl));
        window.Tab();

        window.AssertFocused(Named<Button>(window, "CloseButton"));
    }

    [AvaloniaFact]
    public void Tab_from_the_header_bars_enters_the_rail_and_then_the_module_s_first_control()
    {
        var (window, _) = ShowWorkspace();
        var tabs = WorkspaceRenderHelper.SelectTab(window, "ETB");
        var content = WorkspaceRenderHelper.SelectedTabContent(window);
        var railTab = (TabItem)tabs.ContainerFromIndex(tabs.SelectedIndex)!;

        // Focus has been inside the module before: the rail used to keep that control as the place
        // Tab enters it, and sent Tab from the header straight back to it.
        FocusByTab(Named<Button>(content, "EtbAddAndTaskButton"));
        var lastHeaderControl = Named<Button>(window, "TaskDueDoneButton");
        FocusByTab(lastHeaderControl);
        window.AssertFocused(lastHeaderControl);

        window.Tab();
        window.AssertFocused(railTab);

        window.Tab();
        window.AssertFocused(Named<CheckBox>(content, "HideSystemCheckBox"));
    }

    [AvaloniaFact]
    public void Shift_tab_from_the_module_s_first_control_goes_back_to_the_rail()
    {
        var (window, _) = ShowWorkspace();
        var tabs = WorkspaceRenderHelper.SelectTab(window, "ETB");
        var content = WorkspaceRenderHelper.SelectedTabContent(window);
        var railTab = (TabItem)tabs.ContainerFromIndex(tabs.SelectedIndex)!;

        // Shift+Tab used to resolve the rail to the very control it started on, and went nowhere.
        FocusByTab(Named<CheckBox>(content, "HideSystemCheckBox"));
        window.ShiftTab();

        window.AssertFocused(railTab);
    }

    [AvaloniaFact]
    public void Kontakte_row_tabs_to_the_number_before_the_mail_button_beside_it()
    {
        var (window, _) = ShowWorkspace();
        WorkspaceRenderHelper.SelectTab(window, "KONTAKTE");
        var content = WorkspaceRenderHelper.SelectedTabContent(window);
        var call = content.GetVisualDescendants().OfType<Button>().First(b => b.Classes.Contains("contact-call"));
        var mail = content.GetVisualDescendants().OfType<Button>().First(b => b.Classes.Contains("contact-mail"));

        FocusByTab(Named<TextBox>(content, "ContactSearchBox"));
        window.Tab();
        window.AssertFocused(call);

        window.Tab();
        window.AssertFocused(mail);
    }

    private sealed class Provider : IMasterDataProvider
    {
        public MasterDataSet Get() => MasterDataSet.Empty with
        {
            UnitStatus = new[] { "Alarmiert" },
        };

        public void Save(MasterDataSet set)
        {
        }
    }

    private sealed class NoFiles : IMasterDataFileService
    {
        public MasterDataImportResult Read(string path) => new(MasterDataSet.Empty, Array.Empty<string>());

        public void Write(string path, MasterDataSet set)
        {
        }
    }

    [AvaloniaFact]
    public void Stammdaten_tab_to_an_add_button_after_the_rows_above_it()
    {
        var vm = new MasterDataEditorViewModel(new Provider(), new FakeDialogs(), new NoFiles());
        vm.SelectedSection = vm.Sections.First(s => s.Title == "Einheiten-Status");
        var view = new MasterDataEditorView { DataContext = vm };
        var window = new Window { Content = view, Width = 1080, Height = 680 };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        var buttons = view.GetVisualDescendants().OfType<Button>().ToList();
        var addRow = buttons.First(b => b.Content is "+ HINZUFÜGEN");
        var removeRow = buttons.First(b => (ToolTip.GetTip(b) as string) == "Entfernen");

        // The rail: the section list, then "+ NEUE CHECKLISTE" under it.
        FocusByTab(Named<ListBox>(view, "CategoryList").GetVisualDescendants().OfType<ListBoxItem>().First(i => i.IsSelected));
        window.Tab();
        window.AssertFocused(Named<Button>(view, "AddChecklistButton"));

        // The section: its rows, then "+ HINZUFÜGEN" under them.
        FocusByTab(removeRow);
        window.Tab();
        window.AssertFocused(addRow);
    }
}
