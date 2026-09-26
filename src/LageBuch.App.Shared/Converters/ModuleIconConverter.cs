using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;
using LageBuch.Persistence.MasterData;

namespace LageBuch.App.Shared.Converters;

/// <summary>
/// The glyph for one rail entry, keyed by its <see cref="NavModules"/> key.
/// </summary>
/// <remarks>
/// Only the narrow layout's bottom bar uses this. The wide rail has room for a whole word and is
/// better off with one — an icon there would be decoration, and a Kommandant reads "ATEMSCHUTZ"
/// faster than they decode a mask. In a 82dp bottom-bar cell the word alone is 10px type, so the
/// glyph is what carries recognition at a glance and the word confirms it.
/// <para>
/// Same family as every other icon in this app: Material's outlined set, the geometry pasted
/// inline exactly as <c>meldung-icon</c> and the row actions do it. Atemschutz and Aufgaben
/// deliberately reuse the very paths their header Meldung bars already use, so the bar that warns
/// and the tab it jumps to are visibly the same thing. A Checkliste is named by whoever wrote it,
/// so it can only have the generic list glyph.
/// </para>
/// </remarks>
public sealed class ModuleIconConverter : IValueConverter
{
    public static readonly ModuleIconConverter Instance = new();

    // Einsatztagebuch: a written page.
    private const string EtbPath =
        "M14 2H6c-1.1 0-1.99.9-1.99 2L4 20c0 1.1.89 2 1.99 2H18c1.1 0 2-.9 2-2V8l-6-6zm2 16H8v-2h8v2zm0-4H8v-2h8v2zm-3-5V3.5L18.5 9H13z";

    // Aufgaben: the clipboard from TaskDueBar.
    private const string TasksPath =
        "M19 3h-4.18C14.4 1.84 13.3 1 12 1c-1.3 0-2.4.84-2.82 2H5c-1.1 0-2 .9-2 2v14c0 1.1.9 2 2 2h14c1.1 0 2-.9 2-2V5c0-1.1-.9-2-2-2zm-6 15h-2v-2h2v2zm0-4h-2V8h2v6zm-1-9c-.55 0-1-.45-1-1s.45-1 1-1 1 .45 1 1-.45 1-1 1z";

    // Funktionen: a person with a badge.
    private const string RolesPath =
        "M12 12c2.21 0 4-1.79 4-4s-1.79-4-4-4-4 1.79-4 4 1.79 4 4 4zm0 2c-2.67 0-8 1.34-8 4v2h16v-2c0-2.66-5.33-4-8-4z";

    // Kräfte: a vehicle.
    private const string ForcesPath =
        "M18.92 6.01C18.72 5.42 18.16 5 17.5 5h-11c-.66 0-1.21.42-1.42 1.01L3 12v8c0 .55.45 1 1 1h1c.55 0 1-.45 1-1v-1h12v1c0 .55.45 1 1 1h1c.55 0 1-.45 1-1v-8l-2.08-5.99zM6.5 16c-.83 0-1.5-.67-1.5-1.5S5.67 13 6.5 13s1.5.67 1.5 1.5S7.33 16 6.5 16zm11 0c-.83 0-1.5-.67-1.5-1.5s.67-1.5 1.5-1.5 1.5.67 1.5 1.5-.67 1.5-1.5 1.5zM5 11l1.5-4.5h11L19 11H5z";

    // Atemschutz: the stopwatch from ScbaControlBar — the Druckabfrage clock.
    private const string ScbaPath =
        "M15 1H9v2h6V1zm-4 13h2V8h-2v6zm8.03-6.61 1.42-1.42c-.43-.51-.9-.99-1.41-1.41l-1.42 1.42A8.962 8.962 0 0 0 12 4c-4.97 0-9 4.03-9 9s4.02 9 9 9a9 9 0 0 0 7.03-14.61zM12 20c-3.87 0-7-3.13-7-7s3.13-7 7-7 7 3.13 7 7-3.13 7-7 7z";

    // CO-Messung: a gauge.
    private const string CoPath =
        "M12 2C6.48 2 2 6.48 2 12s4.48 10 10 10 10-4.48 10-10S17.52 2 12 2zm0 18c-4.41 0-8-3.59-8-8s3.59-8 8-8 8 3.59 8 8-3.59 8-8 8zm1-13h-2v5.41l3.79 3.8 1.42-1.42L13 11.59V7z";

    // Dateien: a folder.
    private const string FilesPath =
        "M10 4H4c-1.1 0-1.99.9-1.99 2L2 18c0 1.1.9 2 2 2h16c1.1 0 2-.9 2-2V8c0-1.1-.9-2-2-2h-8l-2-2z";

    // Links: a chain.
    private const string LinksPath =
        "M3.9 12c0-1.71 1.39-3.1 3.1-3.1h4V7H7c-2.76 0-5 2.24-5 5s2.24 5 5 5h4v-1.9H7c-1.71 0-3.1-1.39-3.1-3.1zM8 13h8v-2H8v2zm9-6h-4v1.9h4c1.71 0 3.1 1.39 3.1 3.1s-1.39 3.1-3.1 3.1h-4V17h4c2.76 0 5-2.24 5-5s-2.24-5-5-5z";

    // A Checkliste, whatever its owner called it: a ticked list.
    private const string ChecklistPath =
        "M22 5.18 10.59 16.6l-4.24-4.24 1.41-1.41 2.83 2.83 10-10L22 5.18zM19.79 10.22C19.92 10.79 20 11.39 20 12c0 4.42-3.58 8-8 8s-8-3.58-8-8 3.58-8 8-8c1.58 0 3.04.46 4.28 1.25l1.44-1.44C16.1 2.67 14.13 2 12 2 6.48 2 2 6.48 2 12s4.48 10 10 10 10-4.48 10-10c0-1.19-.22-2.33-.6-3.39l-1.61 1.61z";

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var path = (value as string) switch
        {
            NavModules.Etb => EtbPath,
            NavModules.Tasks => TasksPath,
            NavModules.Roles => RolesPath,
            NavModules.Forces => ForcesPath,
            NavModules.Scba => ScbaPath,
            NavModules.Co => CoPath,
            NavModules.Files => FilesPath,
            NavModules.Links => LinksPath,

            // Checklist, and any key a newer build wrote that this one does not know.
            _ => ChecklistPath,
        };

        return Geometry.Parse(path);
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
