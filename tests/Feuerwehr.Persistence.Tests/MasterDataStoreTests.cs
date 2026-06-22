using Feuerwehr.Persistence.MasterData;
using Microsoft.Data.Sqlite;

namespace Feuerwehr.Persistence.Tests;

public class MasterDataStoreTests : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"md-{Guid.NewGuid():N}.db");

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        if (File.Exists(_path)) File.Delete(_path);
    }

    [Fact]
    public void Embedded_defaults_contain_the_known_ffb_lists()
    {
        var set = MasterDataDefaults.LoadEmbedded();
        Assert.Contains("EL", set.Roles);
        Assert.Contains("abbestellt", set.Status);
        Assert.Contains("FFB", set.Districts);
        Assert.Equal(313, set.Streets.Count);
        Assert.Equal(12, set.ChecklistTemplate.Count);
        Assert.Contains(set.Streets, s => s.Name == "Bahnhofstr.");
    }

    [Fact]
    public void GetOrSeed_creates_and_persists_master_data()
    {
        var store = new MasterDataStore();
        var first = store.GetOrSeed(_path);
        Assert.Equal(9, first.Roles.Count);

        // second call reads existing store without reseeding duplicates
        var second = store.GetOrSeed(_path);
        Assert.Equal(first.Streets.Count, second.Streets.Count);
        Assert.Equal(first.Roles.Count, second.Roles.Count);
    }
}
