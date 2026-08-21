using LageBuch.AppLogic.Services;
using LageBuch.Persistence.MasterData;
using Microsoft.Data.Sqlite;

namespace LageBuch.AppLogic.Tests;

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

    [Fact]
    public void Save_returns_personnel_name_sorted_not_in_supplied_order()
    {
        var provider = new MasterDataProvider(_path);
        var current = provider.Get();

        // Deliberately reverse-alphabetical: a buggy Save that does `_cached = set;` would echo
        // this exact order back, whereas the store always reads personnel back sorted by
        // last_name, first_name (see MasterDataStore.ReadPersonnel).
        var zieger = new Person("Zieger", "Anna", null, null, null);
        var amsel = new Person("Amsel", "Berta", null, null, null);
        provider.Save(current with { Personnel = new[] { zieger, amsel } });

        var personnel = provider.Get().Personnel;
        var amselIndex = IndexOfName(personnel, "Amsel");
        var ziegerIndex = IndexOfName(personnel, "Zieger");

        Assert.True(amselIndex >= 0 && ziegerIndex >= 0, "Both saved people should be present.");
        Assert.True(amselIndex < ziegerIndex, "Amsel should sort before Zieger regardless of any pre-existing local roster.");

        static int IndexOfName(IReadOnlyList<Person> people, string lastName)
        {
            for (var i = 0; i < people.Count; i++)
                if (people[i].LastName == lastName)
                    return i;
            return -1;
        }
    }
}
