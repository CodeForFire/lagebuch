using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using LageBuch.Persistence.MasterData;

namespace LageBuch.Osm;

/// <summary>
/// A Gemeinde's Ortsteile and Straßen as <see cref="StreetExtractor"/> read them from OpenStreetMap,
/// in the shape the Stammdaten hold them (#461).
/// </summary>
public sealed record StreetList(IReadOnlyList<string> Districts, IReadOnlyList<Street> Streets)
{
    // The formatting MasterDataJson.Serialize writes a Stammdaten file in, so a fragment pasted
    // into one reads the same as the rest of it.
    private static readonly JsonSerializerOptions FragmentOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    /// <summary>
    /// <paramref name="set"/> with its Ortsteile and Straßen replaced by these, and every other
    /// category left exactly as it was.
    /// </summary>
    public MasterDataSet ApplyTo(MasterDataSet set)
    {
        ArgumentNullException.ThrowIfNull(set);
        return set with { Districts = Districts, Streets = Streets };
    }

    /// <summary>
    /// Just the <c>districts</c> and <c>streets</c> keys of the Stammdaten file format. Cut out of
    /// a full <see cref="MasterDataJson.Serialize"/> rather than written again, so the two can
    /// never disagree on a field name.
    /// </summary>
    public string ToJsonFragment() => Fragment().ToJsonString(FragmentOptions) + "\n";

    /// <summary>
    /// <paramref name="stammdatenJson"/>, a Stammdaten file, with its <c>districts</c> and
    /// <c>streets</c> replaced and everything else left as it was: keys this build does not know,
    /// a <c>_comment</c>, values in an older shape. Absent keys are added after the Stichwörter,
    /// where an export writes them.
    /// </summary>
    /// <exception cref="JsonException">The text is not a Stammdaten file.</exception>
    public string MergeInto(string stammdatenJson)
    {
        ArgumentNullException.ThrowIfNull(stammdatenJson);
        if (JsonNode.Parse(stammdatenJson) is not JsonObject root)
        {
            throw new JsonException("Eine Stammdaten-Datei ist ein JSON-Objekt.");
        }

        var insertAt = root.IndexOf("keywords") is >= 0 and var keywords ? keywords + 1 : root.Count;
        foreach (var (key, value) in Fragment().Select(p => (p.Key, p.Value?.DeepClone())).ToList())
        {
            if (root.ContainsKey(key))
            {
                root[key] = value;
            }
            else
            {
                root.Insert(insertAt++, key, value);
            }
        }

        var merged = root.ToJsonString(FragmentOptions) + "\n";
        try
        {
            _ = MasterDataJson.Parse(merged);
        }
        catch (KeyNotFoundException e)
        {
            throw new JsonException($"Keine gültige Stammdaten-Datei: {e.Message}", e);
        }
        catch (InvalidOperationException e)
        {
            throw new JsonException($"Keine gültige Stammdaten-Datei: {e.Message}", e);
        }

        return merged;
    }

    private JsonObject Fragment()
    {
        var full = JsonNode.Parse(MasterDataJson.Serialize(ApplyTo(MasterDataSet.Empty)))?.AsObject()
            ?? throw new InvalidOperationException("MasterDataJson.Serialize wrote no object.");
        return new JsonObject
        {
            ["districts"] = full["districts"]?.DeepClone(),
            ["streets"] = full["streets"]?.DeepClone(),
        };
    }
}
