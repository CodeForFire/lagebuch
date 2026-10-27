namespace LageBuch.Persistence.Tests;

public sealed class PrivateTempFolderTests : IDisposable
{
    private readonly string _dir = Path.Join(Path.GetTempPath(), $"private-temp-{Guid.NewGuid():N}");

    public void Dispose()
    {
        if (Directory.Exists(_dir))
        {
            Directory.Delete(_dir, recursive: true);
        }
    }

    // The folder one "Öffnen" copies into is created with Create alone, never tightened later,
    // so its mode has to be right from the start.
    [Fact]
    public void Create_makes_a_folder_only_its_owner_can_enter()
    {
        if (OperatingSystem.IsWindows())
        {
            return; // The per-user temp directory's ACL keeps other users out there.
        }

        PrivateTempFolder.Create(_dir);

        Assert.Equal(PrivateTempFolder.OwnerOnly, File.GetUnixFileMode(_dir));
    }
}
