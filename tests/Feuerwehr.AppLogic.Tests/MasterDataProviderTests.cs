using Feuerwehr.AppLogic.Services;
using Microsoft.Data.Sqlite;

namespace Feuerwehr.AppLogic.Tests;

public class MasterDataProviderTests : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"mdp-{Guid.NewGuid():N}.db");

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        if (File.Exists(_path)) File.Delete(_path);
    }

    [Fact]
    public void Save_persists_and_refreshes_the_cache()
    {
        var provider = new MasterDataProvider(_path);
        var current = provider.Get();

        provider.Save(current with { Roles = new[] { "EL", "Nur Ich" } });

        Assert.Equal(new[] { "EL", "Nur Ich" }, provider.Get().Roles);          // cache refreshed
        Assert.Equal(new[] { "EL", "Nur Ich" }, new MasterDataProvider(_path).Get().Roles); // and on disk
    }
}
