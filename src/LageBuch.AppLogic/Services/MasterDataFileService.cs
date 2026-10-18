using LageBuch.Persistence.MasterData;

namespace LageBuch.AppLogic.Services;

public sealed class MasterDataFileService : IMasterDataFileService
{
    /// <summary>
    /// The largest Stammdaten JSON an import accepts. A real Feuerwehr's file is tens of
    /// kilobytes; the cap only exists because the file is outside input, and it is enforced on
    /// both ends: here before parsing, and by the Android picker while copying the file in.
    /// </summary>
    public const long MaxImportMegabytes = 5;

    public const long MaxImportBytes = MaxImportMegabytes * 1024 * 1024;

    public static readonly string TooLargeMessage = $"Datei ist größer als das Limit von {MaxImportMegabytes} MB.";

    /// <exception cref="IOException">The file is over <see cref="MaxImportBytes"/>.</exception>
    public MasterDataImportResult Read(string path)
    {
        using var stream = File.OpenRead(path);
        if (stream.Length > MaxImportBytes)
        {
            throw new IOException(TooLargeMessage);
        }

        return MasterDataJson.ParseForImport(stream);
    }

    public void Write(string path, MasterDataSet set) =>
        File.WriteAllText(path, MasterDataJson.Serialize(set));
}
