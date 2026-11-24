using LageBuch.Domain.Atemschutz;
using LageBuch.Domain.Time;

namespace LageBuch.Domain.Tests;

/// <summary>
/// The same Trupp cannot be in the Atemschutzüberwachung twice (#968). A Trupp is named by its
/// Funkrufname and Trupp-Art ("HLF 1 · Angriffstrupp"), so two Trupps not yet abgenommen must never
/// share that name, or nobody can tell which one a Druckkontrolle or a Rückzug belongs to. An
/// abgenommen Trupp does not count: a second round from the same Fahrzeug is an ordinary Einsatz.
/// </summary>
public class ScbaTruppNameUniquenessTests
{
    private static readonly DateTimeOffset T0 =
        new(2026, 6, 22, 9, 0, 0, TimeSpan.FromHours(2));

    private static Incident NewIncident(out FixedClock clock)
    {
        clock = new FixedClock(T0);
        return Incident.Start(clock, new SessionOperator("Müller", "FFB 12/1"));
    }

    private static AtemschutzTrupp AddTrupp(Incident incident, IClock clock, string designation, string? callSign = "HLF 1") =>
        incident.AddScbaTrupp(
            clock,
            designation,
            TruppMember.Crew("Müller", "Schmidt"),
            entryPressure: 300,
            callSign: callSign);

    private static void Abnehmen(Incident incident, IClock clock, AtemschutzTrupp trupp)
    {
        incident.StartScbaTrupp(clock, trupp.Id);
        incident.WithdrawScbaTrupp(clock, trupp.Id);
        incident.MarkScbaRemoved(clock, trupp.Id);
    }

    [Fact]
    public void Changing_the_sicherheitstrupp_to_the_angriffstrupp_of_the_same_fahrzeug_is_refused()
    {
        var incident = NewIncident(out var clock);
        AddTrupp(incident, clock, "Angriffstrupp");
        var sicherheit = AddTrupp(incident, clock, "Sicherheitstrupp");

        var refused = Assert.Throws<InvalidOperationException>(
            () => incident.ChangeScbaTruppType(sicherheit.Id, "Angriffstrupp", 30, 10));

        Assert.Equal("„HLF 1 · Angriffstrupp“ ist bereits eingetragen.", refused.Message);
        Assert.Equal("Sicherheitstrupp", sicherheit.Designation);
    }

    [Fact]
    public void Registering_the_same_trupp_twice_is_refused()
    {
        var incident = NewIncident(out var clock);
        AddTrupp(incident, clock, "Angriffstrupp");

        var refused = Assert.Throws<InvalidOperationException>(() => AddTrupp(incident, clock, "Angriffstrupp"));

        Assert.Equal("„HLF 1 · Angriffstrupp“ ist bereits eingetragen.", refused.Message);
        Assert.Single(incident.ScbaTrupps);
    }

    [Theory]
    [InlineData("hlf 1", "angriffstrupp")]
    [InlineData("  HLF 1 ", " Angriffstrupp  ")]
    public void Case_and_surrounding_spaces_do_not_make_a_different_trupp(string callSign, string designation)
    {
        var incident = NewIncident(out var clock);
        var sicherheit = AddTrupp(incident, clock, "Sicherheitstrupp", callSign);
        AddTrupp(incident, clock, "Angriffstrupp");

        Assert.Throws<InvalidOperationException>(() => AddTrupp(incident, clock, designation, callSign));
        Assert.Throws<InvalidOperationException>(() => incident.ChangeScbaTruppType(sicherheit.Id, designation, 30, 10));
        Assert.Equal(2, incident.ScbaTrupps.Count);
    }

    [Fact]
    public void Two_trupps_without_a_funkrufname_and_the_same_trupp_art_are_refused()
    {
        var incident = NewIncident(out var clock);
        AddTrupp(incident, clock, "Angriffstrupp", callSign: null);

        Assert.Throws<InvalidOperationException>(() => AddTrupp(incident, clock, "Angriffstrupp", callSign: "  "));
    }

    [Fact]
    public void The_same_trupp_art_from_another_fahrzeug_is_allowed()
    {
        var incident = NewIncident(out var clock);
        AddTrupp(incident, clock, "Angriffstrupp");
        var other = AddTrupp(incident, clock, "Sicherheitstrupp", "LF 2");

        AddTrupp(incident, clock, "Angriffstrupp", "LF 1");
        incident.ChangeScbaTruppType(other.Id, "Angriffstrupp", 30, 10);

        Assert.Equal(3, incident.ScbaTrupps.Count);
        Assert.Equal("LF 2 · Angriffstrupp", other.DisplayName);
    }

    [Fact]
    public void An_abgenommen_trupp_does_not_block_the_next_round_from_the_same_fahrzeug()
    {
        var incident = NewIncident(out var clock);
        var first = AddTrupp(incident, clock, "Angriffstrupp");
        var sicherheit = AddTrupp(incident, clock, "Sicherheitstrupp");
        Abnehmen(incident, clock, first);

        incident.ChangeScbaTruppType(sicherheit.Id, "Angriffstrupp", 30, 10);
        var third = AddTrupp(incident, clock, "Sicherheitstrupp");

        Assert.Equal("HLF 1 · Angriffstrupp", sicherheit.DisplayName);
        Assert.Equal("HLF 1 · Sicherheitstrupp", third.DisplayName);
    }

    [Fact]
    public void A_trupp_changing_only_the_spelling_of_its_own_trupp_art_is_not_a_duplicate_of_itself()
    {
        var incident = NewIncident(out var clock);
        var trupp = AddTrupp(incident, clock, "angriffstrupp");

        incident.ChangeScbaTruppType(trupp.Id, "Angriffstrupp", 30, 10);

        Assert.Equal("Angriffstrupp", trupp.Designation);
    }

    [Fact]
    public void An_abgenommen_trupp_keeps_its_own_refusal_rather_than_a_duplicate_one()
    {
        var incident = NewIncident(out var clock);
        var first = AddTrupp(incident, clock, "Sicherheitstrupp");
        Abnehmen(incident, clock, first);
        AddTrupp(incident, clock, "Angriffstrupp");

        var refused = Assert.Throws<InvalidOperationException>(
            () => incident.ChangeScbaTruppType(first.Id, "Angriffstrupp", 30, 10));

        Assert.Equal("Die Trupp-Art eines abgenommenen Trupps kann nicht mehr geändert werden.", refused.Message);
    }

    [Fact]
    public void The_query_finds_the_trupp_holding_a_name_except_the_one_asking()
    {
        var incident = NewIncident(out var clock);
        var angriff = AddTrupp(incident, clock, "Angriffstrupp");

        Assert.Same(angriff, incident.FindScbaTruppNamed("hlf 1", "ANGRIFFSTRUPP"));
        Assert.Null(incident.FindScbaTruppNamed("HLF 1", "Angriffstrupp", exceptTruppId: angriff.Id));
        Assert.Null(incident.FindScbaTruppNamed("HLF 1", "Sicherheitstrupp"));
        Assert.Null(incident.FindScbaTruppNamed(null, "Angriffstrupp"));

        var unnamed = AddTrupp(incident, clock, "Wassertrupp", callSign: null);
        Assert.Same(unnamed, incident.FindScbaTruppNamed(null, "Wassertrupp"));
    }
}
