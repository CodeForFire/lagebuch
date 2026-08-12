using System.Text.Json;
using System.Text.Json.Serialization;

namespace Feuerwehr.Sync;

/// <summary>
/// The single shared serializer configuration for everything on the wire (snapshots and commands),
/// so host and client agree byte-for-byte. Enums travel as strings — a stable, readable contract
/// that survives reordering (the SQLite layer keeps its own ordinal contract independently).
/// </summary>
public static class SyncJson
{
    public static JsonSerializerOptions Options { get; } = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    public static string Serialize<T>(T value) => JsonSerializer.Serialize(value, Options);

    public static T Deserialize<T>(string json) =>
        JsonSerializer.Deserialize<T>(json, Options)
        ?? throw new JsonException($"Deserialized null for {typeof(T).Name}.");
}
