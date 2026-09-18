using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using LageBuch.App.Shared.Views;
using LageBuch.AppLogic;
using LageBuch.AppLogic.Services;
using LageBuch.AppLogic.ViewModels;
using LageBuch.Domain;
using LageBuch.Domain.CoMeasurement;
using LageBuch.Domain.Etb;
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
public class DemoFlowRenderTests
{
    private const string Elw = "Florian Musterstadt 11/1";
    private const string Lf = "Florian Musterstadt 40/1";
    private const string Lf2 = "Florian Musterstadt 41/1";
    private const string Dlk = "Florian Musterstadt 30/1";

    private sealed class DemoRecent : IRecentFilesStore
    {
        private readonly List<string> _list = new()
        {
            "/home/elw/Einsaetze/20260622-0900.fwincident",
            "/home/elw/Einsaetze/20260619-1432.fwincident",
            "/home/elw/Einsaetze/uebung.fwincident",
        };

        public IReadOnlyList<string> GetRecent() => _list;

        public void Add(string path)
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
        var window = new Window { Content = new HomeView { DataContext = vm }, Width = 1920, Height = 1032 };
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
        var window = new Window { Content = new MasterDataEditorView { DataContext = vm }, Width = 1920, Height = 1032 };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(9, vm.Sections.Count);
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
            "/home/elw/Einsaetze/uebung.fwincident",
            md.ChecklistTemplateAufbau.Select(i => (i.Text, i.IsMandatory)),
            md.ChecklistTemplateAbbau.Select(i => (i.Text, i.IsMandatory)),
            new IncidentNumber("B 1.2 260622 0042"),
            keyword: "B 3 – Zimmerbrand");
        session.SetAddress("Hauptstraße 12", "Musterstadt");
        var ticker = new ManualTicker();
        var vm = new IncidentWorkspaceViewModel(
            session,
            clock,
            ticker,
            md,
            new FakeDialogs(),
            new NoopAlarmService(),
            new NoopIncidentHostController());
        var window = new Window { Content = new IncidentWorkspaceView { DataContext = vm }, Width = 1920, Height = 1032 };
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
            session.ToggleChecklistItem(session.Incident.ChecklistAufbau[index].Id);
        }

        SelectTab(window, "AUFBAU");
        Assert.Equal(3, vm.ChecklistAufbau.Items.Count(i => i.IsDone));
        Capture(window, "checkliste.png");

        // 3) ETB: the first minutes of the incident.
        session.AddJournalEntry(EtbDirection.Incoming, "Alarmierung B 3 Zimmerbrand, Hauptstraße 12, Rauch aus dem 2. OG, Personen vermutlich noch im Gebäude", "ILS", Elw);
        clock.Now = clock.Now.AddMinutes(4);
        session.AssignRole("EL", "Mustermann, Max", callSign: Elw, from: clock.Now, phone: AnonymizedExampleData.PhoneNumber);
        session.AssignRole("AS-Überwachung", "Wagner, Tobias", from: clock.Now);
        clock.Now = clock.Now.AddMinutes(2);
        session.AddForceUnit("FF Musterstadt", 9, Lf, "Im Einsatz", "Innenangriff", scbaCount: 4, officerCount: 1);
        session.AddForceUnit("FF Musterstadt", 9, Lf2, "Im Einsatz", "Wasserversorgung", scbaCount: 2, officerCount: 1);
        session.AddForceUnit("FF Musterstadt", 3, Dlk, "Im Einsatz", "Menschenrettung über DLK", scbaCount: 1, officerCount: 1);
        session.AddJournalEntry(EtbDirection.Outgoing, "Lagemeldung: Zimmerbrand im 2. OG, Menschenrettung über DLK eingeleitet, Innenangriff mit 1 Trupp", Elw, "ILS");
        clock.Now = clock.Now.AddMinutes(3);
        session.AddJournalEntry(EtbDirection.Incoming, "Person aus dem 2. OG über DLK gerettet, Übergabe an RD", Dlk, Elw);
        session.AddJournalEntry(EtbDirection.Outgoing, "Nachforderung: 1 LF zur Ablösung, RD zur Absicherung", Elw, "ILS");
        clock.Now = clock.Now.AddMinutes(2);
        session.AddForceUnit("FF Musterdorf", 9, "Florian Musterdorf 42/1", "Auf Anfahrt", null, scbaCount: 4, officerCount: 1);
        session.AddJournalEntry(EtbDirection.Incoming, "Florian Musterdorf 42/1 alarmiert, ETA 8 Minuten", "ILS", Elw);
        ticker.Pulse();

        SelectTab(window, "ETB");
        Assert.True(vm.Etb.Entries.Count(e => e.DirectionValue != EtbDirection.System) >= 5);
        Capture(window, "etb.png");

        // 4) Kräfte: four units, one still on its way.
        SelectTab(window, "KRÄFTE");
        Assert.Equal(4, vm.Forces.Forces.Count);
        Capture(window, "kraefte.png");

        // 5) Funktionen.
        SelectTab(window, "FUNKTIONEN");
        Assert.Equal(2, vm.Roles.Roles.Count);
        Capture(window, "funktionen.png");

        // 6) Atemschutz: Trupp 1 started at t0 and past its 30 minutes (Rückzugsalarm), Trupp 2
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

        // 7) Aufgaben: one done, one overdue, one open.
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

        // 8) CO-Messung: the search grid for the building, floor by floor.
        session.AddCoBuilding("Hauptstraße 12", 2, 2); // EG + 2 OG, two Wohnungen each
        var haus = session.Incident.Buildings[0].Id;
        session.RecordCoValue(haus, 2, 1, 120);
        session.SetDwellingStatus(haus, 2, 1, DwellingStatus.Affected);
        session.SetDwellingDetails(haus, 2, 1, "Musterfrau", true);
        session.RecordCoValue(haus, 2, 2, 35);
        session.SetDwellingStatus(haus, 2, 2, DwellingStatus.Searched);
        session.RecordCoValue(haus, 1, 1, 8);
        session.SetDwellingStatus(haus, 1, 1, DwellingStatus.Searched);
        session.SetDwellingStatus(haus, 1, 2, DwellingStatus.Searched);
        session.SetDwellingDetails(haus, 0, 1, "Mustermann", false);
        SelectTab(window, "CO-MESSUNG");
        Assert.NotNull(vm.CoMessprotokoll.SelectedBuilding);
        Assert.NotEmpty(vm.CoMessprotokoll.MatrixRows);
        Capture(window, "co-messung.png");

        // 9) PDF export: the section picker that precedes the report.
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
