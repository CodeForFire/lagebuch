using LageBuch.AppLogic.Services.Map;

namespace LageBuch.AppLogic.Tests;

/// <summary>An in-memory Kartenpaket library; nothing is downloaded and no folder is real.</summary>
internal sealed class FakeRegionPackLibrary : IRegionPackLibrary
{
    private readonly List<InstalledRegionPack> _installed = new();

    public IReadOnlyList<RegionPackInfo> Catalogue { get; set; } = [];

    public RegionPackException? Failure { get; set; }

    /// <summary>When set, an install waits for it, so a test can look at the section mid-download.</summary>
    public TaskCompletionSource? InstallGate { get; set; }

    /// <summary>When set, fetching the catalogue waits for it.</summary>
    public TaskCompletionSource? CatalogGate { get; set; }

    /// <summary>Called as a removal starts, so a test can look at the section at that moment.</summary>
    public Action? Removing { get; set; }

    public int InstallCount { get; private set; }

    public static RegionPackInfo Pack(string slug, string name, string builtAt = "2026-08-31") =>
        new(name, slug, $"https://example.org/{slug}.zip", 16 * 1024 * 1024, 48.08, 10.99, 48.29, 11.41, builtAt, "© OpenStreetMap contributors");

    public void Preinstall(RegionPackInfo info) => _installed.Add(new InstalledRegionPack(info, Path.Join("regions", info.Slug)));

    public async Task<IReadOnlyList<RegionPackInfo>> GetCatalogAsync(CancellationToken cancellationToken = default)
    {
        if (CatalogGate is { } gate)
        {
            await gate.Task;
        }

        return Failure is { } failure ? throw failure : Catalogue;
    }

    public IReadOnlyList<InstalledRegionPack> GetInstalled() => _installed.ToList();

    public async Task<InstalledRegionPack> InstallAsync(RegionPackInfo pack, IProgress<double>? progress = null, CancellationToken cancellationToken = default)
    {
        InstallCount++;
        progress?.Report(0.5);
        if (InstallGate is { } gate)
        {
            await gate.Task;
        }

        if (Failure is { } failure)
        {
            throw failure;
        }

        _installed.RemoveAll(p => p.Info.Slug == pack.Slug);
        var installed = new InstalledRegionPack(pack, Path.Join("regions", pack.Slug));
        _installed.Add(installed);
        return installed;
    }

    public void Remove(InstalledRegionPack pack)
    {
        Removing?.Invoke();
        if (Failure is { } failure)
        {
            throw failure;
        }

        _installed.RemoveAll(p => p.Info.Slug == pack.Info.Slug);
    }
}
