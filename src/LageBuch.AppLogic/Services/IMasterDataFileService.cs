using LageBuch.Persistence.MasterData;

namespace LageBuch.AppLogic.Services;

/// <summary>
/// Reads and writes a master-data set to a JSON file on disk. Keeps the editor view-model free of
/// <see cref="System.IO.File"/> and JSON details, and lets tests substitute a canned/capturing fake.
/// IO and parse failures propagate to the caller.
/// </summary>
public interface IMasterDataFileService
{
    MasterDataSet Read(string path);
    void Write(string path, MasterDataSet set);
}
