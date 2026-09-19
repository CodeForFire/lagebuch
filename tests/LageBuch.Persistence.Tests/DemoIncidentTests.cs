using LageBuch.Domain;
using LageBuch.Domain.Atemschutz;
using LageBuch.Domain.CoMeasurement;
using LageBuch.Domain.Etb;
using LageBuch.Domain.Tasks;
using LageBuch.Domain.Time;
using LageBuch.Domain.ValueObjects;
using LageBuch.Persistence.MasterData;
using Microsoft.Data.Sqlite;

namespace LageBuch.Persistence.Tests;

// The README's "Probefahrt" ships two fictional sample files under docs/samples/. This test is
// what keeps them honest: it builds the demo Einsatz through the domain API, saves and reloads it
// through the real repository, and parses the demo Stammdaten with the real importer -- so a
// schema change that would break either sample breaks CI first.
//
// Set SAMPLES_OUT=<dir> to also write docs/samples/uebung.fwincident (`make samples`).
public class DemoIncidentTests : IDisposable
{
    private readonly string _path = Path.Join(Path.GetTempPath(), $"demo-{Guid.NewGuid():N}.fwincident");

    private sealed class Clock : IClock
    {
        public DateTimeOffset Now { get; set; } = new(2026, 6, 22, 9, 0, 0, TimeSpan.FromHours(2));
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        if (File.Exists(_path))
        {
            File.Delete(_path);
        }
    }

    // Every name, call sign and number below is fictional (see AnonymizedExampleData and
    // docs/samples/demo-stammdaten.json); the two files are meant to be used together.
    public static Incident BuildDemoIncident(IClock clock)
    {
        ArgumentNullException.ThrowIfNull(clock);
        var c = (Clock)clock;
        var elw = new SessionOperator("Müller", "Florian Musterstadt 11/1");

        var incident = Incident.Start(c, elw, "B 3 – Zimmerbrand", new IncidentNumber("B 1.2 260622 0042"));
        incident.SetAddress("Hauptstraße 12", "Musterstadt");
        incident.SeedChecklist(
            new[]
            {
                ("Aufstellort ELW weit genug weg, um nicht zu behindern?", true),
                ("Rote Kennleuchte ein, Blaulicht aus?", false),
                ("Funkgeräte auf Einsatzkanal, Lautstärke geprüft?", true),
                ("PC eingeschaltet, Lagebuch geöffnet?", true),
                ("Rückmeldung an ILS abgesetzt?", true),
            },
            new[]
            {
                ("Alle Trupps abgemeldet, Atemschutzüberwachung beendet?", true),
                ("Einsatzende an ILS gemeldet?", true),
                ("PDF-Bericht exportiert?", false),
                ("Fahrzeug abgerüstet und einsatzbereit?", true),
            });
        incident.ToggleChecklistItem(c, elw, incident.ChecklistAufbau[0].Id);
        incident.ToggleChecklistItem(c, elw, incident.ChecklistAufbau[2].Id);
        incident.ToggleChecklistItem(c, elw, incident.ChecklistAufbau[3].Id);

        incident.AddJournalEntry(c, elw, EtbDirection.Incoming, "Alarmierung B 3 Zimmerbrand, Hauptstraße 12, Rauch aus dem 2. OG, Personen vermutlich noch im Gebäude", from: "ILS", to: "Florian Musterstadt 11/1");
        c.Now = c.Now.AddMinutes(4);
        incident.AssignRole(c, elw, "EL", "Mustermann, Max", callSign: "Florian Musterstadt 11/1", from: c.Now, phone: "01 71 / 1 23 45 67");
        incident.AssignRole(c, elw, "AS-Überwachung", "Wagner, Tobias", from: c.Now);

        c.Now = c.Now.AddMinutes(2);
        incident.AddForceUnit(c, elw, "FF Musterstadt", 9, callSign: "Florian Musterstadt 40/1", status: "Im Einsatz", notes: "Innenangriff", scbaCount: 4, officerCount: 1);
        incident.AddForceUnit(c, elw, "FF Musterstadt", 9, callSign: "Florian Musterstadt 41/1", status: "Im Einsatz", notes: "Wasserversorgung", scbaCount: 2, officerCount: 1);
        incident.AddForceUnit(c, elw, "FF Musterstadt", 3, callSign: "Florian Musterstadt 30/1", status: "Im Einsatz", notes: "Menschenrettung über DLK", scbaCount: 1, officerCount: 1);
        incident.AddJournalEntry(c, elw, EtbDirection.Outgoing, "Lagemeldung: Zimmerbrand im 2. OG, Menschenrettung über DLK eingeleitet, Innenangriff mit 1 Trupp", from: "Florian Musterstadt 11/1", to: "ILS");

        c.Now = c.Now.AddMinutes(1);
        var angriffstrupp = incident.AddScbaTrupp(
            c,
            "Angriffstrupp",
            TruppMember.Crew("Schmidt", "Wagner"),
            entryPressure: 300,
            callSign: "Florian Musterstadt 40/1",
            task: "Innenangriff 2. OG");
        incident.StartScbaTrupp(c, angriffstrupp.Id);
        incident.AddScbaTrupp(
            c,
            "Sicherheitstrupp",
            TruppMember.Crew("Mustermann", "Musterfrau"),
            entryPressure: 300,
            callSign: "Florian Musterstadt 41/1");

        c.Now = c.Now.AddMinutes(3);
        incident.AddJournalEntry(c, elw, EtbDirection.Incoming, "Person aus dem 2. OG über DLK gerettet, Übergabe an RD", from: "Florian Musterstadt 30/1", to: "Florian Musterstadt 11/1");
        incident.AddTask(c, elw, "Nachbarwohnung 2. OG links kontrollieren", "Florian Musterstadt 41/1", TaskImportance.High, TaskUrgency.High, 10);
        incident.AddTask(c, elw, "Stromversorgung abschalten lassen (Stadtwerke)", "Mustermann, Max", TaskImportance.High, TaskUrgency.Medium, 20);
        incident.AddTask(c, elw, "Presse-Info vorbereiten", null, TaskImportance.Low, TaskUrgency.Low, 60);

        c.Now = c.Now.AddMinutes(3);
        incident.RecordScbaPressure(c, angriffstrupp.Id, 240);
        incident.AddJournalEntry(c, elw, EtbDirection.Incoming, "Druckabfrage Angriffstrupp: 240 bar", from: "Florian Musterstadt 40/1", to: "Florian Musterstadt 11/1");
        incident.AddJournalEntry(c, elw, EtbDirection.Outgoing, "Nachforderung: 1 LF zur Ablösung, RD zur Absicherung", from: "Florian Musterstadt 11/1", to: "ILS");

        c.Now = c.Now.AddMinutes(2);
        incident.AddForceUnit(c, elw, "FF Musterdorf", 9, callSign: "Florian Musterdorf 42/1", status: "Auf Anfahrt", scbaCount: 4, officerCount: 1);
        incident.AddJournalEntry(c, elw, EtbDirection.Incoming, "Florian Musterdorf 42/1 alarmiert, ETA 8 Minuten", from: "ILS", to: "Florian Musterstadt 11/1");
        incident.SetTaskCompleted(incident.Tasks[0].Id, true, c, elw);

        c.Now = c.Now.AddMinutes(2);
        incident.AddCoBuilding(c, elw, "Hauptstraße 12", 2, 2); // EG + 2 OG, two Wohnungen each
        var haus = incident.Buildings[0].Id;
        incident.RecordCoValue(c, elw, haus, 2, 1, 120);
        incident.SetDwellingStatus(c, elw, haus, 2, 1, DwellingStatus.Affected);
        incident.SetDwellingDetails(haus, 2, 1, "Musterfrau", true);
        incident.RecordCoValue(c, elw, haus, 2, 2, 35);
        incident.SetDwellingStatus(c, elw, haus, 2, 2, DwellingStatus.Searched);
        incident.RecordCoValue(c, elw, haus, 1, 1, 8);
        incident.SetDwellingStatus(c, elw, haus, 1, 1, DwellingStatus.Searched);
        incident.SetDwellingStatus(c, elw, haus, 1, 2, DwellingStatus.Searched);
        incident.SetDwellingDetails(haus, 0, 1, "Mustermann", false);
        incident.AddJournalEntry(c, elw, EtbDirection.Internal, "CO-Messung im Treppenhaus begonnen, 2. OG rechts 120 ppm", from: "Florian Musterstadt 41/1");

        c.Now = c.Now.AddMinutes(4);
        incident.AddJournalEntry(c, elw, EtbDirection.Outgoing, "Lagemeldung: Feuer unter Kontrolle, Menschenrettung abgeschlossen, Nachlöscharbeiten laufen", from: "Florian Musterstadt 11/1", to: "ILS");
        return incident;
    }

    [Fact]
    public void The_demo_incident_round_trips_through_the_repository()
    {
        var clock = new Clock();
        var incident = BuildDemoIncident(clock);

        IncidentRepository.Save(_path, incident);
        var loaded = IncidentRepository.Load(_path);

        Assert.Equal(IncidentState.Open, loaded.State);
        Assert.Equal("B 3 – Zimmerbrand", loaded.Keyword);
        Assert.Equal("B 1.2 260622 0042", loaded.IncidentNumber!.Value);
        Assert.Equal("Hauptstraße 12", loaded.Street);
        Assert.Equal(incident.Journal.Count, loaded.Journal.Count);
        Assert.True(loaded.Journal.Count(e => e.Direction != EtbDirection.System) >= 8);
        Assert.Equal(2, loaded.Roles.Count);
        Assert.Equal(4, loaded.Forces.Count);
        Assert.Equal(2, loaded.ScbaTrupps.Count);
        Assert.True(loaded.ScbaTrupps[0].IsActive);
        Assert.Equal(240, loaded.ScbaTrupps[0].LatestPressure);
        Assert.True(loaded.ScbaTrupps[1].IsWaiting);
        Assert.Equal(3, loaded.Tasks.Count);
        Assert.True(loaded.Tasks[0].IsCompleted);
        Assert.Single(loaded.Buildings);
        Assert.Equal(6, loaded.Dwellings.Count);
        Assert.Equal(5, loaded.ChecklistAufbau.Count);
        Assert.Equal(3, loaded.ChecklistAufbau.Count(i => i.IsDone));

        WriteSampleIfRequested(incident);
    }

    [Fact]
    public void The_demo_master_data_file_parses_with_the_real_importer()
    {
        var path = Path.Join(RepoRoot(), "docs", "samples", "demo-stammdaten.json");
        Assert.True(File.Exists(path), $"missing sample file: {path}");

        using var stream = File.OpenRead(path);
        var result = MasterDataJson.ParseForImport(stream);

        Assert.Empty(result.DroppedLegacyEntries);
        var set = result.Set;
        Assert.Equal(7, set.Vehicles.Count);
        Assert.Equal(6, set.Personnel.Count);
        Assert.Equal(new[] { "FF Musterstadt", "FF Musterdorf" }, set.Brigades);
        Assert.Contains("Florian Musterstadt 11/1", set.RadioCallSigns);
        Assert.NotEmpty(set.Roles);
        Assert.NotEmpty(set.UnitStatus);
        Assert.NotEmpty(set.TruppTypes);

        // The sample is what a first-time user imports, so it must actually demonstrate #398:
        // a Trupp-Typ carrying its own crew size and Einsatzzeit rather than relying on its name.
        Assert.Contains(set.TruppTypes, t => t.Name == "CSA-Trupp" && t.MemberCount == 3 && t.MaxDurationMinutes == 20);
        Assert.NotEmpty(set.ChecklistTemplateAufbau);
        Assert.NotEmpty(set.ChecklistTemplateAbbau);
        Assert.NotEmpty(set.Links);
        Assert.Equal(50, set.Settings.ReturnPressureBar);

        // The demo incident's call signs must all be covered by the demo Stammdaten, otherwise the
        // Probefahrt shows a Kräfte list whose vehicles the dropdowns don't know.
        var demo = BuildDemoIncident(new Clock());
        Assert.All(
            demo.Forces.Select(f => f.CallSign!),
            callSign => Assert.Contains(callSign, set.RadioCallSigns));
    }

    private static void WriteSampleIfRequested(Incident incident)
    {
        var dir = Environment.GetEnvironmentVariable("SAMPLES_OUT");
        if (string.IsNullOrWhiteSpace(dir))
        {
            return;
        }

        Directory.CreateDirectory(dir);
        var target = Path.Join(dir, "uebung.fwincident");
        foreach (var stale in new[] { target, target + "-wal", target + "-shm" })
        {
            if (File.Exists(stale))
            {
                File.Delete(stale);
            }
        }

        IncidentRepository.Save(target, incident);
        SqliteConnection.ClearAllPools();

        // The shipped sample must be one self-contained file: the connection factory runs the
        // database in WAL mode, and closing the last connection is what folds the -wal back in.
        Assert.True(File.Exists(target));
        Assert.False(File.Exists(target + "-wal"), "WAL sidecar left behind next to the sample");
        Assert.False(File.Exists(target + "-shm"), "SHM sidecar left behind next to the sample");
        Assert.Equal(incident.Journal.Count, IncidentRepository.Load(target).Journal.Count);
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Join(dir.FullName, "LageBuch.sln")))
        {
            dir = dir.Parent;
        }

        return dir?.FullName ?? throw new InvalidOperationException("LageBuch.sln not found above " + AppContext.BaseDirectory);
    }
}
