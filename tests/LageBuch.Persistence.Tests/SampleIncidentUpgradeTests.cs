using LageBuch.Domain;
using Microsoft.Data.Sqlite;

namespace LageBuch.Persistence.Tests;

// docs/samples/uebung.fwincident is a whole Einsatzdatei rather than a fixture assembled in a
// test, so this is the end-to-end check that the shipped sample actually opens and still carries
// its Checklisten. It was a V21 file while this branch was written, which is how the V23 upgrade
// was first exercised against something a released build had produced; `make samples` has since
// rewritten it at V23, and the V21 path is covered by ChecklistListMigrationTests' own fixtures.
//
// It is copied before opening: a Load migrates in place, and stamping this branch's
// schema_version onto a tracked file is exactly what AGENTS.md warns never to do.
public class SampleIncidentUpgradeTests : IDisposable
{
    private readonly string _copy = Path.Join(Path.GetTempPath(), $"sample-{Guid.NewGuid():N}.fwincident");

    public SampleIncidentUpgradeTests() => File.Copy(SamplePath(), _copy, overwrite: true);

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        if (File.Exists(_copy))
        {
            File.Delete(_copy);
        }

        GC.SuppressFinalize(this);
    }

    private static string SamplePath()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Join(dir.FullName, "LageBuch.sln")))
        {
            dir = dir.Parent;
        }

        return Path.Join(
            dir?.FullName ?? throw new InvalidOperationException("LageBuch.sln not found"),
            "docs",
            "samples",
            "uebung.fwincident");
    }

    [Fact]
    public void The_shipped_sample_opens_and_keeps_both_of_its_checklists()
    {
        var incident = IncidentRepository.Load(_copy);

        Assert.Equal(2, incident.Checklists.Count);
        Assert.Equal(
            new[] { ChecklistDefaults.AufbauListId, ChecklistDefaults.AbbauListId },
            incident.Checklists.Select(l => l.Id));
        Assert.Equal(new[] { "Aufbau", "Abbau" }, incident.Checklists.Select(l => l.Title));
        Assert.All(incident.Checklists, l => Assert.NotEmpty(l.Items));
    }

    // Loading migrates in place, so the second open exercises the already-upgraded file.
    [Fact]
    public void Reopening_the_upgraded_sample_is_stable()
    {
        var first = IncidentRepository.Load(_copy);
        var second = IncidentRepository.Load(_copy);

        Assert.Equal(
            first.Checklists.Select(l => (l.Id, l.Title, l.Items.Count)),
            second.Checklists.Select(l => (l.Id, l.Title, l.Items.Count)));
    }
}
