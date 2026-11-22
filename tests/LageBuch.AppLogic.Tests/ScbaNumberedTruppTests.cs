using LageBuch.AppLogic.ViewModels;
using LageBuch.Domain;
using LageBuch.Domain.Atemschutz;
using LageBuch.Persistence.MasterData;

namespace LageBuch.AppLogic.Tests;

/// <summary>
/// A Trupp-Typ marked "Funkrufname aus Nummer" (#995): the form asks for a number instead of a
/// Funkrufname and the Trupp is called "CSA-Trupp 1" -- in the grid, the banners and the ETB.
/// </summary>
public class ScbaNumberedTruppTests
{
    private const string CsaTrupp = "CSA-Trupp";

    private static readonly DateTimeOffset T0 = new(2026, 6, 22, 9, 0, 0, TimeSpan.FromHours(2));

    private static MasterDataSet Md() => MasterDataSet.Empty with
    {
        TruppTypes = new[] { new TruppType("Angriffstrupp"), new TruppType(CsaTrupp, 3, 20, NamedByNumber: true) },
    };

    private static LocalIncidentSession NewSession(FixedClock clock) =>
        TestSession.StartNew(
            new FakeStore(),
            clock,
            new SessionOperator("Müller", "FFB 12/1"),
            "/x.lagebuch",
            Array.Empty<(string, bool)>(),
            Array.Empty<(string, bool)>());

    private static ScbaViewModel Vm(FixedClock clock, LocalIncidentSession session) =>
        new(session, Md(), clock, new FakeTicker(), new FakeAlarmService(), () => { });

    private static void FillCrew(ScbaViewModel vm)
    {
        vm.NewTruppfuehrer = "Müller";
        vm.NewTruppmann = "Schmidt";
        vm.NewZweiterTruppmann = "Huber";
    }

    [Fact]
    public void Numbered_trupp_art_hides_the_call_sign_and_suggests_number_one()
    {
        var clock = new FixedClock(T0);
        using var vm = Vm(clock, NewSession(clock));

        vm.NewDesignation = "Angriffstrupp";
        Assert.False(vm.IsNamedByNumber);

        vm.NewDesignation = CsaTrupp;
        Assert.True(vm.IsNamedByNumber);
        Assert.Equal(1, vm.NewTypeNumber);
    }

    [Fact]
    public void Registered_numbered_trupp_is_called_csa_trupp_1_in_grid_and_etb()
    {
        var clock = new FixedClock(T0);
        var session = NewSession(clock);
        using var vm = Vm(clock, session);

        vm.NewDesignation = CsaTrupp;
        FillCrew(vm);

        // A Funkrufname typed before the Trupp-Art was switched is not what the Trupp is called.
        vm.NewCallSign = "HLF 1";
        vm.AddTruppCommand.Execute(null);

        var row = Assert.Single(vm.Trupps);
        Assert.Equal("CSA-Trupp 1", row.DisplayName);
        Assert.Equal("CSA-Trupp 1", row.CallSign);
        var entry = Assert.Single(
            session.Incident.Journal,
            e => e.Text == "CSA-Trupp 1 bereitgestellt: Müller / Schmidt / Huber, Einstiegsdruck 300 bar");
        Assert.Equal("CSA-Trupp 1", entry.From);
    }

    // Counted per Trupp-Art: the Angriffstrupp before it does not make this "CSA-Trupp 2".
    [Fact]
    public void Second_csa_trupp_is_suggested_number_two()
    {
        var clock = new FixedClock(T0);
        using var vm = Vm(clock, NewSession(clock));
        vm.NewDesignation = "Angriffstrupp";
        vm.NewTruppfuehrer = "Maier";
        vm.NewTruppmann = "Wagner";
        vm.AddTruppCommand.Execute(null);

        vm.NewDesignation = CsaTrupp;
        Assert.Equal(1, vm.NewTypeNumber);
        FillCrew(vm);
        vm.AddTruppCommand.Execute(null);

        vm.NewDesignation = CsaTrupp;
        Assert.Equal(2, vm.NewTypeNumber);
    }

    [Fact]
    public void A_number_set_by_hand_names_the_trupp()
    {
        var clock = new FixedClock(T0);
        using var vm = Vm(clock, NewSession(clock));

        vm.NewDesignation = CsaTrupp;
        vm.NewTypeNumber = 5;
        FillCrew(vm);
        vm.AddTruppCommand.Execute(null);

        Assert.Equal("CSA-Trupp 5", Assert.Single(vm.Trupps).DisplayName);
    }

    [Fact]
    public void Number_already_in_use_is_refused_with_a_message()
    {
        var clock = new FixedClock(T0);
        using var vm = Vm(clock, NewSession(clock));
        vm.NewDesignation = CsaTrupp;
        FillCrew(vm);
        vm.AddTruppCommand.Execute(null);

        vm.NewDesignation = CsaTrupp;
        vm.NewTypeNumber = 1;
        FillCrew(vm);
        vm.AddTruppCommand.Execute(null);

        Assert.Single(vm.Trupps);
        Assert.Equal(ValidationMessages.TypeNumberInUse, vm.NewTypeNumberError);
        Assert.Contains(ValidationMessages.TypeNumberInUse, vm.ErrorSummary, StringComparison.Ordinal);
    }

    [Fact]
    public void Number_is_required()
    {
        var clock = new FixedClock(T0);
        using var vm = Vm(clock, NewSession(clock));
        vm.NewDesignation = CsaTrupp;
        vm.NewTypeNumber = 0;
        FillCrew(vm);
        vm.AddTruppCommand.Execute(null);

        Assert.Empty(vm.Trupps);
        Assert.Equal(ValidationMessages.Required, vm.NewTypeNumberError);
    }

    // A Trupp registered elsewhere -- a joined client, or the host while this form sits open --
    // moves the suggestion on, as long as nobody typed a number of their own.
    [Fact]
    public void A_trupp_registered_meanwhile_moves_the_suggestion_on()
    {
        var clock = new FixedClock(T0);
        var session = NewSession(clock);
        using var vm = Vm(clock, session);
        vm.NewDesignation = CsaTrupp;
        Assert.Equal(1, vm.NewTypeNumber);

        session.AddScbaTrupp(
            CsaTrupp,
            TruppMember.Crew("Maier", "Wagner", "Fischer"),
            entryPressure: 300,
            callSign: AtemschutzTrupp.NumberedCallSign(CsaTrupp, 1));

        Assert.Equal(2, vm.NewTypeNumber);
    }

    // The TRUPP-ART ComboBox writes null through its binding when the form resets, whatever the
    // property's declared type -- the number suggestion must not throw on it mid-Einsatz.
    [Fact]
    public void A_cleared_trupp_art_selection_does_not_throw()
    {
        var clock = new FixedClock(T0);
        using var vm = Vm(clock, NewSession(clock));
        vm.NewDesignation = CsaTrupp;

        typeof(ScbaViewModel).GetProperty(nameof(ScbaViewModel.NewDesignation))?.SetValue(vm, null);

        Assert.False(vm.IsNamedByNumber);
    }

    [Fact]
    public void An_ordinary_trupp_art_shows_no_number_error()
    {
        var clock = new FixedClock(T0);
        using var vm = Vm(clock, NewSession(clock));
        vm.NewDesignation = "Angriffstrupp";
        vm.NewTypeNumber = 0;
        vm.AddTruppCommand.Execute(null);

        Assert.Null(vm.NewTypeNumberError);
    }
}
