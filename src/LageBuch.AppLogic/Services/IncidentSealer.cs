using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using LageBuch.Domain;
using LageBuch.Sync;

namespace LageBuch.AppLogic.Services;

/// <summary>
/// Computes and checks the seal of a closed Einsatz (#598). Lives here rather than in the domain
/// because it needs the attachments' bytes, which only the file store can reach.
/// </summary>
/// <remarks>
/// <para>
/// The seal is not a hash of the file: every open rewrites the SQLite file (the migration stamp,
/// the WAL header), so its bytes change while its content does not. The input is a canonical
/// serialization instead, format <see cref="IncidentSeal.CurrentVersion"/>:
/// </para>
/// <list type="number">
/// <item>the line <c>lagebuch-seal-v1</c>;</item>
/// <item>the incident's <see cref="IncidentSnapshot"/> as compact JSON, then a line break;</item>
/// <item>one line <c>{id} {sha256|-}</c> per attachment, ordered by id.</item>
/// </list>
/// <para>
/// The JSON is canonical so that the incident in memory at close and the one read back from
/// SQLite give the same bytes. Four things differ between them without the content differing:
/// a removed Haus leaves the others' <c>Ordinal</c> stale, dwellings come back ordered by floor
/// across houses, timers come back in table order, and dictionaries keep insertion order. So
/// building ordinals are replaced by their list position, dwellings are sorted by (house, floor,
/// apartment), timers by key, and every object's keys are sorted ordinally.
/// </para>
/// <para>
/// Properties holding <c>null</c>, <c>false</c>, <c>0</c> or an empty array or object are left
/// out, and so are the sync counters <c>revision</c> and <c>epoch</c>. A field appended to the
/// snapshot later with such a default therefore leaves every older seal intact. A field whose
/// value for an old incident is anything else must be kept out of the v1 input, or become a new
/// version. The serializer options are a frozen copy of the wire's for the same reason: a change
/// made for the wire must not move a seal printed years ago.
/// </para>
/// </remarks>
public static class IncidentSealer
{
    private static readonly JsonSerializerOptions CanonicalOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    /// <summary>
    /// Seals <paramref name="incident"/> over its content and the given attachment hashes, keyed
    /// by <c>IncidentFile.Id</c>; a missing key or a <c>null</c> hash means the bytes were missing.
    /// </summary>
    public static IncidentSeal Compute(Incident incident, IReadOnlyDictionary<Guid, string?> fileSha256)
    {
        ArgumentNullException.ThrowIfNull(incident);
        ArgumentNullException.ThrowIfNull(fileSha256);
        var input = CanonicalInput(incident, fileSha256);
        var hash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(input)));
        var files = incident.Files.ToDictionary(f => f.Id, f => fileSha256.GetValueOrDefault(f.Id));
        return new IncidentSeal(IncidentSeal.CurrentVersion, hash, files);
    }

    /// <summary>
    /// Checks <paramref name="incident"/>'s stored seal against its content and against the
    /// attachments' hashes as they are on disk now.
    /// </summary>
    public static SealCheck Verify(Incident incident, IReadOnlyDictionary<Guid, string?> currentFileSha256)
    {
        ArgumentNullException.ThrowIfNull(incident);
        ArgumentNullException.ThrowIfNull(currentFileSha256);
        if (incident.Seal is not { } seal)
        {
            return new SealCheck(SealCheckStatus.NotSealed, null);
        }

        if (!string.Equals(seal.Version, IncidentSeal.CurrentVersion, StringComparison.Ordinal))
        {
            return new SealCheck(
                SealCheckStatus.Unverifiable,
                seal.Version.Length == 0 ? "Siegelformat unbekannt" : $"Siegelformat unbekannt: {seal.Version}");
        }

        var problems = new List<string>();

        // Recomputed over the hashes stored with the seal, not the ones on disk now: rewriting an
        // attachment together with its stored hash still has to show up here.
        if (!string.Equals(Compute(incident, seal.FileSha256).Hash, seal.Hash, StringComparison.Ordinal))
        {
            problems.Add("Inhalt weicht vom Siegel ab");
        }

        foreach (var file in incident.Files)
        {
            var sealedSha = seal.FileSha256.GetValueOrDefault(file.Id);
            var currentSha = currentFileSha256.GetValueOrDefault(file.Id);
            if (!string.Equals(sealedSha, currentSha, StringComparison.Ordinal))
            {
                problems.Add(currentSha is null ? $"Anhang fehlt: {file.DisplayName}" : $"Anhang verändert: {file.DisplayName}");
            }
        }

        return problems.Count == 0
            ? new SealCheck(SealCheckStatus.Intact, null)
            : new SealCheck(SealCheckStatus.Broken, string.Join("; ", problems));
    }

    /// <summary>The SHA-256 of a file's bytes as lowercase hex, or <c>null</c> when the file does not exist.</summary>
    public static string? HashFile(string diskPath)
    {
        ArgumentNullException.ThrowIfNull(diskPath);
        if (!File.Exists(diskPath))
        {
            return null;
        }

        using var stream = File.OpenRead(diskPath);
        return Convert.ToHexStringLower(SHA256.HashData(stream));
    }

    /// <inheritdoc cref="HashFile"/>
    public static async Task<string?> HashFileAsync(string diskPath, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(diskPath);
        if (!File.Exists(diskPath))
        {
            return null;
        }

        await using var stream = new FileStream(diskPath, FileMode.Open, FileAccess.Read, FileShare.Read, bufferSize: 81920, useAsync: true);
        return Convert.ToHexStringLower(await SHA256.HashDataAsync(stream, cancellationToken));
    }

    /// <summary>The exact text the v1 hash is taken over; see the remarks on <see cref="IncidentSealer"/>.</summary>
    internal static string CanonicalInput(Incident incident, IReadOnlyDictionary<Guid, string?> fileSha256)
    {
        var snapshot = SnapshotMapper.ToSnapshot(incident);
        snapshot = snapshot with
        {
            Buildings = snapshot.Buildings.Select((b, i) => b with { Ordinal = i }).ToList(),
            Dwellings = snapshot.Dwellings
                .OrderBy(d => d.BuildingId.ToString("D"), StringComparer.Ordinal)
                .ThenBy(d => d.FloorOrdinal)
                .ThenBy(d => d.ApartmentNumber)
                .ToList(),
            Timers = snapshot.Timers.OrderBy(t => t.Key, StringComparer.Ordinal).ToList(),
        };

        var root = Canonicalize(JsonSerializer.SerializeToNode(snapshot, CanonicalOptions));
        if (root is JsonObject rootObject)
        {
            rootObject.Remove("revision");
            rootObject.Remove("epoch");
        }

        var input = new StringBuilder();
        input.Append(IncidentSeal.CurrentVersion).Append('\n');
        input.Append(root?.ToJsonString(CanonicalOptions)).Append('\n');
        foreach (var file in incident.Files.OrderBy(f => f.Id.ToString("D"), StringComparer.Ordinal))
        {
            input.Append(CultureInfo.InvariantCulture, $"{file.Id:D} {fileSha256.GetValueOrDefault(file.Id) ?? "-"}\n");
        }

        return input.ToString();
    }

    private static JsonNode? Canonicalize(JsonNode? node)
    {
        switch (node)
        {
            case JsonObject obj:
                var sorted = new JsonObject();
                foreach (var (key, value) in obj.OrderBy(p => p.Key, StringComparer.Ordinal))
                {
                    var canonical = Canonicalize(value);
                    if (!IsDefault(canonical))
                    {
                        sorted[key] = canonical;
                    }
                }

                return sorted;
            case JsonArray array:
                // Positions matter in an array, so its elements are kept even when they are a
                // default -- only object properties are dropped.
                return new JsonArray(array.Select(Canonicalize).ToArray());
            default:
                return node?.DeepClone();
        }
    }

    private static bool IsDefault(JsonNode? node) => node switch
    {
        null => true,
        JsonObject obj => obj.Count == 0,
        JsonArray array => array.Count == 0,
        _ => node.GetValueKind() switch
        {
            JsonValueKind.Null or JsonValueKind.False => true,
            JsonValueKind.Number => string.Equals(node.ToJsonString(), "0", StringComparison.Ordinal),
            _ => false,
        },
    };
}

/// <summary>What opening a closed incident found about its seal.</summary>
public enum SealCheckStatus
{
    /// <summary>Closed before seals existed. Not a warning.</summary>
    NotSealed,

    /// <summary>Content and attachments match the seal.</summary>
    Intact,

    /// <summary>The content or an attachment no longer matches the seal.</summary>
    Broken,

    /// <summary>The seal could not be checked: an unknown format, or an attachment that could not be read.</summary>
    Unverifiable,
}

/// <summary>The outcome of a seal check, with a short German reason when it is not intact.</summary>
public sealed record SealCheck(SealCheckStatus Status, string? Detail);
