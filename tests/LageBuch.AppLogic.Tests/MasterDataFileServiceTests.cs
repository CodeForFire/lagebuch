using LageBuch.AppLogic.Services;

namespace LageBuch.AppLogic.Tests;

public class MasterDataFileServiceTests : IDisposable
{
    private readonly string _path = Path.Join(Path.GetTempPath(), $"stammdaten-{Guid.NewGuid():N}.json");

    public void Dispose()
    {
        if (File.Exists(_path))
        {
            File.Delete(_path);
        }
    }

    // A Stammdaten import is outside input: an oversized file is refused before it is parsed, the
    // same cap the Android picker enforces while copying it.
    [Fact]
    public void Read_refuses_a_file_over_the_import_cap_before_parsing_it()
    {
        using (var stream = File.Create(_path))
        {
            stream.SetLength(MasterDataFileService.MaxImportBytes + 1);
        }

        var ex = Assert.Throws<IOException>(() => new MasterDataFileService().Read(_path));

        Assert.Equal(MasterDataFileService.TooLargeMessage, ex.Message);
    }
}
