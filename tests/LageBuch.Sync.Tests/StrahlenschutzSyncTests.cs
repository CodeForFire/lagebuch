using LageBuch.Domain;
using LageBuch.Domain.Atemschutz;

namespace LageBuch.Sync.Tests;

/// <summary>The Strahlenschutzüberwachung of a Strahlenschutztrupp across the wire (#994).</summary>
public class StrahlenschutzSyncTests
{
    private static Incident NewIncident(FixedClock clock) =>
        Incident.Start(clock, new SessionOperator("Host", "FFB 1"), keyword: null, incidentNumber: null);

    private static void ApplyOverWire(SyncCommand command, Incident incident, FixedClock clock) =>
        CommandApplier.Apply(SyncJson.Deserialize<SyncCommand>(SyncJson.Serialize(command)), incident, clock);

    private static AtemschutzTrupp AddOverWire(Incident incident, FixedClock clock)
    {
        ApplyOverWire(
            new AddScbaTruppCommand(
                "Strahlenschutztrupp",
                new[] { new TruppMemberDto(TruppRole.Truppfuehrer, "Müller"), new TruppMemberDto(TruppRole.Truppmann, "Schmidt") },
                "HLF 1",
                null,
                30,
                60,
                10,
                EntryPressure: 300,
                RadiationMonitoring: true),
            incident,
            clock);
        return incident.ScbaTrupps[^1];
    }

    [Fact]
    public void A_client_registers_a_strahlenschutztrupp_on_the_host()
    {
        var clock = new FixedClock();
        var incident = NewIncident(clock);

        var trupp = AddOverWire(incident, clock);

        Assert.True(trupp.RadiationMonitoring);
        Assert.All(trupp.Members, m => Assert.Equal(RadiationRecord.Empty, m.Radiation));
    }

    [Fact]
    public void A_client_records_a_reading_on_the_host()
    {
        var clock = new FixedClock();
        var incident = NewIncident(clock);
        var trupp = AddOverWire(incident, clock);

        ApplyOverWire(
            new RecordScbaRadiationCommand(
                trupp.Id, TruppRole.Truppmann, new RadiationRecordDto("OSL 1", false, "EPD 7", false, "M 2", "PA 3", 35m, DoseUnit.MicroSievert)),
            incident,
            clock);

        Assert.Equal(
            new RadiationRecord("OSL 1", false, "EPD 7", false, "M 2", "PA 3", new DoseReading(35m, DoseUnit.MicroSievert)),
            trupp.Members[1].Radiation);
    }

    // A sync peer on a shared LAN is untrusted: what the domain refuses, the host refuses.
    [Theory]
    [InlineData(80, 1.0, 0)]
    [InlineData(5, -1.0, 0)]
    [InlineData(5, 1.0, 9)]
    public void The_host_refuses_a_record_the_domain_refuses(int numberLength, double dose, int unit)
    {
        var clock = new FixedClock();
        var incident = NewIncident(clock);
        var trupp = AddOverWire(incident, clock);
        var hostile = new RadiationRecordDto(
            new string('9', numberLength), false, "EPD 7", false, null, null, (decimal)dose, (DoseUnit)unit);

        Assert.ThrowsAny<ArgumentException>(
            () => ApplyOverWire(new RecordScbaRadiationCommand(trupp.Id, TruppRole.Truppfuehrer, hostile), incident, clock));
        Assert.Equal(RadiationRecord.Empty, trupp.Members[0].Radiation);
    }

    [Fact]
    public void A_value_without_a_unit_is_refused()
    {
        var dto = new RadiationRecordDto("OSL 1", false, "EPD 7", false, null, null, 1m);

        Assert.Throws<ArgumentException>(() => dto.ToRecord());
    }

    [Fact]
    public void The_snapshot_carries_the_flag_and_every_record()
    {
        var clock = new FixedClock();
        var incident = NewIncident(clock);
        var trupp = AddOverWire(incident, clock);
        var record = new RadiationRecord("OSL 1", false, "EPD 7", true, "M 2", "PA 3");
        incident.RecordScbaRadiation(trupp.Id, TruppRole.Truppfuehrer, record);

        var snapshot = SyncJson.Deserialize<IncidentSnapshot>(SyncJson.Serialize(SnapshotMapper.ToSnapshot(incident)));
        var mirrored = SnapshotMapper.FromSnapshot(snapshot).ScbaTrupps[0];

        Assert.True(mirrored.RadiationMonitoring);
        Assert.Equal(new[] { record, RadiationRecord.Empty }, mirrored.Members.Select(m => m.Radiation));
    }

    // A client reads a host's snapshot leniently: one bad value costs that value, not the Einsatz.
    [Fact]
    public void A_hostile_snapshot_record_is_read_leniently()
    {
        var dto = new RadiationRecordDto(new string('9', 80), false, "EPD 7", true, null, null, 5m, DoseUnit.MilliSievert);

        var record = dto.ToUntrustedRecord();

        Assert.Equal(RadiationRecord.MaxNumberLength, record.OslDosimeterNumber?.Length);
        Assert.Null(record.AlarmReading);
    }
}
