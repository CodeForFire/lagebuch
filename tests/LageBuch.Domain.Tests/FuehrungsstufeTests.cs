using LageBuch.Domain.Etb;
using LageBuch.Domain.Leadership;

namespace LageBuch.Domain.Tests;

public class FuehrungsstufeTests
{
    private static readonly DateTimeOffset T0 = new(2026, 9, 27, 14, 0, 0, TimeSpan.FromHours(2));

    private static (Incident Incident, FixedClock Clock, SessionOperator Op) NewIncident()
    {
        var clock = new FixedClock(T0);
        var op = new SessionOperator("Muster", "FFB 12/1");
        return (Incident.Start(clock, op), clock, op);
    }

    private static EtbEntry Last(Incident incident) => incident.Journal[^1];

    [Fact]
    public void Setting_the_stufe_logs_it()
    {
        var (incident, clock, op) = NewIncident();

        incident.SetFuehrungsstufe(clock, op, Fuehrungsstufe.C);

        Assert.Equal(Fuehrungsstufe.C, incident.Fuehrungsstufe);
        Assert.Equal("Führungsstufe C festgelegt", Last(incident).Text);
        Assert.Equal(EtbDirection.System, Last(incident).Direction);
    }

    [Fact]
    public void Setting_the_same_stufe_writes_nothing()
    {
        var (incident, clock, op) = NewIncident();
        incident.SetFuehrungsstufe(clock, op, Fuehrungsstufe.B);
        var count = incident.Journal.Count;

        incident.SetFuehrungsstufe(clock, op, Fuehrungsstufe.B);

        Assert.Equal(count, incident.Journal.Count);
        Assert.Equal(Fuehrungsstufe.B, incident.Fuehrungsstufe);
    }

    [Fact]
    public void Clearing_logs_aufgehoben()
    {
        var (incident, clock, op) = NewIncident();
        incident.SetFuehrungsstufe(clock, op, Fuehrungsstufe.D);

        incident.SetFuehrungsstufe(clock, op, null);

        Assert.Null(incident.Fuehrungsstufe);
        Assert.Equal("Führungsstufe aufgehoben", Last(incident).Text);
    }

    [Fact]
    public void Post_funkgruppe_change_is_logged_other_fields_are_silent()
    {
        var (incident, clock, op) = NewIncident();
        Assert.Equal(LeadershipPost.Empty, incident.LeadershipPost);
        var count = incident.Journal.Count;

        var silent = incident.UpdateLeadershipPost(clock, op, " Feuerwache ", "Florian 1", "0123", null);

        Assert.Equal(count, incident.Journal.Count);
        Assert.Equal(new LeadershipPost("Feuerwache", "Florian 1", "0123", null), silent);
        Assert.Equal(silent, incident.LeadershipPost);

        incident.UpdateLeadershipPost(clock, op, "Feuerwache", "Florian 1", "0123", " Kanal 5 ");
        Assert.Equal("Funkgruppe Einsatzleitung: Kanal 5", Last(incident).Text);

        var afterLog = incident.Journal.Count;
        incident.UpdateLeadershipPost(clock, op, "Wache 2", "Florian 1", "0123", "Kanal 5");
        Assert.Equal(afterLog, incident.Journal.Count);

        incident.UpdateLeadershipPost(clock, op, "Wache 2", "Florian 1", "0123", null);
        Assert.Equal(afterLog, incident.Journal.Count);
        Assert.Null(incident.LeadershipPost.Funkgruppe);
    }

    [Fact]
    public void Post_telefon_over_50_is_refused()
    {
        var (incident, clock, op) = NewIncident();

        Assert.Throws<ArgumentException>(
            () => incident.UpdateLeadershipPost(clock, op, null, null, new string('1', 51), null));

        Assert.Equal(LeadershipPost.Empty, incident.LeadershipPost);
    }

    [Fact]
    public void Closed_incident_refuses_both()
    {
        var (incident, clock, op) = NewIncident();
        incident.Close(clock, op);

        Assert.Throws<IncidentClosedException>(() => incident.SetFuehrungsstufe(clock, op, Fuehrungsstufe.A));
        Assert.Throws<IncidentClosedException>(
            () => incident.UpdateLeadershipPost(clock, op, null, null, null, "K1"));
    }

    [Fact]
    public void Setting_an_undefined_stufe_is_refused_and_changes_nothing()
    {
        // JsonStringEnumConverter also accepts a raw integer, so a sync peer can send "stufe": 7.
        var (incident, clock, op) = NewIncident();
        var entries = incident.Journal.Count;

        Assert.Throws<ArgumentOutOfRangeException>(
            () => incident.SetFuehrungsstufe(clock, op, (Fuehrungsstufe)7));

        Assert.Null(incident.Fuehrungsstufe);
        Assert.Equal(entries, incident.Journal.Count);
    }
}
