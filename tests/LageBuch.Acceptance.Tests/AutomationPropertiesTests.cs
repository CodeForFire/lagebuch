using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using LageBuch.App.Shared.Views;
using LageBuch.AppLogic;
using LageBuch.AppLogic.Services;
using LageBuch.AppLogic.ViewModels;
using LageBuch.Domain;
using LageBuch.Persistence.MasterData;

namespace LageBuch.Acceptance.Tests;

// #262 (UX review, "No accessibility support"): zero AutomationProperties existed anywhere in
// src/**/*.axaml before this -- icon-only buttons carried nothing but a ToolTip, invisible to any
// automation/assistive layer. Pins a Name on each icon-only button that was missing one.
public class AutomationPropertiesTests
{
    private sealed class SampleProvider : IMasterDataProvider
    {
        public MasterDataSet Get() => MasterDataSet.Empty with
        {
            Roles = new[] { "EL" },
            ChecklistTemplates = ChecklistTemplate.AufbauAbbau(new[] { new ChecklistTemplateItem("Aufstellort ELW frei?", true) }, null),
            Links = new[] { new Link("Wetterdienst", "https://dwd.de") },
            Personnel = new[] { new Person("Mustermann", "Max", "ZF", "Land 1", null) },
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

    private static string? Name(Control c) => AutomationProperties.GetName(c);

    private static List<Button> IconButtons(Visual root) =>
        root.GetVisualDescendants().OfType<Button>().Where(b => b.Classes.Contains("icon-btn")).ToList();

    [AvaloniaFact]
    public void Master_data_editor_reorder_and_remove_buttons_are_named_for_automation()
    {
        var vm = new MasterDataEditorViewModel(new SampleProvider(), new FakeDialogs(), new NoFiles());
        var view = new MasterDataEditorView { DataContext = vm };
        var window = new Window { Content = view, Width = 1080, Height = 680 };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        void AssertNamed(string sectionTitle, int expectedButtons)
        {
            vm.SelectedSection = vm.Sections.Single(s => s.Title == sectionTitle);
            Dispatcher.UIThread.RunJobs();
            var buttons = IconButtons(view);
            Assert.Equal(expectedButtons, buttons.Count);
            foreach (var button in buttons)
            {
                var tip = ToolTip.GetTip(button) as string;
                Assert.False(
                    string.IsNullOrWhiteSpace(Name(button)),
                    $"{sectionTitle}: icon-btn button with tooltip '{tip}' has no AutomationProperties.Name");
            }
        }

        AssertNamed("Rollen", 3); // Nach oben / Nach unten / Entfernen, one row
        AssertNamed("Aufbau", 3);

        // Navigation rows reorder but cannot be removed: they are derived from the modules and
        // the Checklisten, so only «Neue Checkliste» and the delete button change the set.
        AssertNamed("Navigation", 2 * (NavModules.All.Count + 1));
        AssertNamed("Links", 3);
        AssertNamed("Personal", 1); // only Entfernen

        var vehicles = (VehiclesSection)vm.Sections.Single(s => s.Title == "Fahrzeuge");
        vehicles.AddCommand.Execute(null);
        AssertNamed("Fahrzeuge", 3);
    }

    private static ForcesViewModel BuildForcesVm(out LocalIncidentSession session)
    {
        session = TestSession.StartNew(
            new FakeStore(),
            new FixedClock(),
            new SessionOperator("Müller", "FFB 12/1"),
            "/x.fwincident",
            Array.Empty<(string, bool)>(),
            Array.Empty<(string, bool)>());
        session.AddForceUnit("FFB Wache 1", 9, null, "Alarmiert", null);
        var md = MasterDataSet.Empty with
        {
            Vehicles = new[] { new Vehicle("FFB Wache 1", "FFB 1/40/1", 9) },
            UnitStatus = new[] { "Alarmiert" },
        };
        return new ForcesViewModel(session, new FixedClock(), md, () => { });
    }

    [AvaloniaFact]
    public void Forces_row_action_buttons_are_named_for_automation()
    {
        var vm = BuildForcesVm(out _);
        var view = new ForcesView { DataContext = vm };
        var window = new Window { Content = view, Width = 1200, Height = 600 };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        var grid = view.GetControl<DataGrid>("ForcesGrid");
        var buttons = IconButtons(grid);
        Assert.Equal(2, buttons.Count); // Stärke korrigieren, Einheit entfernen
        foreach (var button in buttons)
        {
            var tip = ToolTip.GetTip(button) as string;
            Assert.False(string.IsNullOrWhiteSpace(Name(button)), $"'{tip}' button has no AutomationProperties.Name");
        }
    }

    [AvaloniaFact]
    public void Forces_clear_vehicle_button_is_named_for_automation()
    {
        var vm = BuildForcesVm(out _);
        vm.SelectedVehicle = new Vehicle("FFB Wache 1", "FFB ELW 1", 4);
        var view = new ForcesView { DataContext = vm };
        var window = new Window { Content = view, Width = 1200, Height = 600 };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        var clearButton = view.GetControl<Button>("ClearVehicleButton");
        Assert.True(clearButton.IsVisible);
        Assert.Equal("Fahrzeugauswahl aufheben", Name(clearButton));
    }

    private static (EtbViewModel Vm, LocalIncidentSession Session) BuildEtbVm()
    {
        var session = TestSession.StartNew(
            new FakeStore(),
            new FixedClock(),
            new SessionOperator("Müller", "FFB 12/1"),
            "/x.fwincident",
            Array.Empty<(string, bool)>(),
            Array.Empty<(string, bool)>());
        var vm = new EtbViewModel(
            session,
            new FixedClock(),
            MasterDataSet.Empty,
            () => { },
            createTaskFromEntry: _ => { });
        return (vm, session);
    }

    [AvaloniaFact]
    public void Etb_row_action_buttons_are_named_for_automation()
    {
        var (vm, _) = BuildEtbVm();
        vm.NewText = "Lagemeldung";
        vm.AddEntryCommand.Execute(null);

        // WasEdited only becomes true once a row has been through one edit, same flow as an
        // operator correcting an entry.
        var row = vm.Entries[0];
        row.BeginEditCommand.Execute(null);
        vm.EditText = "Lagemeldung (korrigiert)";
        vm.SaveEditCommand.Execute(null);

        var view = new EtbView { DataContext = vm };
        var window = new Window { Content = view, Width = 1200, Height = 600 };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        var buttons = IconButtons(view);
        Assert.Equal(3, buttons.Count); // Bearbeiten, Verlauf (bearbeitet), Aufgabe erstellen
        foreach (var button in buttons)
        {
            var tip = ToolTip.GetTip(button) as string;
            Assert.False(string.IsNullOrWhiteSpace(Name(button)), $"'{tip}' button has no AutomationProperties.Name");
        }
    }

    private sealed class FakeHost : IIncidentHostController
    {
        public bool CanHost => true;

        public bool IsHosting { get; private set; }

        public string? ShareHint => "Erreichbar unter https://192.168.0.5:5859";

        public string? SharePin => IsHosting ? "1234" : null;

        public Task StartAsync(LocalIncidentSession session, MasterDataSet masterData, CancellationToken cancellationToken = default)
        {
            IsHosting = true;
            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken cancellationToken = default)
        {
            IsHosting = false;
            return Task.CompletedTask;
        }
    }

    [AvaloniaFact]
    public async Task Einsatzdaten_pencil_and_share_toggle_buttons_are_named_for_automation()
    {
        var session = TestSession.StartNew(
            new FakeStore(),
            new FixedClock(),
            new SessionOperator("Müller", "FFB 12/1"),
            "/x.fwincident",
            Array.Empty<(string, bool)>(),
            Array.Empty<(string, bool)>(),
            keyword: "B3P");
        var vm = new IncidentWorkspaceViewModel(
            session,
            new FixedClock(),
            new NoopTicker(),
            MasterDataSet.Empty,
            new FakeDialogs(),
            new NoopAlarmService(),
            new FakeHost());
        var view = new IncidentWorkspaceView { DataContext = vm };
        var window = new Window { Content = view, Width = 1200, Height = 700 };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        // The icon-only pencil that opens the Einsatzdaten dialog has no text of its own.
        var pencil = view.GetControl<Button>("EditIncidentDataButton");
        Assert.Equal("Einsatzdaten bearbeiten", Name(pencil));

        // The share-toggle button's Name mirrors its ToolTip binding, so both sharing states
        // must carry the right label, not just whichever rendered first.
        var shareButton = view.GetVisualDescendants().OfType<Button>()
            .Single(b => b.Classes.Contains("icon-btn") && b != pencil);
        var beforeName = Name(shareButton);
        Assert.False(string.IsNullOrWhiteSpace(beforeName));

        await vm.ToggleSharingCommand.ExecuteAsync(null);
        Dispatcher.UIThread.RunJobs();
        var afterName = Name(shareButton);
        Assert.False(string.IsNullOrWhiteSpace(afterName));
        Assert.NotEqual(beforeName, afterName); // name tracks the sharing state, not stuck on one value
    }

    [AvaloniaFact]
    public void Links_clear_search_button_is_named_for_automation()
    {
        var vm = new LinksViewModel(new[] { new Link("Wetterdienst", "https://dwd.de") }, new FakeDialogs());
        vm.FilterText = "wetter";
        var view = new LinksView { DataContext = vm };
        var window = new Window { Content = view, Width = 800, Height = 500 };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        var clearButton = view.GetControl<Button>("ClearLinkSearchButton");
        Assert.True(clearButton.IsVisible);
        Assert.Equal("Suche zurücksetzen", Name(clearButton));
    }

    [AvaloniaFact]
    public void Files_remove_button_is_named_for_automation()
    {
        var clock = new FixedClock();
        var op = new SessionOperator("Müller", "FFB 12/1");
        var session = TestSession.StartNew(
            new FakeStore(),
            clock,
            op,
            "/x.fwincident",
            Array.Empty<(string, bool)>(),
            Array.Empty<(string, bool)>());
        session.Incident.AddFile(clock, op, "brand.jpg", "image/jpeg", 10);
        var vm = new FilesViewModel(session, new FakeDialogs(), () => { });
        var view = new FilesView { DataContext = vm };
        var window = new Window { Content = view, Width = 1200, Height = 600 };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        var removeButton = IconButtons(view).Single(b => (ToolTip.GetTip(b) as string) == "Datei entfernen");
        Assert.Equal("Datei entfernen", Name(removeButton));
    }
}
