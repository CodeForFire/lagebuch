using LageBuch.Persistence.MasterData;

namespace LageBuch.AppLogic.Services;

public sealed class MasterDataProvider : IMasterDataProvider
{
    private readonly string _path;
    private MasterDataSet? _cached;

    public MasterDataProvider(string masterDataPath) => _path = masterDataPath;

    public MasterDataSet Get() => _cached ??= MasterDataStore.GetOrCreate(_path);

    public void Save(MasterDataSet set)
    {
        MasterDataStore.Save(_path, set);

        // Re-read rather than trust the in-memory copy: the store is the canonical shape
        // (e.g. personnel comes back name-sorted), so callers see exactly what a fresh start would.
        _cached = MasterDataStore.GetOrCreate(_path);
    }
}
