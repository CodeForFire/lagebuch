namespace Feuerwehr.App.Tests;

public class AppPathsTests : IDisposable
{
    private readonly string _base = Path.Combine(Path.GetTempPath(), $"lb-{Guid.NewGuid():N}");

    public void Dispose()
    {
        if (Directory.Exists(_base)) Directory.Delete(_base, recursive: true);
    }

    [Fact]
    public void GetAppDataDir_creates_lagebuch_subdir()
    {
        var dir = AppPaths.GetAppDataDir(_base);
        Assert.True(Directory.Exists(dir));
        Assert.EndsWith("Lagebuch", dir.TrimEnd(Path.DirectorySeparatorChar));
    }

    [Fact]
    public void GetAppDataDir_is_idempotent()
    {
        var a = AppPaths.GetAppDataDir(_base);
        var b = AppPaths.GetAppDataDir(_base);
        Assert.Equal(a, b);
        Assert.True(Directory.Exists(b));
    }
}
