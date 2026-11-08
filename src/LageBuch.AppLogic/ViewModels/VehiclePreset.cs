using LageBuch.Persistence.MasterData;

namespace LageBuch.AppLogic.ViewModels;

/// <summary>
/// What a Stammdaten vehicle brings into the Kräfte when it is recorded: the Stärke its seats
/// suggest and its Fahrzeugtyp. Shared by the Kräfte form and the alarm import, so a vehicle comes
/// in the same way whichever of the two records it.
/// </summary>
/// <param name="Zugfuehrer">1 when the vehicle carries a Zugführer, else 0.</param>
/// <param name="Officers">The Führungskraft: one, while a seat is left for it.</param>
/// <param name="Mannschaft">The remaining seats.</param>
/// <param name="VehicleType">The catalogue's code when it knows the type, the Stammdaten's text when it does not.</param>
public sealed record VehiclePreset(int Zugfuehrer, int Officers, int Mannschaft, string? VehicleType)
{
    public int Strength => Zugfuehrer + Officers + Mannschaft;

    public static VehiclePreset Of(Vehicle vehicle)
    {
        ArgumentNullException.ThrowIfNull(vehicle);

        // Sitzplätze-Vorbelegung: 9 Sitze ergeben 1 Führungskraft + 8 Mannschaft (#76). The
        // Zugführer occupies one of the vehicle's seats, so it comes out of the same pool instead
        // of being added on top (#260).
        var zugfuehrer = vehicle.HasZugfuehrer ? 1 : 0;
        var remainingSeats = Math.Max(vehicle.Seats - zugfuehrer, 0);
        var vehicleType = Domain.VehicleTypes.Find(vehicle.VehicleType)?.Code ?? Domain.VehicleTypes.FromStored(vehicle.VehicleType);
        return new VehiclePreset(zugfuehrer, Math.Min(1, remainingSeats), Math.Max(remainingSeats - 1, 0), vehicleType);
    }
}
