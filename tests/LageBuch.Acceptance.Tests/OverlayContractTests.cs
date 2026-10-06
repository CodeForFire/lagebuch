using System.Reflection;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CommunityToolkit.Mvvm.ComponentModel;
using LageBuch.App.Shared.Views;
using LageBuch.AppLogic;
using LageBuch.AppLogic.Services;
using LageBuch.AppLogic.ViewModels;
using LageBuch.Domain;
using LageBuch.Domain.CoMeasurement;
using LageBuch.Persistence.MasterData;

namespace LageBuch.Acceptance.Tests;

internal enum OverlayClause
{
    FocusInside,
    TabStaysInside,
    EscCancels,
    FocusReturns,
}

// #537, keyboard contract 1 of #545: an overlay takes focus once shown, keeps Tab inside, closes on
// Esc and hands focus back to the control that opened it. Every overlay is shown the same way, as
// a view model in a Pending* property of its host that ViewLocator turns into its view, so the
// scenarios are checked against that set: a new overlay fails
// Overlay_scenarios_cover_every_Pending_overlay until it has a scenario here.
//
// Inline panels, drawn by their module's own XAML and shown by IsVisible, keep the same four
// clauses (#538) and are listed apart in InlinePanelScenarios. They are not modal, so focus may
// still leave one for the page beside it by a click; that is not a clause. The one exception is
// CoMessprotokoll's Wohnungen-entfernen panel, which must not take focus (#419) and is covered in
// OverlayKeyboardTests instead.
//
// What is broken today is listed in KnownFailures, which ratchets: a listed clause must still fail,
// so the fix in #538 turns this red until its entry is deleted.
public class OverlayContractTests
{
    // Clauses an overlay still fails, each with its reason and the issue that fixes it. A listed
    // clause must keep failing; delete the entry with the fix. Empty since #538.
    private static readonly Dictionary<(string Scenario, OverlayClause Clause), string> KnownFailures = new();

    private static readonly OverlayScenario[] Scenarios =
    [
        new("Workspace.OperatorPrompt", typeof(IncidentWorkspaceViewModel), typeof(OperatorPromptViewModel), OpenWorkspaceOperatorPrompt),
        new("Workspace.Confirm", typeof(IncidentWorkspaceViewModel), typeof(ConfirmDialogViewModel), OpenWorkspaceConfirm),
        new("Workspace.TaskDialog", typeof(IncidentWorkspaceViewModel), typeof(TaskDialogViewModel), OpenWorkspaceTaskDialog),
        new("Workspace.PdfExportOptions", typeof(IncidentWorkspaceViewModel), typeof(PdfExportOptionsViewModel), OpenWorkspacePdfExportOptions),
        new("Workspace.IncidentDataDialog", typeof(IncidentWorkspaceViewModel), typeof(IncidentDataDialogViewModel), OpenWorkspaceIncidentDataDialog),
        new("Main.OperatorPrompt", typeof(MainWindowViewModel), typeof(OperatorPromptViewModel), OpenMainOperatorPrompt),
        new("Main.About", typeof(MainWindowViewModel), typeof(AboutViewModel), OpenMainAbout),
        new("Main.ShortcutOverview", typeof(MainWindowViewModel), typeof(ShortcutOverviewViewModel), OpenMainShortcutOverview),
        new("MasterDataEditor.Confirm", typeof(MasterDataEditorViewModel), typeof(ConfirmDialogViewModel), OpenMasterDataEditorConfirm),
    ];

    private static readonly OverlayScenario[] InlinePanelScenarios =
    [
        new("Etb.EditPanel", typeof(EtbViewModel), typeof(EtbEntryRow), OpenEtbEditPanel),
        new("Roles.TransferPanel", typeof(RolesViewModel), typeof(RoleAssignmentRow), OpenRolesTransferPanel),
        new("Co.AddBuildingPanel", typeof(CoMessprotokollViewModel), typeof(Building), OpenCoAddBuildingPanel),
        new("Co.RemoveBuildingPanel", typeof(CoMessprotokollViewModel), typeof(Building), OpenCoRemoveBuildingPanel),
        new("Co.FloorRemovalPanel", typeof(CoMessprotokollViewModel), typeof(FloorRemovalViewModel), OpenCoFloorRemovalPanel),
        new("Co.DwellingEditor", typeof(CoMessprotokollViewModel), typeof(DwellingEditorViewModel), OpenCoDwellingEditor),
    ];

    private static IEnumerable<OverlayScenario> AllScenarios => Scenarios.Concat(InlinePanelScenarios);

    public static TheoryData<string, string> Cases()
    {
        var data = new TheoryData<string, string>();
        foreach (var scenario in AllScenarios)
        {
            foreach (var clause in Enum.GetValues<OverlayClause>())
            {
                data.Add(scenario.Id, clause.ToString());
            }
        }

        return data;
    }

    [AvaloniaFact]
    public void Overlay_scenarios_cover_every_Pending_overlay()
    {
        var pending = typeof(IncidentWorkspaceViewModel).Assembly.GetTypes()
            .SelectMany(t => t.GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
            .Where(p => p.Name.StartsWith("Pending", StringComparison.Ordinal) && HasLocatedView(p.PropertyType))
            .Select(p => $"{p.DeclaringType!.Name}.{p.Name}: {p.PropertyType.Name}")
            .Order(StringComparer.Ordinal)
            .ToArray();
        var covered = Scenarios
            .Select(s => s.Host.GetProperties().Single(p => p.PropertyType == s.Overlay && p.Name.StartsWith("Pending", StringComparison.Ordinal)))
            .Select(p => $"{p.DeclaringType!.Name}.{p.Name}: {p.PropertyType.Name}")
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(pending, covered);
    }

    // Mirrors ViewLocator: FooViewModel is shown as LageBuch.App.Shared.Views.FooView.
    private static bool HasLocatedView(Type viewModel) =>
        typeof(ObservableObject).IsAssignableFrom(viewModel)
        && typeof(AboutView).Assembly.GetType(
            $"LageBuch.App.Shared.Views.{viewModel.Name.Replace("ViewModel", "View", StringComparison.Ordinal)}") is not null;

    [AvaloniaFact]
    public void Known_failures_name_existing_scenarios()
    {
        var ids = AllScenarios.Select(s => s.Id).ToHashSet(StringComparer.Ordinal);
        Assert.All(KnownFailures.Keys, k => Assert.Contains(k.Scenario, ids));
    }

    [AvaloniaTheory]
    [MemberData(nameof(Cases))]
    public void Overlay_keeps_the_keyboard_contract(string scenarioId, string clauseName)
    {
        var clause = Enum.Parse<OverlayClause>(clauseName);
        var scenario = AllScenarios.Single(s => s.Id == scenarioId);
        var opened = scenario.Open();
        var ok = Check(opened, clause, out var message);

        if (KnownFailures.TryGetValue((scenarioId, clause), out var why))
        {
            Assert.False(ok, $"{scenarioId}/{clause} passes now -- delete its KnownFailures entry ({why}).");
        }
        else
        {
            Assert.True(ok, $"{scenarioId}/{clause}: {message}");
        }
    }

    private static bool Check(Opened opened, OverlayClause clause, out string message)
    {
        var window = opened.Window;
        var overlay = opened.Overlay();
        message = string.Empty;
        if (overlay is null)
        {
            message = "the overlay is not on screen after opening it";
            return false;
        }

        if (clause == OverlayClause.FocusInside)
        {
            message = $"focus is on {window.DescribeFocus()} after opening";
            return window.IsFocusWithin(overlay);
        }

        // The other clauses start from focus inside, so each is measured on its own.
        if (!window.IsFocusWithin(overlay) && !FocusFirstStop(overlay))
        {
            message = "nothing inside the overlay takes focus";
            return false;
        }

        switch (clause)
        {
            case OverlayClause.TabStaysInside:
                var presses = TabStops(overlay).Count() + 2;
                for (var i = 0; i < presses * 2; i++)
                {
                    if (i < presses)
                    {
                        window.Tab();
                    }
                    else
                    {
                        window.ShiftTab();
                    }

                    if (!window.IsFocusWithin(overlay))
                    {
                        message = $"{(i < presses ? "Tab" : "Shift+Tab")} #{(i % presses) + 1} moved focus out to {window.DescribeFocus()}";
                        return false;
                    }
                }

                return true;

            case OverlayClause.EscCancels:
                var pressedOn = window.DescribeFocus();

                // An open suggestion list takes the first Esc (contract 3); the next one is the
                // overlay's. Suggestion lists open on focus, so this is the common case.
                if (window.FocusedElement() is StyledElement { TemplatedParent: AutoCompleteBox { IsDropDownOpen: true } suggestions })
                {
                    window.Press(PhysicalKey.Escape);
                    if (suggestions.IsDropDownOpen)
                    {
                        message = $"Esc on {pressedOn} did not close its suggestion list";
                        return false;
                    }
                }

                window.Press(PhysicalKey.Escape);
                message = $"Esc on {pressedOn} did not close the overlay through its cancel path";
                return opened.IsCancelled();

            case OverlayClause.FocusReturns:
                opened.Cancel();
                Dispatcher.UIThread.RunJobs();
                var returnsTo = opened.ReturnsTo ?? opened.Opener;
                message = $"after closing, focus is on {window.DescribeFocus()}, not on {KeyboardInput.Describe(returnsTo)}";
                return opened.IsCancelled() && window.IsFocused(returnsTo);

            default:
                throw new ArgumentOutOfRangeException(nameof(clause), clause, null);
        }
    }

    private static IEnumerable<InputElement> TabStops(Control overlay) =>
        overlay.GetVisualDescendants().OfType<InputElement>()
            .Where(e => e.Focusable && e.IsEffectivelyVisible && e.IsEffectivelyEnabled && KeyboardNavigation.GetIsTabStop(e));

    private static bool FocusFirstStop(Control overlay)
    {
        var first = TabStops(overlay).FirstOrDefault();
        if (first is null)
        {
            return false;
        }

        first.Focus(NavigationMethod.Tab);
        Dispatcher.UIThread.RunJobs();
        return true;
    }

    // Opens the overlay the way a user does: focus on the opener, then its command, so focus is on
    // the opener at the moment the overlay appears.
    private static void Activate(Control opener)
    {
        opener.Focus(NavigationMethod.Tab);
        Dispatcher.UIThread.RunJobs();
        var button = Assert.IsType<Button>(opener, exactMatch: false);
        Assert.NotNull(button.Command);
        Assert.True(button.Command.CanExecute(button.CommandParameter), $"{KeyboardInput.Describe(button)} cannot run");
        button.Command.Execute(button.CommandParameter);
        Dispatcher.UIThread.RunJobs();
    }

    private static T Named<T>(Visual root, string name)
        where T : Control =>
        root.GetVisualDescendants().OfType<T>().Single(c => c.Name == name);

    private static Window Show(Control content, double width = 1920, double height = 1032)
    {
        var window = new Window { Content = content, Width = width, Height = height };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        return window;
    }

    private static Func<Control?> ViewOf<TView>(Window window)
        where TView : Control =>
        () => window.GetVisualDescendants().OfType<TView>().SingleOrDefault(v => v.IsEffectivelyVisible);

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
            new NoopIncidentHostController(),
            new ExportablePdf());
        return (Show(new IncidentWorkspaceView { DataContext = vm }), vm);
    }

    private static Opened OpenWorkspaceOperatorPrompt()
    {
        var (window, vm) = ShowWorkspace();
        var opener = Named<Button>(window, "ChangeOperatorButton");
        Activate(opener);
        return new(window, opener, ViewOf<OperatorPromptView>(window), () => vm.PendingPrompt is null, () => vm.PendingPrompt?.CancelCommand.Execute(null));
    }

    private static Opened OpenWorkspaceConfirm()
    {
        var (window, vm) = ShowWorkspace();
        var opener = Named<Button>(window, "CloseButton");
        Activate(opener);
        return new(window, opener, ViewOf<ConfirmDialogView>(window), () => vm.PendingConfirm is null && !vm.IsReadOnly, () => vm.PendingConfirm?.CancelCommand.Execute(null));
    }

    private static Opened OpenWorkspaceTaskDialog()
    {
        var (window, vm) = ShowWorkspace();
        WorkspaceRenderHelper.SelectTab(window, "ETB");
        vm.Etb.NewText = "Lagemeldung übermittelt";
        var opener = Named<Button>(window, "EtbAddAndTaskButton");
        Activate(opener);
        return new(window, opener, ViewOf<TaskDialogView>(window), () => vm.PendingTaskDialog is null && vm.Tasks.Rows.Count == 0, () => vm.PendingTaskDialog?.CancelCommand.Execute(null));
    }

    private static Opened OpenWorkspacePdfExportOptions()
    {
        var (window, vm) = ShowWorkspace();
        var opener = Named<Button>(window, "ExportButton");
        Activate(opener);
        return new(window, opener, ViewOf<PdfExportOptionsView>(window), () => vm.PendingPdfExportOptions is null, () => vm.PendingPdfExportOptions?.CancelCommand.Execute(null));
    }

    private static Opened OpenWorkspaceIncidentDataDialog()
    {
        var (window, vm) = ShowWorkspace();
        var opener = Named<Button>(window, "AddIncidentDataButton");
        Activate(opener);
        return new(window, opener, ViewOf<IncidentDataDialogView>(window), () => vm.PendingIncidentDataDialog is null, () => vm.PendingIncidentDataDialog?.CancelCommand.Execute(null));
    }

    // A row's own button, the way a keyboard user reaches it: Tab into the grid, then to the row.
    private static Button RowButton(Window window, string grid, Func<Button, bool> match) =>
        Named<DataGrid>(window, grid).GetVisualDescendants().OfType<Button>()
            .First(b => b.IsEffectivelyVisible && match(b));

    private static Opened OpenEtbEditPanel()
    {
        var (window, vm) = ShowWorkspace();
        WorkspaceRenderHelper.SelectTab(window, "ETB");
        vm.Etb.NewText = "Lagemeldung übermittelt";
        vm.Etb.AddEntryCommand.Execute(null);
        Dispatcher.UIThread.RunJobs();
        var opener = RowButton(window, "EtbGrid", b => AutomationProperties.GetName(b) == "Bearbeiten");
        Activate(opener);

        // Cancel leaves the grid as it was, so the row's own button is still there to go back to.
        return new(window, opener, PanelOf(window, "EditPanel"), () => !vm.Etb.IsEditing && vm.Etb.Entries.Any(e => e.Text == "Lagemeldung übermittelt"), () => vm.Etb.CancelEditCommand.Execute(null));
    }

    private static Opened OpenRolesTransferPanel()
    {
        var (window, vm) = ShowWorkspace(s => s.AssignRole("ZF", AnonymizedExampleData.OperatorSurname));
        WorkspaceRenderHelper.SelectTab(window, "FUNKTIONEN");
        var assignments = vm.Roles.Roles.Count;
        var opener = RowButton(window, "RolesGrid", b => b.Name == "TransferRowButton");
        Activate(opener);
        return new(window, opener, PanelOf(window, "TransferPanel"), () => !vm.Roles.IsTransferring && vm.Roles.Roles.Count == assignments, () => vm.Roles.CancelTransferCommand.Execute(null));
    }

    private static Opened OpenCoAddBuildingPanel()
    {
        var (window, vm) = ShowWorkspace();
        WorkspaceRenderHelper.SelectTab(window, "CO-MESSUNG");
        var opener = Named<Button>(window, "AddBuildingButton");
        Activate(opener);
        var co = vm.CoMessprotokoll;
        return new(window, opener, PanelOf(window, "AddBuildingPanel"), () => !co.IsAddBuildingDialogOpen && co.BuildingOptions.Count == 0, () => co.CancelAddBuildingCommand.Execute(null));
    }

    private static Opened OpenCoRemoveBuildingPanel()
    {
        var (window, vm) = ShowWorkspace(s => s.AddCoBuilding("Mehrfamilienhaus A", 2, 2));
        WorkspaceRenderHelper.SelectTab(window, "CO-MESSUNG");
        var opener = Named<Button>(window, "RemoveBuildingButton");
        Activate(opener);
        var co = vm.CoMessprotokoll;
        return new(window, opener, PanelOf(window, "RemoveBuildingPanel"), () => !co.IsRemoveBuildingConfirmOpen && co.BuildingOptions.Count == 1, () => co.CancelRemoveBuildingCommand.Execute(null));
    }

    private static Opened OpenCoFloorRemovalPanel()
    {
        // The panel asks only when the floor carries something: a reading on the top floor.
        var (window, vm) = ShowWorkspace(s =>
        {
            s.AddCoBuilding("Mehrfamilienhaus A", 2, 2);
            var building = s.Incident.Buildings[0];
            s.RecordCoValue(building.Id, building.FloorCount, 1, 45);
        });
        WorkspaceRenderHelper.SelectTab(window, "CO-MESSUNG");
        var co = vm.CoMessprotokoll;
        co.IsStructureMode = true;
        Dispatcher.UIThread.RunJobs();
        var floors = co.MatrixRows.Count;
        var opener = Named<Button>(window, "RemoveObergeschossButton");
        Activate(opener);
        return new(window, opener, PanelOf(window, "FloorRemovalPanel"), () => !co.IsFloorRemovalOpen && co.MatrixRows.Count == floors, () => co.CancelFloorRemovalCommand.Execute(null));
    }

    private static Opened OpenCoDwellingEditor()
    {
        var (window, vm) = ShowWorkspace(s => s.AddCoBuilding("Mehrfamilienhaus A", 2, 2));
        WorkspaceRenderHelper.SelectTab(window, "CO-MESSUNG");
        var co = vm.CoMessprotokoll;
        var opener = window.GetVisualDescendants().OfType<Button>().First(b => b.DataContext is DwellingCellViewModel && b.IsEffectivelyVisible);
        Activate(opener);

        // ABBRECHEN rebuilds the matrix, so the tile that opened the editor is gone; focus falls
        // back to the house picker. Landing on the rebuilt tile is #542's (rebuilds keep focus).
        return new(window, opener, PanelOf(window, "DwellingEditor"), () => !co.IsEditorOpen, () => co.CloseEditorCommand.Execute(null), Named<ComboBox>(window, "BuildingBox"));
    }

    private static Func<Control?> PanelOf(Window window, string name) =>
        () => window.GetVisualDescendants().OfType<Border>().SingleOrDefault(b => b.Name == name && b.IsEffectivelyVisible);

    private static (Window Window, MainWindowViewModel Vm) ShowMain()
    {
        var dialogs = new FakeDialogs();
        var masterData = new StaticMasterData(WorkspaceRenderHelper.MasterData());
        var home = new HomeViewModel(
            new FakeStore(),
            masterData,
            new EmptyRecent(),
            dialogs,
            new FixedClock(),
            new NoopTicker(),
            new NoopAlarmService(),
            new NoopIncidentHostController(),
            "0.1.0");
        var editor = new MasterDataEditorViewModel(masterData, dialogs, new NoFiles());
        var vm = new MainWindowViewModel(home, editor, dialogs, "0.1.0");

        // AttachViewModel, as MainWindow does: it wires the prompt's cancel to the view model.
        var view = new MainView();
        view.AttachViewModel(vm);
        return (Show(view, 1280, 800), vm);
    }

    private static Opened OpenMainOperatorPrompt()
    {
        var (window, vm) = ShowMain();
        var opener = Named<Button>(window, "NewIncidentButton");
        Activate(opener);
        return new(window, opener, ViewOf<OperatorPromptView>(window), () => vm.PendingPrompt is null, () => vm.PendingPrompt?.CancelCommand.Execute(null));
    }

    private static Opened OpenMainAbout()
    {
        var (window, vm) = ShowMain();
        var opener = Named<Button>(window, "AboutButton");
        Activate(opener);
        return new(window, opener, ViewOf<AboutView>(window), () => vm.PendingAbout is null, () => vm.PendingAbout?.CloseCommand.Execute(null));
    }

    private static Opened OpenMainShortcutOverview()
    {
        var (window, vm) = ShowMain();
        var opener = Named<Button>(window, "ShortcutsButton");
        Activate(opener);
        return new(window, opener, ViewOf<ShortcutOverviewView>(window), () => vm.PendingShortcutOverview is null, () => vm.PendingShortcutOverview?.CloseCommand.Execute(null));
    }

    private static Opened OpenMasterDataEditorConfirm()
    {
        var vm = new MasterDataEditorViewModel(new StaticMasterData(WorkspaceRenderHelper.MasterData()), new FakeDialogs(), new NoFiles());
        vm.SelectedSection = vm.Checklists[0];
        var checklists = vm.Checklists.Count;
        var window = Show(new MasterDataEditorView { DataContext = vm }, 1080, 680);
        var opener = Named<Button>(window, "DeleteChecklistButton");
        Activate(opener);
        return new(window, opener, ViewOf<ConfirmDialogView>(window), () => vm.PendingConfirm is null && vm.Checklists.Count == checklists, () => vm.PendingConfirm?.CancelCommand.Execute(null));
    }

    private sealed record OverlayScenario(string Id, Type Host, Type Overlay, Func<Opened> Open);

    // IsCancelled: closed, and the action it asks about did not happen. ReturnsTo: where focus goes
    // on close when that is not the opener, because closing rebuilt it.
    private sealed record Opened(Window Window, Control Opener, Func<Control?> Overlay, Func<bool> IsCancelled, Action Cancel, Control? ReturnsTo = null);

    // Lets EXPORTIEREN run: the default exporter is the Android one, which hides the button.
    private sealed class ExportablePdf : IIncidentPdfExporter
    {
        public bool CanExport => true;

        public byte[] Generate(Incident incident, DateTimeOffset asOf, IReadOnlyDictionary<Guid, byte[]> fileBytes, IReadOnlyDictionary<Guid, string> pdfAttachmentPaths, IncidentPdfSections sections = IncidentPdfSections.All) =>
            Array.Empty<byte>();
    }

    private sealed class StaticMasterData(MasterDataSet set) : IMasterDataProvider
    {
        public MasterDataSet Get() => set;

        public void Save(MasterDataSet s)
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

    private sealed class EmptyRecent : IRecentFilesStore
    {
        public IReadOnlyList<string> GetRecent() => Array.Empty<string>();

        public void Add(string path)
        {
        }

        public void Remove(string path)
        {
        }
    }
}
