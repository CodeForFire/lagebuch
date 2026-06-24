using System.Text.Json;

namespace Feuerwehr.Persistence.MasterData;

public sealed record Street(string Name, string District);

public sealed record MasterDataSet(
    IReadOnlyList<string> Roles,
    IReadOnlyList<string> Status,
    IReadOnlyList<string> Equipment,
    IReadOnlyList<string> Districts,
    IReadOnlyList<string> RadioCallSigns,
    IReadOnlyList<Street> Streets,
    IReadOnlyList<string> ChecklistTemplate,
    IReadOnlyList<string> TruppTypes);

public static class MasterDataDefaults
{
    public static MasterDataSet LoadEmbedded()
    {
        var asm = typeof(MasterDataDefaults).Assembly;
        var resourceName = asm.GetManifestResourceNames()
            .Single(n => n.EndsWith("master-data.json", StringComparison.Ordinal));
        using var stream = asm.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException("Embedded master-data.json not found.");
        using var doc = JsonDocument.Parse(stream);
        var root = doc.RootElement;

        static IReadOnlyList<string> Arr(JsonElement e, string prop) =>
            e.GetProperty(prop).EnumerateArray().Select(x => x.GetString()!).ToList();

        var streets = root.GetProperty("streets").EnumerateArray()
            .Select(s => new Street(s.GetProperty("name").GetString()!, s.GetProperty("district").GetString() ?? string.Empty))
            .ToList();

        return new MasterDataSet(
            Arr(root, "roles"),
            Arr(root, "status"),
            Arr(root, "equipment"),
            Arr(root, "districts"),
            Arr(root, "radioCallSigns"),
            streets,
            Arr(root, "checklistTemplate"),
            Arr(root, "truppTypes"));
    }
}
