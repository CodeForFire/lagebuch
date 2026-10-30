using System.Diagnostics.CodeAnalysis;
using LageBuch.Domain;
using LageBuch.Domain.Time;
using LageBuch.Persistence.Sqlite;
using Microsoft.Data.Sqlite;

namespace LageBuch.Persistence.Tests;

public sealed class IncidentRepositoryNavOverridesTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("lagebuch-test-").FullName;

    private string Path_ => Path.Join(_dir, "einsatz.lagebuch");

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        Directory.Delete(_dir, recursive: true);
    }

    [Fact]
    public void Nav_overrides_survive_save_and_load()
    {
        var incident = NewIncident();
        incident.SetNavVisibility("etb", false);
        incident.SetNavVisibility("lagekarte", true);

        IncidentRepository.Save(Path_, incident);
        var loaded = IncidentRepository.Load(Path_);

        Assert.Equal(2, loaded.NavOverrides.Count);
        Assert.False(loaded.NavOverrides["etb"]);
        Assert.True(loaded.NavOverrides["lagekarte"]);
    }

    [Fact]
    public void An_einsatz_at_the_override_caps_survives_save_and_load()
    {
        var incident = NewIncident();
        var keys = Enumerable.Range(0, Incident.MaxNavOverrides)
            .Select(i => i.ToString("D2", System.Globalization.CultureInfo.InvariantCulture) + new string('ä', Incident.MaxNavKeyLength - 2))
            .ToList();
        foreach (var key in keys)
        {
            incident.SetNavVisibility(key, false);
        }

        IncidentRepository.Save(Path_, incident);
        var loaded = IncidentRepository.Load(Path_);

        Assert.Equal(Incident.MaxNavOverrides, loaded.NavOverrides.Count);
        Assert.All(keys, k => Assert.False(loaded.NavOverrides[k]));
    }

    [Fact]
    public void An_einsatz_without_overrides_writes_null()
    {
        IncidentRepository.Save(Path_, NewIncident());

        Assert.Equal(DBNull.Value, Scalar("SELECT nav_overrides FROM incident_meta;"));
        Assert.Empty(IncidentRepository.Load(Path_).NavOverrides);
    }

    [Fact]
    public void A_malformed_nav_overrides_value_opens_with_none()
    {
        IncidentRepository.Save(Path_, NewIncident());
        Tamper("UPDATE incident_meta SET nav_overrides = '{not json';");

        Assert.Empty(IncidentRepository.Load(Path_).NavOverrides);
    }

    [Fact]
    public void An_oversized_nav_overrides_value_opens_with_none()
    {
        IncidentRepository.Save(Path_, NewIncident());
        var huge = "{\"etb\":false,\"pad\":\"" + new string('x', 30000) + "\"}";
        Tamper($"UPDATE incident_meta SET nav_overrides = '{huge}';");

        Assert.Empty(IncidentRepository.Load(Path_).NavOverrides);
    }

    [Fact]
    public void A_v36_file_migrates_and_opens_with_none()
    {
        IncidentRepository.Save(Path_, NewIncident());
        Tamper("ALTER TABLE incident_meta DROP COLUMN nav_overrides; DELETE FROM schema_version; INSERT INTO schema_version (version) VALUES (36);");

        var loaded = IncidentRepository.Load(Path_);

        Assert.Empty(loaded.NavOverrides);
        Assert.Equal(DBNull.Value, Scalar("SELECT nav_overrides FROM incident_meta;"));
        Assert.Equal((long)Migrations.CurrentVersion, Scalar("SELECT version FROM schema_version;"));
    }

    private static Incident NewIncident() =>
        Incident.Start(new FixedTestClock(), new SessionOperator("Muster", "FFB 12/1"));

    [SuppressMessage("Security", "CA2100", Justification = "Test-only: the statement is one of this test's own constants.")]
    private object? Scalar(string sql)
    {
        using var cn = SqliteConnectionFactory.OpenExisting(Path_);
        using var cmd = cn.CreateCommand();
        cmd.CommandText = sql;
        return cmd.ExecuteScalar();
    }

    [SuppressMessage("Security", "CA2100", Justification = "Test-only: the statement is built from this test's own constants to play a hostile or older file.")]
    private void Tamper(string sql)
    {
        using var cn = SqliteConnectionFactory.OpenExisting(Path_);
        using var cmd = cn.CreateCommand();
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }

    private sealed class FixedTestClock : IClock
    {
        public DateTimeOffset Now => new(2026, 10, 30, 14, 0, 0, TimeSpan.FromHours(1));
    }
}
