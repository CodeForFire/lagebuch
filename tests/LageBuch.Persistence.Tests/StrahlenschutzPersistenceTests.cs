using System.Diagnostics.CodeAnalysis;
using LageBuch.Domain;
using LageBuch.Domain.Atemschutz;
using LageBuch.Domain.Time;
using Microsoft.Data.Sqlite;

namespace LageBuch.Persistence.Tests;

/// <summary>The Strahlenschutzüberwachung of a Strahlenschutztrupp in the .lagebuch file (#994).</summary>
public class StrahlenschutzPersistenceTests : IDisposable
{
    private readonly string _path = Path.Join(Path.GetTempPath(), $"rad-{Guid.NewGuid():N}.lagebuch");

    private sealed class Clock : IClock
    {
        public DateTimeOffset Now { get; set; } = new(2026, 6, 22, 9, 0, 0, TimeSpan.FromHours(2));
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        if (File.Exists(_path))
        {
            File.Delete(_path);
        }
    }

    private static Incident IncidentWithStrahlenschutztrupp(out AtemschutzTrupp trupp)
    {
        var clock = new Clock();
        var incident = Incident.Start(clock, new SessionOperator("Müller", "FFB 12/1"));
        trupp = incident.AddScbaTrupp(
            clock,
            "Strahlenschutztrupp",
            TruppMember.Crew("Müller", "Schmidt", "Huber"),
            entryPressure: 300,
            radiationMonitoring: true);
        incident.AddScbaTrupp(clock, "Angriffstrupp", TruppMember.Crew("Bauer", "Klein"), entryPressure: 300);
        return incident;
    }

    [Fact]
    public void Numbers_readings_and_missing_dosimeters_round_trip()
    {
        var incident = IncidentWithStrahlenschutztrupp(out var trupp);
        var full = new RadiationRecord("OSL 1", false, "EPD 7", false, "M 12", "PA 3", new DoseReading(0.125m, DoseUnit.MilliSievert));
        var micro = new RadiationRecord("OSL 2", false, "EPD 8", false, alarmReading: new DoseReading(35m, DoseUnit.MicroSievert));
        var missing = new RadiationRecord(oslDosimeterMissing: true, alarmDosimeterNumber: "EPD 9", alarmDosimeterMissing: true);
        incident.RecordScbaRadiation(trupp.Id, TruppRole.Truppfuehrer, full);
        incident.RecordScbaRadiation(trupp.Id, TruppRole.Truppmann, micro);
        incident.RecordScbaRadiation(trupp.Id, TruppRole.ZweiterTruppmann, missing);

        IncidentRepository.Save(_path, incident);
        var loaded = IncidentRepository.Load(_path);

        var loadedTrupp = loaded.ScbaTrupps[0];
        Assert.True(loadedTrupp.RadiationMonitoring);
        Assert.Equal(new[] { full, micro, missing }, loadedTrupp.Members.Select(m => m.Radiation));

        // An ordinary Trupp stays outside it: no flag, no records.
        var ordinary = loaded.ScbaTrupps[1];
        Assert.False(ordinary.RadiationMonitoring);
        Assert.All(ordinary.Members, m => Assert.Null(m.Radiation));
    }

    // Registered but nothing entered yet: the empty record is not the same as no record.
    [Fact]
    public void An_empty_record_stays_an_empty_record()
    {
        var incident = IncidentWithStrahlenschutztrupp(out _);

        IncidentRepository.Save(_path, incident);

        Assert.All(IncidentRepository.Load(_path).ScbaTrupps[0].Members, m => Assert.Equal(RadiationRecord.Empty, m.Radiation));
    }

    // A .lagebuch is a file someone handed over: an implausible value costs that value, never the
    // whole Einsatz.
    [Theory]
    [InlineData("'abc'", 0)]
    [InlineData("'-1'", 0)]
    [InlineData("'1e9'", 0)]
    [InlineData("'1.5'", 9)]
    public void An_unreadable_reading_in_a_handed_over_file_is_dropped_not_fatal(string doseSql, int unit)
    {
        var incident = IncidentWithStrahlenschutztrupp(out _);
        IncidentRepository.Save(_path, incident);
        Exec($"UPDATE scba_trupp_members SET dose_value = {doseSql}, dose_unit = {unit}, osl_number = '{new string('9', 80)}' WHERE has_radiation = 1;");

        var member = IncidentRepository.Load(_path).ScbaTrupps[0].Members[0];

        Assert.Null(member.Radiation?.AlarmReading);
        Assert.Equal(RadiationRecord.MaxNumberLength, member.Radiation?.OslDosimeterNumber?.Length);
    }

    [Fact]
    public void A_reading_beside_a_missing_alarmdosimeter_is_dropped_not_fatal()
    {
        var incident = IncidentWithStrahlenschutztrupp(out _);
        IncidentRepository.Save(_path, incident);
        Exec("UPDATE scba_trupp_members SET alarm_missing = 1, dose_value = '0.5', dose_unit = 0 WHERE has_radiation = 1;");

        var record = IncidentRepository.Load(_path).ScbaTrupps[0].Members[0].Radiation;

        Assert.True(record?.AlarmDosimeterMissing);
        Assert.Null(record?.AlarmReading);
    }

    // A file from before #994 reads back with no Trupp under Strahlenschutzüberwachung.
    [Fact]
    public void A_v45_file_gains_the_columns_and_still_loads()
    {
        var incident = IncidentWithStrahlenschutztrupp(out _);
        IncidentRepository.Save(_path, incident);
        Exec("UPDATE schema_version SET version = 45; "
            + "ALTER TABLE scba_trupps DROP COLUMN radiation_monitoring; "
            + "ALTER TABLE scba_trupp_members DROP COLUMN has_radiation; "
            + "ALTER TABLE scba_trupp_members DROP COLUMN dose_value;");

        var loaded = IncidentRepository.Load(_path);

        Assert.False(loaded.ScbaTrupps[0].RadiationMonitoring);
        Assert.All(loaded.ScbaTrupps[0].Members, m => Assert.Null(m.Radiation));
    }

    [SuppressMessage("Security", "CA2100", Justification = "Test-only: the statement is built from this test's own constants to play a hostile or older file.")]
    private void Exec(string sql)
    {
        using (var cn = new SqliteConnection($"Data Source={_path}"))
        {
            cn.Open();
            using var cmd = cn.CreateCommand();
            cmd.CommandText = sql;
            cmd.ExecuteNonQuery();
        }

        SqliteConnection.ClearAllPools();
    }
}
