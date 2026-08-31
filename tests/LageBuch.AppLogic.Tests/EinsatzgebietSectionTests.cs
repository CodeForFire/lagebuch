using LageBuch.AppLogic.Services;
using LageBuch.AppLogic.ViewModels;
using LageBuch.Persistence.MasterData;

namespace LageBuch.AppLogic.Tests;

public class EinsatzgebietSectionTests : IDisposable
{
    private readonly List<string> _tempDirs = new();

    public void Dispose()
    {
        foreach (var dir in _tempDirs)
            if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
    }

    private string NewTempDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"einsatzgebiet-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        _tempDirs.Add(dir);
        return dir;
    }

    private static readonly RegionPackInfo Ffb = new(
        "Landkreis Fürstenfeldbruck", "ffb", "https://example.org/ffb.zip", 42,
        48.08, 10.99, 48.29, 11.41, "2026-09-01", "© OpenStreetMap contributors (ODbL)");

    private sealed class FakeCatalog(IReadOnlyList<RegionPackInfo> regions) : IRegionPackCatalogService
    {
        public Task<IReadOnlyList<RegionPackInfo>> GetAvailableRegionsAsync(CancellationToken ct = default)
            => Task.FromResult(regions);
    }

    private sealed class FakeInstaller(string installedFolder) : IRegionPackInstaller
    {
        public RegionPackInfo? LastInstalled { get; private set; }

        public Task<string> DownloadAndInstallAsync(RegionPackInfo pack, IProgress<double>? progress, CancellationToken ct = default)
        {
            LastInstalled = pack;
            progress?.Report(0.5);
            progress?.Report(1.0);
            return Task.FromResult(installedFolder);
        }
    }

    private static EinsatzgebietSection NewSection(
        IRegionPackCatalogService? catalog = null, IRegionPackInstaller? installer = null, Action? onChanged = null) =>
        new("Einsatzgebiet", Einsatzgebiet.Empty,
            onChanged ?? (() => { }),
            catalog ?? new FakeCatalog(Array.Empty<RegionPackInfo>()),
            installer ?? new FakeInstaller("/tmp/unused"));

    [Fact]
    public async Task LoadCatalogCommand_populates_AvailableRegions_on_success()
    {
        var section = NewSection(catalog: new FakeCatalog(new[] { Ffb }));

        await section.LoadCatalogCommand.ExecuteAsync(null);

        Assert.Equal("ffb", Assert.Single(section.AvailableRegions).Slug);
    }

    [Fact]
    public async Task LoadCatalogCommand_leaves_the_list_empty_and_sets_a_quiet_status_when_nothing_is_returned()
    {
        var section = NewSection(catalog: new FakeCatalog(Array.Empty<RegionPackInfo>()));

        await section.LoadCatalogCommand.ExecuteAsync(null);

        Assert.Empty(section.AvailableRegions);
        Assert.NotNull(section.CatalogStatus);
    }

    [Fact]
    public async Task DownloadSelectedRegionCommand_installs_the_selected_region_and_sets_name_and_folder()
    {
        var installedFolder = NewTempDir();
        var installer = new FakeInstaller(installedFolder);
        var section = NewSection(installer: installer);
        section.SelectedRegion = Ffb;

        await section.DownloadSelectedRegionCommand.ExecuteAsync(null);

        Assert.Equal(Ffb, installer.LastInstalled);
        Assert.Equal("Landkreis Fürstenfeldbruck", section.Name);
        Assert.Equal(installedFolder, section.FolderPath);
    }

    [Fact]
    public async Task DownloadSelectedRegionCommand_fires_the_dirty_callback()
    {
        var changedCount = 0;
        var section = NewSection(installer: new FakeInstaller(NewTempDir()), onChanged: () => changedCount++);
        section.SelectedRegion = Ffb;

        await section.DownloadSelectedRegionCommand.ExecuteAsync(null);

        Assert.True(changedCount > 0);
    }

    [Fact]
    public void DownloadSelectedRegionCommand_cannot_execute_without_a_selected_region()
    {
        var section = NewSection();

        Assert.False(section.DownloadSelectedRegionCommand.CanExecute(null));
    }

    [Fact]
    public async Task DownloadSelectedRegionCommand_reports_progress_up_to_one()
    {
        var section = NewSection(installer: new FakeInstaller(NewTempDir()));
        section.SelectedRegion = Ffb;

        await section.DownloadSelectedRegionCommand.ExecuteAsync(null);

        Assert.Equal(1.0, section.DownloadProgress);
    }

    // --- File-presence validation: is region.mbtiles/region.dem actually at FolderPath? ---

    [Fact]
    public void KartendatenStatus_is_null_when_folder_path_is_empty()
    {
        var section = NewSection();

        Assert.Null(section.KartendatenStatus);
        Assert.False(section.KartendatenGefunden);
    }

    [Fact]
    public void KartendatenStatus_reports_both_files_missing()
    {
        var section = NewSection();
        section.FolderPath = NewTempDir();

        Assert.False(section.KartendatenGefunden);
        Assert.Contains("region.mbtiles", section.KartendatenStatus);
        Assert.Contains("region.dem", section.KartendatenStatus);
    }

    [Fact]
    public void KartendatenStatus_names_only_the_missing_file_when_one_is_present()
    {
        var section = NewSection();
        var dir = NewTempDir();
        File.WriteAllText(Path.Combine(dir, "region.mbtiles"), "x");
        section.FolderPath = dir;

        Assert.False(section.KartendatenGefunden);
        Assert.DoesNotContain("region.mbtiles", section.KartendatenStatus);
        Assert.Contains("region.dem", section.KartendatenStatus);
    }

    [Fact]
    public void KartendatenStatus_is_positive_when_both_files_are_present()
    {
        var section = NewSection();
        var dir = NewTempDir();
        File.WriteAllText(Path.Combine(dir, "region.mbtiles"), "x");
        File.WriteAllText(Path.Combine(dir, "region.dem"), "x");
        section.FolderPath = dir;

        Assert.True(section.KartendatenGefunden);
        Assert.NotNull(section.KartendatenStatus);
    }
}
