using LageBuch.Domain.Atemschutz;
using LageBuch.Domain.Time;

namespace LageBuch.Domain.Tests;

/// <summary>
/// Changing a Trupp's Trupp-Art after Bereitstellung (#835): the Sicherheitstrupp goes in next
/// and another crew takes over its job. The Trupp stays the same Trupp — crew, Druckwerte and the
/// Sicherheitstrupp link are untouched. The Einsatzzeit the Trupp-Art carries is taken over only
/// while the Trupp still waits; once it is under air the running clock is never moved.
/// </summary>
public class TruppArtChangeTests
{
    private static readonly DateTimeOffset T0 =
        new(2026, 6, 22, 9, 0, 0, TimeSpan.FromHours(2));

    private static Incident NewIncident(out FixedClock clock)
    {
        clock = new FixedClock(T0);
        return Incident.Start(clock, new SessionOperator("Müller", "FFB 12/1"));
    }

    private static AtemschutzTrupp AddTrupp(Incident incident, IClock clock, string designation) =>
        incident.AddScbaTrupp(
            clock,
            designation,
            TruppMember.Crew("Müller", "Schmidt"),
            entryPressure: 300,
            callSign: "Florian Musterdorf 42/1",
            maxDurationMinutes: 30,
            pressureControlIntervalMinutes: 10);

    [Fact]
    public void Changing_the_type_of_a_waiting_trupp_takes_the_new_einsatzzeit()
    {
        var incident = NewIncident(out var clock);
        var trupp = AddTrupp(incident, clock, "Sicherheitstrupp");

        incident.ChangeScbaTruppType(trupp.Id, "  CSA-Trupp ", maxDurationMinutes: 20, pressureControlIntervalMinutes: 6);

        Assert.Equal("CSA-Trupp", trupp.Designation);
        Assert.Equal("Florian Musterdorf 42/1 · CSA-Trupp", trupp.DisplayName);
        Assert.Equal(20, trupp.MaxDurationMinutes);
        Assert.Equal(6, trupp.PressureControlIntervalMinutes);
    }

    [Fact]
    public void Changing_the_type_under_air_keeps_the_running_einsatzzeit()
    {
        var incident = NewIncident(out var clock);
        var trupp = AddTrupp(incident, clock, "Sicherheitstrupp");
        incident.StartScbaTrupp(clock, trupp.Id);
        var due = trupp.DueAt;

        incident.ChangeScbaTruppType(trupp.Id, "Angriffstrupp", maxDurationMinutes: 20, pressureControlIntervalMinutes: 6);

        Assert.Equal("Angriffstrupp", trupp.Designation);
        Assert.Equal(30, trupp.MaxDurationMinutes);
        Assert.Equal(10, trupp.PressureControlIntervalMinutes);
        Assert.Equal(due, trupp.DueAt);
    }

    [Fact]
    public void Changing_the_type_keeps_crew_readings_and_link()
    {
        var incident = NewIncident(out var clock);
        var angriff = AddTrupp(incident, clock, "Angriffstrupp");
        var sicherheit = AddTrupp(incident, clock, "Sicherheitstrupp");
        incident.SetScbaSafetyTrupp(angriff.Id, sicherheit.Id);
        incident.SetScbaSafetyTrupp(sicherheit.Id, null);
        incident.StartScbaTrupp(clock, sicherheit.Id);
        incident.RecordScbaPressure(clock, sicherheit.Id, 250, "2. OG");
        var members = sicherheit.Members.ToList();
        var id = sicherheit.Id;
        var number = sicherheit.TruppNumber;

        incident.ChangeScbaTruppType(sicherheit.Id, "Angriffstrupp", 30, 10);

        Assert.Equal(id, sicherheit.Id);
        Assert.Equal(number, sicherheit.TruppNumber);
        Assert.Equal(members, sicherheit.Members);
        Assert.Equal(250, Assert.Single(sicherheit.PressureReadings).Bar);
        Assert.Equal(300, sicherheit.EntryPressure);
        Assert.Equal(sicherheit.Id, angriff.SafetyTruppId);
        Assert.True(sicherheit.IsActive);
    }

    [Fact]
    public void A_blank_trupp_art_is_refused()
    {
        var incident = NewIncident(out var clock);
        var trupp = AddTrupp(incident, clock, "Sicherheitstrupp");

        var refused = Assert.Throws<ArgumentException>(() => incident.ChangeScbaTruppType(trupp.Id, "  ", 30, 10));

        Assert.StartsWith("Trupp-Bezeichnung darf nicht leer sein.", refused.Message, StringComparison.Ordinal);
        Assert.Equal("Sicherheitstrupp", trupp.Designation);
    }

    [Theory]
    [InlineData(0, 10)]
    [InlineData(30, 0)]
    public void A_non_positive_einsatzzeit_or_interval_is_refused(int maxDuration, int interval)
    {
        var incident = NewIncident(out var clock);
        var trupp = AddTrupp(incident, clock, "Sicherheitstrupp");

        Assert.Throws<ArgumentOutOfRangeException>(() => incident.ChangeScbaTruppType(trupp.Id, "Angriffstrupp", maxDuration, interval));
        Assert.Equal("Sicherheitstrupp", trupp.Designation);
        Assert.Equal(30, trupp.MaxDurationMinutes);
    }

    [Fact]
    public void An_abgenommen_trupp_cannot_change_type()
    {
        var incident = NewIncident(out var clock);
        var trupp = AddTrupp(incident, clock, "Sicherheitstrupp");
        incident.StartScbaTrupp(clock, trupp.Id);
        incident.WithdrawScbaTrupp(clock, trupp.Id);
        incident.MarkScbaRemoved(clock, trupp.Id);

        var refused = Assert.Throws<InvalidOperationException>(() => incident.ChangeScbaTruppType(trupp.Id, "Angriffstrupp", 30, 10));

        Assert.Equal("Die Trupp-Art eines abgenommenen Trupps kann nicht mehr geändert werden.", refused.Message);
        Assert.Equal("Sicherheitstrupp", trupp.Designation);
    }

    [Fact]
    public void A_closed_incident_refuses_the_change()
    {
        var incident = NewIncident(out var clock);
        var trupp = AddTrupp(incident, clock, "Sicherheitstrupp");
        incident.Close(clock, new SessionOperator("Müller", "FFB 12/1"));

        Assert.Throws<IncidentClosedException>(() => incident.ChangeScbaTruppType(trupp.Id, "Angriffstrupp", 30, 10));
    }

    [Fact]
    public void Changing_an_unknown_trupp_is_refused()
    {
        var incident = NewIncident(out _);

        Assert.Throws<KeyNotFoundException>(() => incident.ChangeScbaTruppType(Guid.NewGuid(), "Angriffstrupp", 30, 10));
    }
}
