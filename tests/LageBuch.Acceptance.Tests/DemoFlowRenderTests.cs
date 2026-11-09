using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using LageBuch.App.Shared.Views;
using LageBuch.AppLogic;
using LageBuch.AppLogic.Services;
using LageBuch.AppLogic.Services.Map;
using LageBuch.AppLogic.ViewModels;
using LageBuch.Domain;
using LageBuch.Domain.CoMeasurement;
using LageBuch.Domain.Etb;
using LageBuch.Domain.Leadership;
using LageBuch.Domain.Tasks;
using LageBuch.Domain.ValueObjects;
using LageBuch.Persistence.MasterData;

namespace LageBuch.Acceptance.Tests;

// One fictional Einsatz walked through every module in a single window. This is the source of
// every image in the README: the screenshot grid and the frames of docs/demo/einsatz-flow.gif
// (`make screenshots` / `make demo-gif` set RENDER_OUT; a plain test run captures nothing).
//
// It doubles as an end-to-end smoke test of the workspace: every module is populated through the
// session API and must render the state it was given, and both overlay dialogs must open and
// close from the workspace commands.
public sealed class DemoFlowRenderTests : IDisposable
{
    private const string Elw = "Florian Musterstadt 11/1";
    private const string Lf = "Florian Musterstadt 40/1";
    private const string Lf2 = "Florian Musterstadt 41/1";
    private const string Dlk = "Florian Musterstadt 30/1";
    private const string Musterdorf = "Florian Musterdorf 42/1";

    private readonly string _dir = Directory.CreateTempSubdirectory("lagebuch-demo-").FullName;
    private readonly HttpClient _http = new();

    public void Dispose()
    {
        _http.Dispose();
        Directory.Delete(_dir, recursive: true);
    }

    private sealed class DemoRecent : IRecentFilesStore
    {
        private readonly List<string> _list = new()
        {
            "/home/elw/Einsaetze/20260622-0900.lagebuch",
            "/home/elw/Einsaetze/20260619-1432.lagebuch",
            "/home/elw/Einsaetze/uebung.lagebuch",
        };

        public IReadOnlyList<string> GetRecent() => _list;

        public void Add(string path)
        {
        }

        public void Remove(string path)
        {
        }
    }

    private sealed class DemoMasterData : IMasterDataProvider
    {
        public MasterDataSet Get() => LoadDemoMasterData();

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

    // The same fictional Stammdaten the README tells a first-time user to import, so the
    // screenshots show exactly what the Probefahrt shows.
    private static MasterDataSet LoadDemoMasterData()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Join(dir.FullName, "LageBuch.sln")))
        {
            dir = dir.Parent;
        }

        var path = Path.Join(dir?.FullName ?? throw new InvalidOperationException("LageBuch.sln not found"), "docs", "samples", "demo-stammdaten.json");
        return MasterDataJson.Parse(File.ReadAllText(path));
    }

    private static ChecklistViewModel FirstChecklist(IncidentWorkspaceViewModel vm) =>
        (ChecklistViewModel)vm.NavItems.First(i => i.IsChecklist).Content;

    // The README grid renders at 1920x1032, the shape of the desktop window it documents.
    // Google Play insists on exactly 16:9 for a store screenshot, so `make play-screenshots`
    // overrides the height to 1080. Defaults keep `make screenshots` and `make demo-gif`
    // byte-identical; nothing else reads these.
    private static double RenderWidth => Size("RENDER_WIDTH", 1920);

    private static double RenderHeight => Size("RENDER_HEIGHT", 1032);

    private static double Size(string variable, double fallback) =>
        double.TryParse(
            Environment.GetEnvironmentVariable(variable),
            System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture,
            out var value) && value > 0
            ? value
            : fallback;

    private static void Capture(Window window, string name)
    {
        var dir = Environment.GetEnvironmentVariable("RENDER_OUT");
        if (string.IsNullOrWhiteSpace(dir))
        {
            return;
        }

        Directory.CreateDirectory(dir);
        using var frame = window.CaptureRenderedFrame()!;
        frame.SavePng(Path.Join(dir, name));
    }

    private static void SelectTab(Window window, string header) =>
        WorkspaceRenderHelper.SelectTab(window, header);

    private static void SelectModuleTab(Window window, string railHeader, string tabHeader) =>
        WorkspaceRenderHelper.SelectModuleTab(window, railHeader, tabHeader);

    private static Guid AddNode(
        LocalIncidentSession session, CommandNodeKind kind, string name, string? auftrag, Funkgruppen? funkgruppen = null)
    {
        session.AddCommandNode(kind, name, auftrag: auftrag, funkgruppen: funkgruppen);
        return session.Incident.CommandNodes[^1].Id;
    }

    private static Guid Unit(LocalIncidentSession session, string callSign) =>
        session.Incident.Forces.Single(f => f.CallSign == callSign).Id;

    [AvaloniaFact]
    public void Home_screen_lists_recent_incidents()
    {
        var vm = new HomeViewModel(
            new FakeStore(),
            new DemoMasterData(),
            new DemoRecent(),
            new FakeDialogs(),
            new FixedClock(),
            new ManualTicker(),
            new NoopAlarmService(),
            new NoopIncidentHostController(),
            "0.5.0");
        var window = new Window { Content = new HomeView { DataContext = vm }, Width = RenderWidth, Height = RenderHeight };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(3, vm.RecentFiles.Count);
        Assert.Null(vm.OpenError);
        Capture(window, "home.png");
    }

    [AvaloniaFact]
    public void Stammdaten_editor_shows_the_demo_master_data()
    {
        var vm = new MasterDataEditorViewModel(new DemoMasterData(), new FakeDialogs(), new NoFiles());
        vm.SelectedSection = vm.Sections.Single(s => s.Title == "Fahrzeuge");
        var window = new Window { Content = new MasterDataEditorView { DataContext = vm }, Width = RenderWidth, Height = RenderHeight };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        // The two rail groups: the app's own categories, then the brigade's own Checklisten.
        Assert.Equal(16, vm.Sections.Count);
        Assert.Equal(2, vm.Checklists.Count);
        Capture(window, "stammdaten-editor.png");
    }

    [AvaloniaFact]
    public void One_incident_walked_through_every_module()
    {
        var clock = new FixedClock();
        var md = LoadDemoMasterData();
        var session = LocalIncidentSession.StartNew(
            new FakeStore(),
            clock,
            new SessionOperator(AnonymizedExampleData.OperatorSurname, Elw),
            "/home/elw/Einsaetze/uebung.lagebuch",
            md.ChecklistTemplates.Select(t => new ChecklistSeed(
                t.Id, t.Title, t.Items.Select(i => (i.Text, i.IsMandatory)).ToList())).ToList(),
            new IncidentNumber("B 1.2 260622 0042"),
            keyword: "B 3 – Zimmerbrand");
        session.SetAddress("Hauptstraße", null, "12", "12345", "Musterstadt");
        var ticker = new ManualTicker();

        // The made-up Kartenpaket the Lagekarte tests use, so the Lagekarte frame shows a map
        // without any real OSM data in it.
        var regions = Path.Join(_dir, "regions");
        FictionalRegionPack.Install(regions);
        var vm = new IncidentWorkspaceViewModel(
            session,
            clock,
            ticker,
            md,
            new FakeDialogs(),
            new NoopAlarmService(),
            new NoopIncidentHostController(),
            mapPacks: new RegionPackLibrary(_http, regions));
        var window = new Window { Content = new IncidentWorkspaceView { DataContext = vm }, Width = RenderWidth, Height = RenderHeight };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        // 1) Einsatzdaten: the dialog that fills the header once the ILS has called back.
        vm.EditIncidentDataCommand.Execute(null);
        Dispatcher.UIThread.RunJobs();
        Assert.NotNull(vm.PendingIncidentDataDialog);
        Capture(window, "einsatzdaten.png");
        vm.PendingIncidentDataDialog!.CancelCommand.Execute(null);
        Dispatcher.UIThread.RunJobs();
        Assert.Null(vm.PendingIncidentDataDialog);

        // 2) Aufbau checklist, three of the mandatory items ticked.
        foreach (var index in new[] { 0, 2, 3 })
        {
            session.ToggleChecklistItem(session.Incident.Checklists[0].Items[index].Id);
        }

        SelectTab(window, "AUFBAU");
        Assert.Equal(3, FirstChecklist(vm).Items.Count(i => i.IsDone));
        Capture(window, "checkliste.png");

        // 3) ETB: the first minutes of the incident.
        session.AddJournalEntry(EtbDirection.Incoming, "Alarmierung B 3 Zimmerbrand, Hauptstraße 12, Rauch aus dem 2. OG, Personen vermutlich noch im Gebäude", "ILS", Elw);
        clock.Now = clock.Now.AddMinutes(4);
        session.AssignRole("EL", "Mustermann, Max", callSign: Elw, from: clock.Now, phone: AnonymizedExampleData.PhoneNumber, slot: RoleSlot.Leiter);
        session.AssignRole("AS-Überwachung", "Wagner, Tobias", from: clock.Now);
        clock.Now = clock.Now.AddMinutes(2);
        session.AddForceUnit("FF Musterstadt", 9, Lf, "Im Einsatz", "Innenangriff", scbaCount: 4, officerCount: 1, vehicleType: "HLF");
        session.AddForceUnit("FF Musterstadt", 9, Lf2, "Im Einsatz", "Wasserversorgung", scbaCount: 2, officerCount: 1, vehicleType: "LF");
        session.AddForceUnit("FF Musterstadt", 3, Dlk, "Im Einsatz", "Menschenrettung über DLK", scbaCount: 1, officerCount: 1, vehicleType: "DLK");
        session.AddJournalEntry(EtbDirection.Outgoing, "Lagemeldung: Zimmerbrand im 2. OG, Menschenrettung über DLK eingeleitet, Innenangriff mit 1 Trupp", Elw, "ILS");
        clock.Now = clock.Now.AddMinutes(3);
        session.AddJournalEntry(EtbDirection.Incoming, "Person aus dem 2. OG über DLK gerettet, Übergabe an RD", Dlk, Elw);
        session.AddJournalEntry(EtbDirection.Outgoing, "Nachforderung: 1 LF zur Ablösung, RD zur Absicherung", Elw, "ILS");
        clock.Now = clock.Now.AddMinutes(2);
        session.AddForceUnit("FF Musterdorf", 9, Musterdorf, "Auf Anfahrt", null, scbaCount: 4, officerCount: 1, vehicleType: "LF");
        session.AddJournalEntry(EtbDirection.Incoming, "Florian Musterdorf 42/1 alarmiert, ETA 8 Minuten", "ILS", Elw);
        ticker.Pulse();

        SelectTab(window, "ETB");
        Assert.True(vm.Etb.Entries.Count(e => e.DirectionValue != EtbDirection.System) >= 5);
        Capture(window, "etb.png");

        // 3b) Lagemeldung: the next report to the ILS, drafted from the Einsatz, beside the
        //     Buchstabier-Helfer. Back to the journal afterwards, so the PDF frame shows the ETB.
        session.AddSituationReport(
            Elw,
            "Hauptstraße 12",
            "Zimmerbrand 2. OG, eine Person über DLK gerettet und an den RD übergeben",
            "Innenangriff mit 1 Trupp unter PA, Wasserversorgung aufgebaut",
            "FF Musterstadt, FF Musterdorf auf Anfahrt",
            "1 RTW zur Absicherung");
        SelectModuleTab(window, "ETB", "LAGEMELDUNG");
        vm.SituationReports.SelectedReport = vm.SituationReports.Reports.Single();
        vm.SituationReports.Helper.Input = "Hauptstraße";
        Dispatcher.UIThread.RunJobs();
        Assert.Single(session.Incident.SituationReports);
        Capture(window, "lagemeldung.png");
        SelectModuleTab(window, "ETB", "EINSATZTAGEBUCH");

        // 4) Führung → Organisation: the Führungsorganisation as the A3 sheet draws it, with the
        //    Einsatzleitung, two Abschnitte, a Bereitstellung, the Rettungsdienst and the units.
        session.UpdateLeadershipPost(
            "Hauptstraße 10", Elw, null, new Funkgruppen(["FW_MUSTER 1000"], ["301_F"]), Elw);
        session.SetDispatchChannel("TMO FW_MUSTER");
        var innen = AddNode(
            session,
            CommandNodeKind.Abschnitt,
            "Abschnitt 1 Innenangriff",
            "Brandbekämpfung 2. OG, Menschenrettung",
            new Funkgruppen(["FW_MUSTER 1000"], ["301_F"]));
        var wasser = AddNode(
            session,
            CommandNodeKind.Abschnitt,
            "Abschnitt 2 Wasserversorgung",
            "Wasserversorgung vom Hydranten Hauptstraße",
            new Funkgruppen(["FW_MUSTER 1000"], null));
        var bereitstellung = AddNode(session, CommandNodeKind.Bereitstellung, "Bereitstellung Marktplatz", "Nachrückende Kräfte sammeln");
        AddNode(session, CommandNodeKind.Partner, "Rettungsdienst", null, new Funkgruppen(["RD_MUSTER 1001"], null));
        session.MoveForceUnit(Unit(session, Lf), innen);
        session.MoveForceUnit(Unit(session, Dlk), innen);
        session.MoveForceUnit(Unit(session, Lf2), wasser);
        session.MoveForceUnit(Unit(session, Musterdorf), bereitstellung);
        session.AssignRole("EAL", "Schmidt, Anna", callSign: Lf, from: clock.Now, nodeId: innen, slot: RoleSlot.Leiter);
        session.AssignRole("EAL", "Becker, Jonas", callSign: Lf2, from: clock.Now, nodeId: wasser, slot: RoleSlot.Leiter);
        SelectTab(window, "ORGANISATION");
        Capture(window, "fuehrung.png");

        // 4b) Kräfte: four units, one still on its way, each in its Abschnitt.
        SelectTab(window, "KRÄFTE");
        Assert.Equal(4, vm.Forces.Forces.Count);
        Capture(window, "kraefte.png");

        // 5) Kontakte: the roster the demo Stammdaten describe, Funkrufname included.
        SelectTab(window, "KONTAKTE");
        Assert.Equal(6, vm.Contacts.VisibleContacts.Count);
        Capture(window, "kontakte.png");

        // 6) Funktionen.
        SelectTab(window, "FUNKTIONEN");
        Assert.Equal(4, vm.Roles.Roles.Count);
        Capture(window, "funktionen.png");

        // 6b) Lagekarte: the Einsatzstelle on the made-up Kartenpaket's Marktplatz, the units as
        //     taktische Zeichen around it, and the hydrants close by.
        session.SetIncidentLocation(FictionalRegionPack.Musterstrasse1);
        session.PlaceForceUnit(Unit(session, Lf), new GeoPoint(48.1789, 11.2549));
        session.PlaceForceUnit(Unit(session, Dlk), new GeoPoint(48.1781, 11.2549));
        session.PlaceForceUnit(Unit(session, Lf2), new GeoPoint(48.1778, 11.2531));
        SelectTab(window, "LAGEKARTE");
        Assert.True(SpinWait.SpinUntil(
            () =>
            {
                Dispatcher.UIThread.RunJobs();
                return vm.Lagekarte.WaterPoints is not null || vm.Lagekarte.WaterPointsError is not null;
            },
            TimeSpan.FromSeconds(10)));
        vm.Lagekarte.Map.ChangeViewCommand.Execute(new MapViewChange(FictionalRegionPack.Musterstrasse1, 17));
        Dispatcher.UIThread.RunJobs();
        Assert.NotNull(vm.Lagekarte.WaterPoints);
        Capture(window, "lagekarte.png");

        // 7) Atemschutz: Trupp 1 started at t0 and past its 30 minutes (Rückzugsalarm), Trupp 2
        //    started 20 minutes later and still counting down, Trupp 3 waiting. Driven through the
        //    ScbaViewModel so the rows carry the same live state the operator sees.
        SelectTab(window, "ATEMSCHUTZ");
        vm.Scba.NewDesignation = "Angriffstrupp";
        vm.Scba.NewTruppfuehrer = AnonymizedExampleData.OperatorSurnameAlt;
        vm.Scba.NewTruppmann = AnonymizedExampleData.OperatorSurnameThird;
        vm.Scba.NewCallSign = Lf;
        vm.Scba.AddTruppCommand.Execute(null);
        vm.Scba.Trupps[^1].StartCommand.Execute(null);
        clock.Now = clock.Now.AddMinutes(8);
        ticker.Pulse();
        vm.Scba.Trupps[0].PressureInput = 240;
        vm.Scba.Trupps[0].RecordPressureCommand.Execute(null);
        clock.Now = clock.Now.AddMinutes(12);
        ticker.Pulse();
        vm.Scba.NewDesignation = "Wassertrupp";
        vm.Scba.NewTruppfuehrer = AnonymizedExampleData.PersonLastName;
        vm.Scba.NewTruppmann = AnonymizedExampleData.PersonLastNameAlt;
        vm.Scba.NewCallSign = Lf2;
        vm.Scba.AddTruppCommand.Execute(null);
        vm.Scba.Trupps[^1].StartCommand.Execute(null);
        vm.Scba.NewDesignation = "Sicherheitstrupp";
        vm.Scba.NewTruppfuehrer = AnonymizedExampleData.OperatorSurname;
        vm.Scba.NewTruppmann = AnonymizedExampleData.PersonFirstName;
        vm.Scba.NewCallSign = "Florian Musterdorf 42/1";
        vm.Scba.AddTruppCommand.Execute(null);

        // #399: post Trupp 3 as Trupp 2's Sicherheitstrupp through the picker, exactly as the
        // operator would. Trupp 1 deliberately keeps none, so the screenshot shows both the filled
        // column and the amber "kein Sicherheitstrupp" warning on the row that is in Rückzugsalarm.
        var sicherheitstruppId = vm.Scba.Trupps[2].Id;
        vm.Scba.Trupps[1].SafetyTruppChoices
            .Single(c => c.Id == sicherheitstruppId).SelectCommand.Execute(null);
        clock.Now = clock.Now.AddMinutes(11);
        ticker.Pulse();
        vm.Reminder?.AcknowledgeCommand.Execute(null);
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(3, vm.Scba.Trupps.Count);
        Assert.True(vm.Scba.Trupps[0].IsAlarm, "Trupp 1 should be in Rückzugsalarm after 31 minutes");
        Assert.True(vm.Scba.Trupps[1].IsActive);
        Assert.False(vm.Scba.Trupps[1].IsAlarm);
        Assert.Equal(sicherheitstruppId, vm.Scba.Trupps[1].SafetyTruppChoices.Single(c => c.IsCurrent).Id);
        Assert.Equal("kein Sicherheitstrupp", vm.Scba.Trupps[0].SafetyTruppHint);
        Capture(window, "atemschutz.png");

        // 8) Aufgaben: one done, one overdue, one open.
        session.AddTask("Nachbarwohnung 2. OG links kontrollieren", Lf2, TaskImportance.High, TaskUrgency.High, 10);
        session.AddTask("Stromversorgung abschalten lassen (Stadtwerke)", "Mustermann, Max", TaskImportance.High, TaskUrgency.Medium, 45);
        session.AddTask("Presse-Info vorbereiten", null, TaskImportance.Low, TaskUrgency.Low, 60);
        session.SetTaskCompleted(session.Incident.Tasks[1].Id, true);
        clock.Now = clock.Now.AddMinutes(15); // the 10-minute Nachbarwohnung task is now overdue
        ticker.Pulse();
        SelectTab(window, "AUFGABEN");
        Assert.Equal(3, session.Incident.Tasks.Count);
        Assert.Contains(vm.Tasks.Rows, r => r.IsOverdue);
        Capture(window, "aufgaben.png");

        // 9) CO-Messung: the search grid for the building, floor by floor.
        session.AddCoBuilding("Hauptstraße 12", 2, 2); // EG + 2 OG, two Wohnungen each
        var haus = session.Incident.Buildings[0].Id;
        session.RecordCoValue(haus, 2, 1, 120);
        session.SetDwellingStatus(haus, 2, 1, DwellingStatus.Affected);
        session.SetDwellingDetails(haus, 2, 1, "Musterfrau", KeyAvailability.Available);
        session.RecordCoValue(haus, 2, 2, 35);
        session.SetDwellingStatus(haus, 2, 2, DwellingStatus.Searched);
        session.RecordCoValue(haus, 1, 1, 8);
        session.SetDwellingStatus(haus, 1, 1, DwellingStatus.Searched);
        session.SetDwellingStatus(haus, 1, 2, DwellingStatus.Searched);
        session.SetDwellingDetails(haus, 0, 1, "Mustermann", KeyAvailability.NotAvailable);
        SelectTab(window, "GASMESSUNG");
        Assert.NotNull(vm.CoMessprotokoll.SelectedBuilding);
        Assert.NotEmpty(vm.CoMessprotokoll.MatrixRows);
        Capture(window, "co-messung.png");

        // 10) Beteiligte: the people this Einsatz touches, the same fictional house the CO search
        //     just walked.
        session.AddInvolvedParty("Musterfrau, Maria", "01 71 / 8 76 54 32", "Bewohnerin 2. OG, über DLK gerettet, Übergabe an RD");
        session.AddInvolvedParty("Mustermann, Otto", "01 71 / 9 87 65 43", "Nachbar, hat die Feuerwehr alarmiert");
        session.AddInvolvedParty("POK Beispiel", null, "Polizei vor Ort, Streife 12/3");
        session.SetInvolvedPartyWhereabouts(session.Incident.InvolvedParties[0].Id, "RTW Rotkreuz Musterstadt 71/1");
        SelectTab(window, "BETEILIGTE");
        Assert.Equal(3, session.Incident.InvolvedParties.Count);
        Capture(window, "beteiligte.png");

        // 10b) Ausgabe: what was handed out at the Einsatzstelle, and to whom; one Gerät is back.
        session.IssueItem("Mobilteil 1", "POK Beispiel", null, "Polizei, Streife 12/3", null);
        session.IssueItem("Weste Presse", "Mustermann, Paul", "01 71 / 2 34 56 78", "Musterstädter Tagblatt", null);
        session.IssueItem("Fernglas", "Schmidt, Anna", null, "EAL Abschnitt 1", "Dachbeobachtung");
        session.ReturnIssuedItem(session.Incident.IssuedItems[2].Id);
        SelectTab(window, "AUSGABE");
        Assert.Equal(3, session.Incident.IssuedItems.Count);
        Capture(window, "ausgabe.png");

        // 11) PDF export: the section picker that precedes the report.
        SelectTab(window, "ETB");
        vm.ExportPdfCommand.Execute(null);
        Dispatcher.UIThread.RunJobs();
        Assert.NotNull(vm.PendingPdfExportOptions);
        Assert.All(vm.PendingPdfExportOptions!.Items, i => Assert.True(i.IsSelected));
        Capture(window, "pdf-export.png");
        vm.PendingPdfExportOptions!.CancelCommand.Execute(null);
        Dispatcher.UIThread.RunJobs();
        Assert.Null(vm.PendingPdfExportOptions);
    }
}
