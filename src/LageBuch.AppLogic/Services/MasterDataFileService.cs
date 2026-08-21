using LageBuch.Persistence.MasterData;

namespace LageBuch.AppLogic.Services;

public sealed class MasterDataFileService : IMasterDataFileService
{
    public MasterDataSet Read(string path)
    {
        using var stream = File.OpenRead(path);
        return MasterDataJson.Parse(stream);
    }

    public void Write(string path, MasterDataSet set) =>
        File.WriteAllText(path, MasterDataJson.Serialize(set));
}
