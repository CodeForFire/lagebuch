using System.Text.Json;

namespace LageBuch.Osm.Tests;

/// <summary>
/// Builds Overpass answers in the shape <c>out tags;</c> returns, so a test states only the
/// elements it is about. Names are real Fürstenfeldbruck streets and places: OpenStreetMap data,
/// public, and plausible together.
/// </summary>
internal static class Overpass
{
    private static long _id = 1000;

    public static string Answer(params object[] elements) =>
        JsonSerializer.Serialize(new { version = 0.6, elements });

    public static string AnswerWithRemark(string remark, params object[] elements) =>
        JsonSerializer.Serialize(new { version = 0.6, remark, elements });

    public static object Gemeinde(string name = "Fürstenfeldbruck") =>
        Element("relation", new()
        {
            ["boundary"] = "administrative",
            ["admin_level"] = "8",
            ["name"] = name,
            ["de:amtlicher_gemeindeschluessel"] = "09179121",
        });

    public static object Road(string name, string highway = "residential") =>
        Element("way", new() { ["highway"] = highway, ["name"] = name });

    public static object Place(string name, string place = "village") =>
        Element("node", new() { ["place"] = place, ["name"] = name });

    public static object Address(
        string street,
        string? postcode = "82256",
        string? city = "Fürstenfeldbruck",
        string? suburb = null,
        string? hamlet = null) =>
        Element("node", Tags(("addr:street", street), ("addr:housenumber", "1"), ("addr:postcode", postcode), ("addr:city", city), ("addr:suburb", suburb), ("addr:hamlet", hamlet)));

    public static object PlaceAddress(string place, string? postcode = "82256", string? city = "Fürstenfeldbruck") =>
        Element("node", Tags(("addr:place", place), ("addr:housenumber", "1"), ("addr:postcode", postcode), ("addr:city", city)));

    private static Dictionary<string, string> Tags(params (string Key, string? Value)[] tags) =>
        tags.Where(t => t.Value is not null).ToDictionary(t => t.Key, t => t.Value ?? string.Empty);

    private static object Element(string type, Dictionary<string, string> tags) =>
        new { type, id = Interlocked.Increment(ref _id), tags };
}
