using LageBuch.Persistence.MasterData;

namespace LageBuch.AppLogic.Services;

/// <summary>
/// The rail labels of the built-in modules, authored upper-case as the rail has always shown
/// them. Kept beside the module keys rather than in <see cref="NavModules"/>: the keys are
/// persisted Stammdaten, these are German display strings.
/// </summary>
public static class ModuleLabels
{
    private static readonly Dictionary<string, string> Headers =
        new(StringComparer.Ordinal)
        {
            [NavModules.Etb] = "ETB",
            [NavModules.Tasks] = "AUFGABEN",
            [NavModules.Roles] = "FUNKTIONEN",
            [NavModules.Forces] = "KRÄFTE",
            [NavModules.InvolvedParties] = "BETEILIGTE",
            [NavModules.Scba] = "ATEMSCHUTZ",
            [NavModules.Co] = "CO-MESSUNG",
            [NavModules.Files] = "DATEIEN",
            [NavModules.Links] = "LINKS",
            [NavModules.Contacts] = "KONTAKTE",
        };

    /// <summary>The rail label of a built-in module.</summary>
    public static string Header(string moduleKey) => Headers[moduleKey];
}
