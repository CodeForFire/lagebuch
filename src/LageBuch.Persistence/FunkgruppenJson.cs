using System.Text.Json;
using LageBuch.Domain.Leadership;

namespace LageBuch.Persistence;

/// <summary>
/// Reads and writes an Einsatzleitung's Funkgruppen as the JSON object stored in
/// <c>incident_meta.el_funkgruppen</c> and <c>command_nodes.funkgruppen</c>:
/// <c>{"tmo":[…],"dmo":[…]}</c>. The file may be hostile, so reading never throws: a malformed value
/// reads as none, and what <see cref="Funkgruppen.FromRecord"/> would not keep is dropped.
/// </summary>
internal static class FunkgruppenJson
{
    // Covers the largest object the writer can produce: 2 × MaxPerKind entries of MaxTextLength chars,
    // each of which JSON may escape to six chars (\uXXXX), plus quotes and commas, plus the keys.
    internal const int MaxLength = (2 * Funkgruppen.MaxPerKind * ((CommandNode.MaxTextLength * 6) + 3)) + 32;

    internal static string? Write(Funkgruppen funkgruppen)
    {
        ArgumentNullException.ThrowIfNull(funkgruppen);
        return funkgruppen.IsEmpty ? null : JsonSerializer.Serialize(new { tmo = funkgruppen.Tmo, dmo = funkgruppen.Dmo });
    }

    internal static Funkgruppen Read(string? json)
    {
        if (string.IsNullOrEmpty(json) || json.Length > MaxLength)
        {
            return Funkgruppen.Empty;
        }

        try
        {
            using var doc = JsonDocument.Parse(json);
            return doc.RootElement.ValueKind == JsonValueKind.Object
                ? Funkgruppen.FromRecord(Kind(doc.RootElement, "tmo"), Kind(doc.RootElement, "dmo"))
                : Funkgruppen.Empty;
        }
        catch (JsonException)
        {
            return Funkgruppen.Empty;
        }
    }

    private static string?[] Kind(JsonElement root, string name) =>
        root.TryGetProperty(name, out var array) && array.ValueKind == JsonValueKind.Array
            ? [.. array.EnumerateArray().Where(e => e.ValueKind == JsonValueKind.String).Select(e => e.GetString())]
            : [];
}
