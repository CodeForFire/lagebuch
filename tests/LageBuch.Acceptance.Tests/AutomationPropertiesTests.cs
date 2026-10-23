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
using LageBuch.Domain.Leadership;
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
            Roles = new[] { new Role("EL") },
            UnitStatus = new[] { "Alarmiert" },
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
    public void Master_data_editor_row_buttons_are_named_for_automation()
    {
        var vm = new MasterDataEditorViewModel(new SampleProvider(), new FakeDialogs(), new NoFiles());
        var view = new MasterDataEditorView { DataContext = vm };
        var window = new Window { Content = view, Width = 1080, Height = 680 };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        void AssertNamed(string sectionTitle, int expectedButtons)
        {
            vm.SelectedSection = vm.Sections.FirstOrDefault(s => s.Title == sectionTitle)
                ?? vm.Checklists.Single(c => c.Title == sectionTitle);
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

        // #698, #715: outside Navigation, the Einheiten-Status, the Checklisten and Links a row
        // carries only Entfernen.
        AssertNamed("Rollen", 1);
        AssertNamed("Einheiten-Status", 3); // Nach oben / Nach unten / Entfernen, one row
        AssertNamed("Aufbau", 3);

        // Navigation rows reorder but cannot be removed: they are derived from the modules and
        // the Checklisten, so only «Neue Checkliste» and the delete button change the set.
        AssertNamed("Navigation", 2 * (NavModules.All.Count + 1));
        AssertNamed("Links", 3);
        AssertNamed("Personal", 2); // only Entfernen, once in the wide row and once in its phone form

        var vehicles = (VehiclesSection)vm.Sections.Single(s => s.Title == "Fahrzeuge");
        vehicles.AddCommand.Execute(null);
        AssertNamed("Fahrzeuge", 1);
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
            createTaskFromEntry: (_, _) => { });
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

        public string? ShareKennung => IsHosting ? "7K2Q-M9XD-4HPA" : null;

        public Task StartAsync(LocalIncidentSession session, MasterDataSet masterData, CancellationToken cancellationToken = default)
        {
            IsHosting = true;
            return Task.CompletedTask;
        }

        public bool JoinsClosed => false;

        public event EventHandler? JoinsClosedChanged
        {
            add { }
            remove { }
        }

        public Task StopAsync(CancellationToken cancellationToken = default)
        {
            IsHosting = false;
            return Task.CompletedTask;
        }

        public void RenewPin()
        {
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
            .Single(b => b.Classes.Contains("icon-btn") && b != pencil && b.IsEffectivelyVisible);
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

    // Every control a screen reader cannot name from its own text: the input fields, whose label is
    // the field's header and so not the control's, and any button without a text Content. Template
    // parts (an AutoCompleteBox's inner TextBox) inherit their owner's name and are skipped.
    private static void AssertEveryTextlessControlIsNamed(Visual root)
    {
        var controls = root.GetVisualDescendants().OfType<Control>()
            .Where(c => c.TemplatedParent is null && c is TextBox or AutoCompleteBox or Button)
            .ToList();
        Assert.NotEmpty(controls);
        foreach (var control in controls)
        {
            var named = !string.IsNullOrWhiteSpace(Name(control))
                || control is Button { Content: string { Length: > 0 } };
            Assert.True(named, $"{control.GetType().Name} {control.Name} has neither text nor an AutomationProperties.Name");
        }
    }

    private static LocalIncidentSession DialogSession() =>
        TestSession.StartNew(
            new FakeStore(),
            new FixedClock(),
            new SessionOperator("Müller", "FFB 12/1"),
            "/x.fwincident",
            Array.Empty<(string, bool)>(),
            Array.Empty<(string, bool)>());

    [AvaloniaFact]
    public void Every_field_and_button_of_the_abschnitt_dialog_is_named_for_automation()
    {
        var vm = CommandNodeDialogViewModel.ForCreate(DialogSession(), MasterDataSet.Empty, CommandNodeKind.Abschnitt, null, "Abschnitt 1");
        var view = new CommandNodeDialogView { DataContext = vm };
        var window = new Window { Content = view, Width = 560, Height = 760 };
        window.Show();
        vm.Standort = "Hofeinfahrt";
        vm.CancelCommand.Execute(null); // the discard panel's buttons too
        Dispatcher.UIThread.RunJobs();

        AssertEveryTextlessControlIsNamed(view);
        Assert.Equal("Funkgruppe", Name(view.GetControl<AutoCompleteBox>("CommandNodeFunkgruppeBox")));
    }

    [AvaloniaFact]
    public void Every_field_and_button_of_the_person_dialog_is_named_for_automation()
    {
        var vm = new LeadershipPersonDialogViewModel(DialogSession(), new FixedClock(), MasterDataSet.Empty, null, RoleSlot.Fachberater);
        var view = new LeadershipPersonDialogView { DataContext = vm };
        var window = new Window { Content = view, Width = 560, Height = 560 };
        window.Show();
        vm.PersonName = "Mustermann, Max";
        vm.CancelCommand.Execute(null);
        Dispatcher.UIThread.RunJobs();

        AssertEveryTextlessControlIsNamed(view);
        Assert.Equal("Funktion", Name(view.GetControl<AutoCompleteBox>("PersonDialogRoleBox")));
        Assert.Equal("Handynummer", Name(view.GetControl<TextBox>("PersonDialogPhoneBox")));
    }

    // The Führung board: the drag grips and the pickers carry no text of their own.
    [AvaloniaFact]
    public void Every_textless_control_of_the_fuehrung_board_is_named_for_automation()
    {
        var session = DialogSession();
        session.AddCommandNode(CommandNodeKind.Abschnitt, "Abschnitt 1 Nord");
        var nord = session.Incident.CommandNodes[^1].Id;
        session.AddCommandNode(CommandNodeKind.Unterabschnitt, "Dach", nord);
        session.AddCommandNode(CommandNodeKind.Partner, "Polizei");
        session.AddForceUnit("FF Emmering", 9, "Florian Emmering 40/1", officerCount: 1);
        session.MoveForceUnit(session.Incident.Forces[^1].Id, nord);
        session.AddForceUnit("FF Aich", 6, "Florian Aich 11/1");
        session.AssignRole("Fachberater THW", "Peter Maier", slot: RoleSlot.Fachberater);
        var vm = new LeadershipViewModel(session, new FixedClock(), MasterDataSet.Empty, () => { });
        var view = new LeadershipView { DataContext = vm };
        var window = new Window { Content = view, Width = 1400, Height = 1000 };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        AssertEveryTextlessControlIsNamed(view);
        var grips = view.GetVisualDescendants().OfType<Border>().Where(b => b.Classes.Contains("unit-grip")).ToList();
        Assert.Equal(2, grips.Count);
        Assert.All(grips, g => Assert.Equal("Einheit verschieben", Name(g)));
        Assert.Equal("Führungsstufe wählen", Name(view.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "StufeButton")));
        Assert.Equal("Einsatzleiter festlegen", Name(view.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "LeadershipSetElButton")));
        Assert.Equal(
            "Melder der Einsatzleitung festlegen",
            Name(view.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "LeadershipSetElMelderButton")));
    }

    private static (Window Window, IncidentWorkspaceViewModel Vm, LocalIncidentSession Session) Workspace(
        double width,
        double height,
        params (string Text, bool IsMandatory)[] aufbau)
    {
        var session = TestSession.StartNew(
            new FakeStore(),
            new FixedClock(),
            new SessionOperator("Müller", "FFB 12/1"),
            "/x.fwincident",
            aufbau,
            Array.Empty<(string, bool)>());
        var vm = new IncidentWorkspaceViewModel(
            session,
            new FixedClock(),
            new NoopTicker(),
            WorkspaceRenderHelper.MasterData(),
            new FakeDialogs(),
            new NoopAlarmService(),
            new NoopIncidentHostController());
        var window = new Window { Content = new IncidentWorkspaceView { DataContext = vm }, Width = width, Height = height };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        return (window, vm, session);
    }

    // #670: a CO tile is a button whose only text is a unit number and a glyph; a screen reader
    // announced an unnamed button for every Nutzungseinheit.
    [AvaloniaFact]
    public void Every_co_unit_tile_is_named_for_its_unit_and_state()
    {
        var (window, _, session) = Workspace(1920, 1032);
        session.AddCoBuilding("Mehrfamilienhaus A", 2, 2);
        WorkspaceRenderHelper.SelectTab(window, "CO-MESSUNG");
        Dispatcher.UIThread.RunJobs();

        List<Button> Tiles() => window.GetVisualDescendants().OfType<Button>()
            .Where(b => b.Classes.Contains("unit-tile"))
            .ToList();

        var tiles = Tiles();
        Assert.NotEmpty(tiles);
        Assert.All(tiles, t =>
        {
            var cell = Assert.IsType<DwellingCellViewModel>(t.DataContext);
            Assert.Equal(cell.TileTooltip, Name(t));
            Assert.StartsWith(cell.Label, Name(t), StringComparison.Ordinal);
            Assert.Contains("noch nicht abgesucht", Name(t), StringComparison.Ordinal);
        });

        var building = session.Incident.Buildings[0];
        var first = Assert.IsType<DwellingCellViewModel>(tiles[0].DataContext);
        session.SetDwellingStatus(building.Id, first.FloorOrdinal, first.ApartmentNumber, Domain.CoMeasurement.DwellingStatus.Affected);
        Dispatcher.UIThread.RunJobs();

        var renamed = Tiles().Single(t => t.DataContext is DwellingCellViewModel c && c.FloorOrdinal == first.FloorOrdinal && c.ApartmentNumber == first.ApartmentNumber);
        Assert.Contains("Person(en) betroffen", Name(renamed), StringComparison.Ordinal);
    }

    // #670: the rail's status dot is colour-only, so the tab's name carries the Checkliste's state.
    [AvaloniaFact]
    public void A_checkliste_rail_tab_is_named_with_its_state()
    {
        var (window, vm, _) = Workspace(1920, 1032, ("Blaulicht aus?", true));
        var tabs = WorkspaceRenderHelper.Tabs(window);
        var index = vm.NavItems.ToList().FindIndex(i => i.IsChecklist);
        var item = vm.NavItems[index];
        var container = Assert.IsType<TabItem>(tabs.ContainerFromIndex(index));

        Assert.Equal($"{item.Header} – unvollständig", Name(container));

        ((ChecklistViewModel)item.Content).Items[0].IsDone = true;
        Dispatcher.UIThread.RunJobs();

        Assert.Equal($"{item.Header} – vollständig", Name(container));

        var module = Assert.IsType<TabItem>(tabs.ContainerFromIndex(vm.NavItems.ToList().FindIndex(i => !i.IsChecklist)));
        Assert.Equal(Assert.IsType<WorkspaceNavItemViewModel>(module.DataContext).Header, Name(module));
    }

    [AvaloniaFact]
    public void A_checkliste_phone_nav_cell_is_named_with_its_state()
    {
        var (window, vm, _) = Workspace(412, 915, ("Blaulicht aus?", true));
        var cells = window.GetVisualDescendants().OfType<Button>()
            .Where(b => b.Classes.Contains("navcell") && b.DataContext is WorkspaceNavItemViewModel && b.IsEffectivelyVisible)
            .ToList();
        Assert.NotEmpty(cells);

        Assert.All(cells, c => Assert.Equal(Assert.IsType<WorkspaceNavItemViewModel>(c.DataContext).AccessibleName, Name(c)));
        Assert.Contains(vm.NavItems, i => i.IsChecklist && i.AccessibleName.EndsWith("– unvollständig", StringComparison.Ordinal));
    }
}
