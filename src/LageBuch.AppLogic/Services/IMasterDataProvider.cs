using LageBuch.Persistence.MasterData;

namespace LageBuch.AppLogic.Services;

public interface IMasterDataProvider
{
    MasterDataSet Get();

    /// <summary>Persists the edited set and refreshes the cache returned by <see cref="Get"/>.</summary>
    void Save(MasterDataSet set);
}
