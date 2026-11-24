using LageBuch.AppLogic.ViewModels;
using LageBuch.Domain;
using LageBuch.Domain.Atemschutz;
using LageBuch.Domain.Etb;
using LageBuch.Persistence.MasterData;
using LageBuch.Sync;

namespace LageBuch.AppLogic.Tests;

/// <summary>
/// Bereitstellen on a joined device (#995 review): the "bereitgestellt" line goes into the ETB
/// exactly when the host took the Trupp -- not on a refusal, and not lost with a lost answer.
/// </summary>
public class ScbaJoinedRegistrationTests
{
    private const string CsaTrupp = "CSA-Trupp";

    private static readonly DateTimeOffset T0 = new(2026, 6, 22, 9, 0, 0, TimeSpan.FromHours(2));

    private static MasterDataSet Md() => MasterDataSet.Empty with
    {
        TruppTypes = new[] { new TruppType(CsaTrupp, 3, 20, NamedByNumber: true) },
    };

    private static (LocalIncidentSession Host, SnapshotRoundTrippingSession Joined, ScbaViewModel Vm) Joined()
    {
        var clock = new FixedClock(T0);
        var host = TestSession.StartNew(
            new FakeStore(),
            clock,
            new SessionOperator("Müller", "FFB 12/1"),
            "/x.lagebuch",
            Array.Empty<(string, bool)>(),
            Array.Empty<(string, bool)>());
        var joined = new SnapshotRoundTrippingSession(host);
        var vm = new ScbaViewModel(joined, Md(), clock, new FakeTicker(), new FakeAlarmService(), () => { });
        Fill(vm, "Müller", "Schmidt", "Huber");
        return (host, joined, vm);
    }

    private static void Fill(ScbaViewModel vm, string truppfuehrer, string truppmann, string zweiter)
    {
        vm.NewDesignation = CsaTrupp;
        vm.NewTruppfuehrer = truppfuehrer;
        vm.NewTruppmann = truppmann;
        vm.NewZweiterTruppmann = zweiter;
    }

    private static string[] BereitgestelltLines(LocalIncidentSession host) =>
        host.Incident.Journal.Where(e => e.Text.Contains("bereitgestellt", StringComparison.Ordinal)).Select(e => e.Text).ToArray();

    // The host takes CSA-Trupp 1 the way another device's press would have.
    private static Task TakenByHost(LocalIncidentSession host, int truppNumber = 1) =>
        host.AddScbaTruppAsync(
            CsaTrupp,
            TruppMember.Crew("Müller", "Schmidt", "Huber"),
            entryPressure: 300,
            truppNumber: truppNumber,
            callSign: AtemschutzTrupp.NumberedCallSign(CsaTrupp, 1),
            maxDurationMinutes: 20);

    [Fact]
    public async Task A_refused_trupp_writes_no_etb_line_and_keeps_the_form()
    {
        var (host, joined, vm) = Joined();
        joined.RefuseNextScbaTrupp = new CommandRejectedException("CSA-Trupp 1 ist bereits vergeben.");

        await vm.AddTruppCommand.ExecuteAsync(null);

        Assert.Empty(host.Incident.ScbaTrupps);
        Assert.Empty(BereitgestelltLines(host));
        Assert.Equal("CSA-Trupp 1 ist bereits vergeben.", vm.ErrorSummary);
        Assert.Equal("Müller", vm.NewTruppfuehrer);
    }

    [Fact]
    public async Task Closing_the_form_clears_the_hosts_refusal()
    {
        var (_, joined, vm) = Joined();
        joined.RefuseNextScbaTrupp = new CommandRejectedException("CSA-Trupp 1 ist bereits vergeben.");
        await vm.AddTruppCommand.ExecuteAsync(null);

        vm.CloseComposerCommand.Execute(null);

        Assert.Null(vm.SubmitError);
        Assert.Null(vm.ErrorSummary);
    }

    [Fact]
    public async Task A_trupp_the_host_took_is_logged_once_although_its_answer_was_lost()
    {
        var (host, joined, vm) = Joined();
        joined.LoseNextScbaTruppAnswer = new HttpRequestException("Verbindung abgebrochen");

        await vm.AddTruppCommand.ExecuteAsync(null);
        host.AddJournalEntry(EtbDirection.Incoming, "Lage unverändert", "Leitstelle", "ELW"); // a later Stand

        Assert.Single(host.Incident.ScbaTrupps);
        Assert.StartsWith("CSA-Trupp 1 bereitgestellt", Assert.Single(BereitgestelltLines(host)), StringComparison.Ordinal);
        Assert.Null(vm.ErrorSummary);
        Assert.Equal(string.Empty, vm.NewTruppfuehrer);
    }

    [Fact]
    public async Task A_lost_answer_is_logged_once_a_stand_shows_the_trupp()
    {
        var (host, joined, vm) = Joined();
        joined.RefuseNextScbaTrupp = new OperationCanceledException();

        await vm.AddTruppCommand.ExecuteAsync(null);

        Assert.Empty(BereitgestelltLines(host));
        Assert.Equal(RemoteIncidentSession.CommandLostReason, vm.ErrorSummary);

        await TakenByHost(host); // the reconcile poll brings the Trupp the host did take

        Assert.Single(BereitgestelltLines(host));
        Assert.Null(vm.ErrorSummary);
        Assert.Equal(string.Empty, vm.NewTruppfuehrer);
    }

    [Fact]
    public async Task A_trupp_resent_after_a_lost_answer_is_logged_once()
    {
        var (host, joined, vm) = Joined();
        joined.RefuseNextScbaTrupp = new HttpRequestException("Verbindung abgebrochen");
        await vm.AddTruppCommand.ExecuteAsync(null);

        await vm.AddTruppCommand.ExecuteAsync(null); // this time it gets through
        host.AddJournalEntry(EtbDirection.Incoming, "Lage unverändert", "Leitstelle", "ELW"); // a later Stand

        Assert.Single(host.Incident.ScbaTrupps);
        Assert.Single(BereitgestelltLines(host));
    }

    [Fact]
    public async Task A_lost_answer_confirmed_later_leaves_the_next_trupp_in_the_form()
    {
        var (host, joined, vm) = Joined();
        joined.RefuseNextScbaTrupp = new HttpRequestException("Verbindung abgebrochen");
        await vm.AddTruppCommand.ExecuteAsync(null);

        Fill(vm, "Weber", "Wagner", "Fischer"); // the Lagebuchführer moved on to the next Trupp
        await TakenByHost(host);

        Assert.Single(BereitgestelltLines(host));
        Assert.Equal("Weber", vm.NewTruppfuehrer);
    }
}
