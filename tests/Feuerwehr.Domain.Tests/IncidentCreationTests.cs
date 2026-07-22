using Feuerwehr.Domain.Etb;
using Feuerwehr.Domain.ValueObjects;
using Feuerwehr.Domain.Time;

namespace Feuerwehr.Domain.Tests;

public sealed class FixedClock : IClock
{
    public FixedClock(DateTimeOffset now) => Now = now;
    public DateTimeOffset Now { get; set; }
}

public class IncidentCreationTests
{
    private static readonly DateTimeOffset T0 =
        new(2026, 6, 22, 9, 0, 0, TimeSpan.FromHours(2));

    [Fact]
    public void Start_initialises_open_incident_with_audit()
    {
        var clock = new FixedClock(T0);
        var op = new SessionOperator("Müller", "FFB 12/1");

        var incident = Incident.Start(clock, op, keyword: "Brand");

        Assert.NotEqual(Guid.Empty, incident.Id);
        Assert.Equal(T0, incident.StartedAt);
        Assert.Equal(IncidentState.Open, incident.State);
        Assert.Equal("Brand", incident.Keyword);
        Assert.Equal("opened", Assert.Single(incident.Audit).Action);
    }

    [Fact]
    public void Start_logs_opening_etb_entry()
    {
        var clock = new FixedClock(T0);
        var op = new SessionOperator("Müller", "FFB 12/1");

        var incident = Incident.Start(clock, op);

        var entry = Assert.Single(incident.Journal);
        Assert.Equal("Einsatz begonnen", entry.Text);
        Assert.Equal(EtbDirection.System, entry.Direction);
        Assert.Equal(T0, entry.Timestamp);
        Assert.Equal("Müller (FFB 12/1)", entry.EnteredBy);
        Assert.Null(entry.From);
        Assert.Null(entry.To);
    }

    [Fact]
    public void Start_with_ils_number_names_it_in_the_opening_entry()
    {
        var incident = Incident.Start(
            new FixedClock(T0), new SessionOperator("Müller"), ilsNumber: IlsNumber.Parse("4711"));

        Assert.Equal("4711", incident.IlsNumber!.Value);
        Assert.Equal("Einsatz begonnen (ILS 4711)", Assert.Single(incident.Journal).Text);
    }

    [Fact]
    public void Metadata_setters_update_values()
    {
        var incident = Incident.Start(new FixedClock(T0), new SessionOperator("Müller"));

        incident.SetIncidentNumber(new IncidentNumber("B 4242"));
        incident.SetIlsNumber(IlsNumber.Parse("4242"));
        incident.SetAddress("Hauptstr. 12", "Buchenau");
        incident.SetStatus("in Bearbeitung");

        Assert.Equal("B 4242", incident.IncidentNumber!.Value);
        Assert.Equal("4242", incident.IlsNumber!.Value);
        Assert.Equal("Hauptstr. 12", incident.Street);
        Assert.Equal("Buchenau", incident.District);
        Assert.Equal("in Bearbeitung", incident.Status);
    }

    [Fact]
    public void SetIncidentNumber_null_clears_previous_value()
    {
        var incident = Incident.Start(new FixedClock(T0), new SessionOperator("Müller"));
        incident.SetIncidentNumber(new IncidentNumber("B 4242"));

        incident.SetIncidentNumber(null);

        Assert.Null(incident.IncidentNumber);
    }
}
