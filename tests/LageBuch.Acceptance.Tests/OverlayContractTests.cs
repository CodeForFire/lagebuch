using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CommunityToolkit.Mvvm.ComponentModel;
using LageBuch.App.Shared.Views;
using LageBuch.AppLogic.Services;
using LageBuch.AppLogic.ViewModels;
using LageBuch.Domain;
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
// Overlay_scenarios_cover_every_Pending_overlay until it has a scenario here. Inline panels drawn
// by their host's own XAML (CoMessprotokoll's removal panels) are #538's to decide.
//
// What is broken today is listed in KnownFailures, which ratchets: a listed clause must still fail,
// so the fix in #538 turns this red until its entry is deleted.
public class OverlayContractTests
{
    // Measured on main before #538. Each entry is a promise that the clause still fails; delete it
    // with the fix.
    private static readonly Dictionary<(string Scenario, OverlayClause Clause), string> KnownFailures = new()
    {
        [("Workspace.Confirm", OverlayClause.FocusInside)] = "#538: ConfirmDialogView focuses CancelButton synchronously on attach, before layout, and the call is dropped",
        [("MasterDataEditor.Confirm", OverlayClause.FocusInside)] = "#538: ConfirmDialogView focuses CancelButton synchronously on attach, before layout, and the call is dropped",
        [("Workspace.TaskDialog", OverlayClause.FocusInside)] = "#538: the view sets no initial focus",
        [("Workspace.PdfExportOptions", OverlayClause.FocusInside)] = "#538: the view sets no initial focus",
        [("Workspace.OperatorPrompt", OverlayClause.TabStaysInside)] = "#538: no overlay keeps Tab inside (no TabNavigation=Cycle)",
        [("Workspace.Confirm", OverlayClause.TabStaysInside)] = "#538: no overlay keeps Tab inside (no TabNavigation=Cycle)",
        [("Workspace.TaskDialog", OverlayClause.TabStaysInside)] = "#538: no overlay keeps Tab inside (no TabNavigation=Cycle)",
        [("Workspace.PdfExportOptions", OverlayClause.TabStaysInside)] = "#538: no overlay keeps Tab inside (no TabNavigation=Cycle)",
        [("Workspace.IncidentDataDialog", OverlayClause.TabStaysInside)] = "#538: no overlay keeps Tab inside (no TabNavigation=Cycle)",
        [("Main.OperatorPrompt", OverlayClause.TabStaysInside)] = "#538: no overlay keeps Tab inside (no TabNavigation=Cycle)",
        [("Main.About", OverlayClause.TabStaysInside)] = "#538: no overlay keeps Tab inside (no TabNavigation=Cycle)",
        [("MasterDataEditor.Confirm", OverlayClause.TabStaysInside)] = "#538: no overlay keeps Tab inside (no TabNavigation=Cycle)",
        [("Workspace.TaskDialog", OverlayClause.EscCancels)] = "#538: TaskDialogView has no Esc handler",
        [("Workspace.OperatorPrompt", OverlayClause.FocusReturns)] = "#538: nothing hands focus back to the opener when an overlay closes",
        [("Workspace.Confirm", OverlayClause.FocusReturns)] = "#538: nothing hands focus back to the opener when an overlay closes",
        [("Workspace.TaskDialog", OverlayClause.FocusReturns)] = "#538: nothing hands focus back to the opener when an overlay closes",
        [("Workspace.PdfExportOptions", OverlayClause.FocusReturns)] = "#538: nothing hands focus back to the opener when an overlay closes",
        [("Workspace.IncidentDataDialog", OverlayClause.FocusReturns)] = "#538: nothing hands focus back to the opener when an overlay closes",
        [("Main.OperatorPrompt", OverlayClause.FocusReturns)] = "#538: nothing hands focus back to the opener when an overlay closes",
        [("Main.About", OverlayClause.FocusReturns)] = "#538: nothing hands focus back to the opener when an overlay closes",
        [("MasterDataEditor.Confirm", OverlayClause.FocusReturns)] = "#538: nothing hands focus back to the opener when an overlay closes",
    };

    private static readonly OverlayScenario[] Scenarios =
    [
        new("Workspace.OperatorPrompt", typeof(IncidentWorkspaceViewModel), typeof(OperatorPromptViewModel), OpenWorkspaceOperatorPrompt),
        new("Workspace.Confirm", typeof(IncidentWorkspaceViewModel), typeof(ConfirmDialogViewModel), OpenWorkspaceConfirm),
        new("Workspace.TaskDialog", typeof(IncidentWorkspaceViewModel), typeof(TaskDialogViewModel), OpenWorkspaceTaskDialog),
        new("Workspace.PdfExportOptions", typeof(IncidentWorkspaceViewModel), typeof(PdfExportOptionsViewModel), OpenWorkspacePdfExportOptions),
        new("Workspace.IncidentDataDialog", typeof(IncidentWorkspaceViewModel), typeof(IncidentDataDialogViewModel), OpenWorkspaceIncidentDataDialog),
        new("Main.OperatorPrompt", typeof(MainWindowViewModel), typeof(OperatorPromptViewModel), OpenMainOperatorPrompt),
        new("Main.About", typeof(MainWindowViewModel), typeof(AboutViewModel), OpenMainAbout),
        new("MasterDataEditor.Confirm", typeof(MasterDataEditorViewModel), typeof(ConfirmDialogViewModel), OpenMasterDataEditorConfirm),
    ];

    public static TheoryData<string, string> Cases()
    {
        var data = new TheoryData<string, string>();
        foreach (var scenario in Scenarios)
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
        var ids = Scenarios.Select(s => s.Id).ToHashSet(StringComparer.Ordinal);
        Assert.All(KnownFailures.Keys, k => Assert.Contains(k.Scenario, ids));
    }

    [AvaloniaTheory]
    [MemberData(nameof(Cases))]
    public void Overlay_keeps_the_keyboard_contract(string scenarioId, string clauseName)
    {
        var clause = Enum.Parse<OverlayClause>(clauseName);
        var scenario = Scenarios.Single(s => s.Id == scenarioId);
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
                message = $"after closing, focus is on {window.DescribeFocus()}, not on the opener {KeyboardInput.Describe(opened.Opener)}";
                return opened.IsCancelled() && window.IsFocused(opened.Opener);

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

    private static (Window Window, IncidentWorkspaceViewModel Vm) ShowWorkspace()
    {
        var session = TestSession.StartNew(
            new FakeStore(),
            new FixedClock(),
            new SessionOperator(AnonymizedExampleData.OperatorSurname, "FFB 12/1"),
            "/x.fwincident",
            Array.Empty<(string, bool)>(),
            Array.Empty<(string, bool)>());
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

    // IsCancelled: closed, and the action it asks about did not happen.
    private sealed record Opened(Window Window, Control Opener, Func<Control?> Overlay, Func<bool> IsCancelled, Action Cancel);

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
