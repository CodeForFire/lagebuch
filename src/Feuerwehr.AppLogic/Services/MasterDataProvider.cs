using Feuerwehr.Persistence.MasterData;

namespace Feuerwehr.AppLogic.Services;

public sealed class MasterDataProvider : IMasterDataProvider
{
    private readonly string _path;
    private MasterDataSet? _cached;

    public MasterDataProvider(string masterDataPath) => _path = masterDataPath;

    public MasterDataSet Get() => _cached ??= new MasterDataStore().GetOrSeed(_path);
}
