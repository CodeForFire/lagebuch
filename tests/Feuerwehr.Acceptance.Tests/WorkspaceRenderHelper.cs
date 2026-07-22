using Feuerwehr.AppLogic;
using Feuerwehr.AppLogic.Services;
using Feuerwehr.AppLogic.ViewModels;
using Feuerwehr.Domain;
using Feuerwehr.Persistence.MasterData;

namespace Feuerwehr.Acceptance.Tests;

internal sealed class ManualTicker : ITicker
{
    private readonly List<Action> _subs = new();
    public IDisposable Subscribe(Action onTick) { _subs.Add(onTick); return new Sub(); }
    public void Pulse() { foreach (var s in _subs.ToArray()) s(); }
    private sealed class Sub : IDisposable { public void Dispose() { } }
}

internal static class WorkspaceRenderHelper
{
    public static MasterDataSet MasterData() => Md();

    private static MasterDataSet Md() => MasterDataSet.Empty with
    {
        Roles = new[] { "EL" },
        ChecklistTemplate = new[]
        {
            "Aufstellort ELW weit genug weg um nicht zu behindern?",
            "Bei BEIDEN Funkgeräten über die Bedienteile am Armaturenbrett die Lautstärke auf 0 gestellt?",
            "Rote Kennleuchte ein, Blaulicht aus?",
            "PC eingeschaltet und VPN Verbindung aktiviert?",
            "Kopfdaten ETB ausgefüllt (Einsatzort, Bearbeiter)?",
        },
        TruppTypes = new[] { "Angriffstrupp", "Sicherheitstrupp", "CSA-Trupp" },
        Brigades = new[] { "FFB Wache 1", "FFB Wache 2", "Aich", "Puch", "Emmering" },
        UnitStatus = new[] { "Alarmiert", "Auf Anfahrt", "Bereitstellungsraum", "Im Einsatz" },
        RadioCallSigns = new[] { "FFB 1/40/1", "FFB 1/23/1", "Aich 42/1", "Land 1" },
        // Fictional roster: the real personnel.json is gitignored, so tests supply their own.
        Personnel = new[]
        {
            new Person("Mustermann", "Max", "ZF", "Land 1", "01 71 / 1 23 45 67"),
            new Person("Musterfrau", "Erika", "GF", null, "01 71 / 7 65 43 21"),
        },
    };

    public static IncidentWorkspaceViewModel BuildEditableWorkspaceWithAllBars()
    {
        var clock = new FixedClock();
        var checklist = new[]
        {
            "Aufstellort ELW weit genug weg um nicht zu behindern?",
            "Bei BEIDEN Funkgeräten über die Bedienteile am Armaturenbrett die Lautstärke auf 0 gestellt?",
            "Rote Kennleuchte ein, Blaulicht aus?",
            "PC eingeschaltet und VPN Verbindung aktiviert?",
            "Kopfdaten ETB ausgefüllt (Einsatzort, Bearbeiter)?",
        };
        var session = IncidentSession.StartNew(new FakeStore(), clock,
            new SessionOperator("Müller", "FFB 12/1"), "/x.fwincident", checklist);
        var ticker = new ManualTicker();
        var vm = new IncidentWorkspaceViewModel(session, clock, ticker, Md(),
            new FakeDialogs(), new NoopAlarmService());
        vm.IncidentNumberInput = "123";

        // Drive the three header bars into their visible states (like the reported screenshot):
        //   1) ILS reminder running, 2) SCBA pressure-control due, 3) Rückzugsalarm active.
        vm.Reminder!.StartCommand.Execute(null);

        vm.Scba.NewDesignation = "Angriffstrupp";
        vm.Scba.NewTruppfuehrer = "Müller";
        vm.Scba.NewTruppmann = "Schmidt";
        vm.Scba.AddTruppCommand.Execute(null);
        var row = vm.Scba.Trupps[^1];
        row.PressureInput = 300;
        row.StartCommand.Execute(null);

        // Advance past the 30-min max duration so the trupp is in Rückzugsalarm.
        clock.Now = clock.Now.AddMinutes(31);
        ticker.Pulse();

        return vm;
    }
}
