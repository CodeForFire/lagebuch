namespace LageBuch.Domain.Tests;

public class IncidentSealTests
{
    private const string Hash = "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";

    private static readonly DateTimeOffset T0 = new(2026, 6, 22, 9, 0, 0, TimeSpan.FromHours(2));

    private static IncidentSeal Seal() =>
        new(IncidentSeal.CurrentVersion, Hash, new Dictionary<Guid, string?>());

    [Fact]
    public void Short_form_spells_the_first_60_bits_in_three_crockford_groups()
    {
        // 0x0123456789abcdef: the top 60 bits, five per character, in Crockford's alphabet.
        Assert.Equal("04HM-ASW9-NF6Y", Seal().ShortForm);
    }

    [Fact]
    public void Short_form_of_a_hash_that_is_not_hex_reads_as_question_marks()
    {
        var seal = new IncidentSeal(IncidentSeal.CurrentVersion, "kaputt", new Dictionary<Guid, string?>());

        Assert.Equal("????-????-????", seal.ShortForm);
    }

    [Fact]
    public void Stored_form_round_trips_version_and_hash()
    {
        var file = Guid.NewGuid();
        var files = new Dictionary<Guid, string?> { [file] = null };

        var read = IncidentSeal.FromStoredForm(Seal().StoredForm, files);

        Assert.Equal(IncidentSeal.CurrentVersion, read.Version);
        Assert.Equal(Hash, read.Hash);
        Assert.Null(read.FileSha256[file]);
    }

    [Fact]
    public void A_stored_value_without_a_version_reads_back_with_an_empty_version_instead_of_failing()
    {
        var read = IncidentSeal.FromStoredForm(Hash, new Dictionary<Guid, string?>());

        Assert.Equal(string.Empty, read.Version);
        Assert.Equal(Hash, read.Hash);
    }

    [Fact]
    public void A_closed_incident_takes_a_seal()
    {
        var clock = new FixedClock(T0);
        var op = new SessionOperator("Müller");
        var incident = Incident.Start(clock, op);
        incident.Close(clock, op);

        incident.ApplySeal(Seal());

        Assert.Equal(Hash, incident.Seal!.Hash);
    }

    [Fact]
    public void An_open_incident_cannot_be_sealed()
    {
        var incident = Incident.Start(new FixedClock(T0), new SessionOperator("Müller"));

        Assert.Throws<InvalidOperationException>(() => incident.ApplySeal(Seal()));
        Assert.Null(incident.Seal);
    }

    [Fact]
    public void A_sealed_incident_cannot_be_sealed_again()
    {
        var clock = new FixedClock(T0);
        var op = new SessionOperator("Müller");
        var incident = Incident.Start(clock, op);
        incident.Close(clock, op);
        incident.ApplySeal(Seal());

        Assert.Throws<InvalidOperationException>(() => incident.ApplySeal(Seal()));
    }

    [Fact]
    public void Rehydrate_keeps_the_seal_of_a_closed_incident_and_drops_one_on_an_open_incident()
    {
        var closed = Rehydrate(IncidentState.Closed, Seal());
        var open = Rehydrate(IncidentState.Open, Seal());

        Assert.Equal(Hash, closed.Seal!.Hash);
        Assert.Null(open.Seal);
    }

    private static Incident Rehydrate(IncidentState state, IncidentSeal seal) =>
        Incident.Rehydrate(
            Guid.NewGuid(),
            T0,
            state,
            null,
            null,
            null,
            null,
            null,
            state == IncidentState.Closed ? T0 : null,
            state == IncidentState.Closed ? "Müller" : null,
            [],
            [],
            [],
            [],
            [],
            [],
            [],
            [],
            [],
            [],
            [],
            [],
            seal);
}
