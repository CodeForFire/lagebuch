using LageBuch.Domain;

namespace LageBuch.AppLogic.ViewModels;

/// <summary>
/// The entries of a Fahrzeugtyp picker: one for none, then the catalogue's codes. The Kräfte grid
/// and the Kräfte form share it, so both offer the same list (#886).
/// </summary>
public static class VehicleTypeChoices
{
    /// <summary>The picker's entry for a unit without a Fahrzeugtyp: the bare Kraftfahrzeug sign.</summary>
    public const string None = "ohne Typ";

    /// <summary>
    /// The entries for a unit that records <paramref name="recorded"/>. A type the catalogue lacks
    /// -- an old file, another Feuerwehr's Stammdaten -- is offered too, so the picker shows what is
    /// recorded instead of going blank.
    /// </summary>
    public static IReadOnlyList<string> For(string? recorded)
    {
        var codes = VehicleTypes.All.Select(t => t.Code).ToList();
        if (recorded is not null && VehicleTypes.Find(recorded) is null)
        {
            codes.Insert(0, recorded);
        }

        return [None, .. codes];
    }

    /// <summary>The picker's entry for a recorded Fahrzeugtyp; <see cref="None"/> for null.</summary>
    public static string Of(string? vehicleType) => vehicleType ?? None;

    /// <summary>What a picked entry records: null for <see cref="None"/> or no pick.</summary>
    public static string? ToStored(string? choice) => choice is null or None ? null : choice;
}
