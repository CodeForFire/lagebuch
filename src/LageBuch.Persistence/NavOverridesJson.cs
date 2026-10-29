using System.Text.Json;

namespace LageBuch.Persistence;

/// <summary>
/// Reads and writes an Einsatz's rail overrides as the JSON object stored in
/// <c>incident_meta.nav_overrides</c>. The file may be hostile, so reading never throws.
/// </summary>
internal static class NavOverridesJson
{
    internal const int MaxLength = 4096;

    internal static string? Write(IReadOnlyDictionary<string, bool> overrides)
    {
        ArgumentNullException.ThrowIfNull(overrides);
        return overrides.Count == 0 ? null : JsonSerializer.Serialize(overrides);
    }

    internal static IReadOnlyDictionary<string, bool> Read(string? json)
    {
        var result = new Dictionary<string, bool>(StringComparer.Ordinal);
        if (string.IsNullOrEmpty(json) || json.Length > MaxLength)
        {
            return result;
        }

        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Object)
            {
                return result;
            }

            foreach (var property in doc.RootElement.EnumerateObject())
            {
                if (property.Value.ValueKind is JsonValueKind.True or JsonValueKind.False)
                {
                    result[property.Name] = property.Value.GetBoolean();
                }
            }
        }
        catch (JsonException)
        {
            return new Dictionary<string, bool>(StringComparer.Ordinal);
        }

        return result;
    }
}
