using LageBuch.Domain.Atemschutz;
using LageBuch.Domain.Time;

namespace LageBuch.Domain.Tests;

/// <summary>
/// The Sicherheitstrupp link (#399). The theme running through these is that the domain records a
/// decision rather than policing it: sharing one standby crew, keeping it after it goes under air,
/// and changing it mid-Einsatz are all allowed. The two things that are refused are the two that
/// can only ever be bugs — a Trupp covering itself, and a link to a Trupp that is not in this
/// incident at all.
/// </summary>
public class SicherheitstruppTests
{
    private static readonly DateTimeOffset T0 =
        new(2026, 6, 22, 9, 0, 0, TimeSpan.FromHours(2));

    private static Incident NewIncident(out FixedClock clock)
    {
        clock = new FixedClock(T0);
        return Incident.Start(clock, new SessionOperator("Müller", "FFB 12/1"));
    }

    private static AtemschutzTrupp AddTrupp(Incident incident, IClock clock, string designation) =>
        incident.AddScbaTrupp(clock, designation, TruppMember.Crew("Müller", "Schmidt"), entryPressure: 300);

    private static AtemschutzTrupp Rehydrated(Guid? safetyTruppId) =>
        AtemschutzTrupp.Rehydrate(
            Guid.NewGuid(),
            1,
            T0,
            null,
            null,
            "Angriffstrupp",
            TruppMember.Crew("Müller", "Schmidt"),
            null,
            null,
            300,
            30,
            50,
            5,
            null,
            Array.Empty<PressureReading>(),
            safetyTruppId);

    [Fact]
    public void A_newly_registered_trupp_has_no_safety_trupp()
    {
        var incident = NewIncident(out var clock);
        Assert.Null(AddTrupp(incident, clock, "Angriffstrupp").SafetyTruppId);
    }

    [Fact]
    public void Safety_trupp_is_assigned_and_cleared()
    {
        var incident = NewIncident(out var clock);
        var angriff = AddTrupp(incident, clock, "Angriffstrupp");
        var sicherheit = AddTrupp(incident, clock, "Sicherheitstrupp");

        incident.SetScbaSafetyTrupp(angriff.Id, sicherheit.Id);
        Assert.Equal(sicherheit.Id, angriff.SafetyTruppId);

        incident.SetScbaSafetyTrupp(angriff.Id, null);
        Assert.Null(angriff.SafetyTruppId);
    }

    [Fact]
    public void A_trupp_cannot_be_its_own_safety_trupp()
    {
        var incident = NewIncident(out var clock);
        var trupp = AddTrupp(incident, clock, "Angriffstrupp");

        Assert.Throws<ArgumentException>(() => incident.SetScbaSafetyTrupp(trupp.Id, trupp.Id));
        Assert.Null(trupp.SafetyTruppId);
    }

    [Fact]
    public void A_safety_trupp_from_outside_this_incident_is_refused()
    {
        var incident = NewIncident(out var clock);
        var trupp = AddTrupp(incident, clock, "Angriffstrupp");

        Assert.Throws<KeyNotFoundException>(() => incident.SetScbaSafetyTrupp(trupp.Id, Guid.NewGuid()));
        Assert.Null(trupp.SafetyTruppId);
    }

    [Fact]
    public void Assigning_to_an_unknown_trupp_is_refused()
    {
        var incident = NewIncident(out var clock);
        var sicherheit = AddTrupp(incident, clock, "Sicherheitstrupp");

        Assert.Throws<KeyNotFoundException>(() => incident.SetScbaSafetyTrupp(Guid.NewGuid(), sicherheit.Id));
    }

    [Fact]
    public void One_trupp_may_stand_by_for_two_deployed_trupps()
    {
        var incident = NewIncident(out var clock);
        var first = AddTrupp(incident, clock, "Angriffstrupp");
        var second = AddTrupp(incident, clock, "Wassertrupp");
        var sicherheit = AddTrupp(incident, clock, "Sicherheitstrupp");

        incident.SetScbaSafetyTrupp(first.Id, sicherheit.Id);
        incident.SetScbaSafetyTrupp(second.Id, sicherheit.Id);

        // Whether one crew can really cover two Trupps is a doctrine call for the Einsatzleiter.
        // The app documents the decision and warns in the UI; it does not refuse it.
        Assert.Equal(sicherheit.Id, first.SafetyTruppId);
        Assert.Equal(sicherheit.Id, second.SafetyTruppId);
    }

    [Fact]
    public void The_link_survives_the_safety_trupp_going_under_air_itself()
    {
        var incident = NewIncident(out var clock);
        var angriff = AddTrupp(incident, clock, "Angriffstrupp");
        var sicherheit = AddTrupp(incident, clock, "Sicherheitstrupp");
        incident.SetScbaSafetyTrupp(angriff.Id, sicherheit.Id);

        incident.StartScbaTrupp(clock, sicherheit.Id);

        // Nothing cascades: who covered whom is history, and the ETB carries when the cover ended.
        Assert.Equal(sicherheit.Id, angriff.SafetyTruppId);
    }

    [Fact]
    public void The_safety_trupp_can_be_changed_while_the_covered_trupp_is_under_air()
    {
        var incident = NewIncident(out var clock);
        var angriff = AddTrupp(incident, clock, "Angriffstrupp");
        var first = AddTrupp(incident, clock, "Sicherheitstrupp");
        var relief = AddTrupp(incident, clock, "Wassertrupp");

        incident.SetScbaSafetyTrupp(angriff.Id, first.Id);
        incident.StartScbaTrupp(clock, angriff.Id);
        incident.SetScbaSafetyTrupp(angriff.Id, relief.Id);

        Assert.Equal(relief.Id, angriff.SafetyTruppId);
    }

    [Fact]
    public void The_safety_trupp_can_still_be_corrected_after_the_trupp_is_abgenommen()
    {
        var incident = NewIncident(out var clock);
        var angriff = AddTrupp(incident, clock, "Angriffstrupp");
        var sicherheit = AddTrupp(incident, clock, "Sicherheitstrupp");

        incident.StartScbaTrupp(clock, angriff.Id);
        incident.WithdrawScbaTrupp(clock, angriff.Id);
        incident.MarkScbaRemoved(clock, angriff.Id);

        // Everything else in the ETB is correctable after the fact; a Sicherheitstrupp typed into
        // the wrong row would otherwise be the one mistake nobody could ever put right.
        incident.SetScbaSafetyTrupp(angriff.Id, sicherheit.Id);
        Assert.Equal(sicherheit.Id, angriff.SafetyTruppId);
    }

    [Fact]
    public void A_closed_incident_refuses_the_assignment()
    {
        var incident = NewIncident(out var clock);
        var angriff = AddTrupp(incident, clock, "Angriffstrupp");
        var sicherheit = AddTrupp(incident, clock, "Sicherheitstrupp");
        incident.Close(clock, new SessionOperator("Müller", "FFB 12/1"));

        Assert.Throws<IncidentClosedException>(() => incident.SetScbaSafetyTrupp(angriff.Id, sicherheit.Id));
    }

    [Fact]
    public void Rehydrate_restores_the_link()
    {
        var safetyId = Guid.NewGuid();
        Assert.Equal(safetyId, Rehydrated(safetyId).SafetyTruppId);
    }

    [Fact]
    public void Rehydrate_defaults_to_no_safety_trupp()
    {
        // The trailing optional parameter is what keeps every pre-#399 call site compiling, and a
        // row written before the column existed genuinely had no Sicherheitstrupp recorded.
        Assert.Null(AtemschutzTrupp.Rehydrate(
            Guid.NewGuid(),
            1,
            T0,
            null,
            null,
            "Angriffstrupp",
            TruppMember.Crew("Müller", "Schmidt"),
            null,
            null,
            300,
            30,
            50,
            5,
            null,
            Array.Empty<PressureReading>()).SafetyTruppId);
    }

    [Fact]
    public void Rehydrate_accepts_a_link_to_a_trupp_that_is_not_there()
    {
        // A stored file is history. Refusing to rehydrate a row whose Sicherheitstrupp has gone
        // missing would make the incident unopenable, which is the same stance Rehydrate already
        // takes on a crew that no longer satisfies ValidateCrew.
        Assert.NotNull(Rehydrated(Guid.NewGuid()).SafetyTruppId);
    }

    [Fact]
    public void FindScbaTruppOrDefault_resolves_a_known_id_and_returns_null_otherwise()
    {
        var incident = NewIncident(out var clock);
        var trupp = AddTrupp(incident, clock, "Angriffstrupp");

        Assert.Same(trupp, incident.FindScbaTruppOrDefault(trupp.Id));
        Assert.Null(incident.FindScbaTruppOrDefault(Guid.NewGuid()));
    }
}
