using LageBuch.Persistence.MasterData;

namespace LageBuch.AppLogic.Services;

public sealed class MasterDataFileService : IMasterDataFileService
{
    public MasterDataImportResult Read(string path)
    {
        using var stream = File.OpenRead(path);
        return MasterDataJson.ParseForImport(stream);
    }

    public void Write(string path, MasterDataSet set) =>
        File.WriteAllText(path, MasterDataJson.Serialize(set));
}
