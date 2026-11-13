using System.Globalization;
using LageBuch.Domain;

namespace LageBuch.AppLogic.ViewModels;

/// <summary>
/// The entries of a Fahrzeugtyp picker: one for none, then the catalogue's codes alphabetically.
/// The Kräfte grid and the Kräfte form share it, so both offer the same list (#886).
/// </summary>
public static class VehicleTypeChoices
{
    /// <summary>The picker's entry for a unit without a Fahrzeugtyp: the bare Kraftfahrzeug sign.</summary>
    public const string None = "ohne Typ";

    // German order, as the Geräte use (#846): a person looks a code up in it, and it is read in
    // the fixed culture Formatting applies, numbers by value, so "ELW 1" stays before "ELW 2".
    private static readonly StringComparer Alphabetical = StringComparer.Create(
        CultureInfo.GetCultureInfo("de-DE"), CompareOptions.IgnoreCase | CompareOptions.NumericOrdering);

    /// <summary>The catalogue's codes, alphabetically: what every Fahrzeugtyp picker offers.</summary>
    public static IReadOnlyList<string> Catalogue { get; } = [.. VehicleTypes.All.Select(t => t.Code).Order(Alphabetical)];

    /// <summary>
    /// The entries for a unit that records <paramref name="recorded"/>. A type the catalogue lacks
    /// -- an old file, another Feuerwehr's Stammdaten -- is offered too, so the picker shows what is
    /// recorded instead of going blank; it comes right after <see cref="None"/>, ahead of the
    /// catalogue.
    /// </summary>
    public static IReadOnlyList<string> For(string? recorded) =>
        recorded is not null && VehicleTypes.Find(recorded) is null
            ? [None, recorded, .. Catalogue]
            : [None, .. Catalogue];

    /// <summary>
    /// The picker's entry for a recorded Fahrzeugtyp: the catalogue's code when it matches in
    /// another case ("dlk" shows as "DLK", the entry <see cref="For"/> offers), the recorded
    /// spelling when the catalogue lacks it, and <see cref="None"/> for null.
    /// </summary>
    public static string Of(string? vehicleType) =>
        VehicleTypes.Find(vehicleType)?.Code ?? vehicleType ?? None;

    /// <summary>What a picked entry records: null for <see cref="None"/> or no pick.</summary>
    public static string? ToStored(string? choice) => choice is null or None ? null : choice;
}
