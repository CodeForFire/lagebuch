namespace LageBuch.AppLogic.Services;

/// <summary>The Fachberater the Einsatzleitung commonly calls in (FwDV 100 Anlage 3), as Funktion names.</summary>
public static class FachberaterSuggestions
{
    private static readonly string[] Fields =
    [
        "THW",
        "Polizei",
        "Rettungsdienst",
        "Stadtwerke",
        "Gasversorger",
        "Stromversorger",
        "Wasserversorger",
        "Notfallseelsorge",
        "Gesundheitsamt",
        "Umweltamt",
        "Ordnungsamt",
        "Deutsche Bahn",
        "Bundeswehr",
        "Chemiker",
        "Arzt",
    ];

    public static IReadOnlyList<string> Roles { get; } = Fields.Select(f => $"Fachberater {f}").ToList();
}
