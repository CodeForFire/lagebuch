using LageBuch.AppLogic.Services.Map;
using LageBuch.Persistence.Map;

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

    /// <summary>Every pack's water points; a pack missing here has none.</summary>
    public Dictionary<string, IReadOnlyList<WaterPoint>> WaterPoints { get; } = new(StringComparer.Ordinal);

    /// <summary>When set, reading water points fails with it.</summary>
    public RegionPackException? WaterFailure { get; set; }

    /// <summary>Every pack's addresses; a search returns those whose street starts with the query's first word.</summary>
    public Dictionary<string, IReadOnlyList<AddressHit>> Addresses { get; } = new(StringComparer.Ordinal);

    public List<string> Searches { get; } = new();

    public static RegionPackInfo Pack(string slug, string name, int version = 1) =>
        new(name, slug, version, $"https://example.org/{slug}.zip", 16 * 1024 * 1024, new string('a', 64), 10.99, 48.08, 11.41, 48.29, 11, 17, "2026-10-27T20:15:49Z", "© OpenStreetMap contributors");

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

    public Task<IReadOnlyList<WaterPoint>> GetWaterPointsAsync(InstalledRegionPack pack, CancellationToken cancellationToken = default) =>
        WaterFailure is { } failure
            ? Task.FromException<IReadOnlyList<WaterPoint>>(failure)
            : Task.FromResult(WaterPoints.GetValueOrDefault(pack.Info.Slug) ?? []);

    public Task<IReadOnlyList<AddressHit>> SearchAddressesAsync(InstalledRegionPack pack, string query, CancellationToken cancellationToken = default)
    {
        Searches.Add($"{pack.Info.Slug}:{query}");
        var first = query.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? string.Empty;
        IReadOnlyList<AddressHit> hits = (Addresses.GetValueOrDefault(pack.Info.Slug) ?? [])
            .Where(a => first.Length > 0 && a.Street.StartsWith(first, StringComparison.Ordinal))
            .ToList();
        return Task.FromResult(hits);
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
