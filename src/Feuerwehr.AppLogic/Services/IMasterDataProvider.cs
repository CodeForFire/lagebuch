using Feuerwehr.Persistence.MasterData;

namespace Feuerwehr.AppLogic.Services;

public interface IMasterDataProvider
{
    MasterDataSet Get();
}
