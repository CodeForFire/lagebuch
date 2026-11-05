namespace LageBuch.Domain;

/// <summary>
/// One Fahrzeugtyp of <see cref="VehicleTypes"/>: the Kurzbezeichnung the taktisches Zeichen
/// carries, its full name, what its sign draws inside the Grundzeichen, and whether the sign
/// stands on a third wheel, as the Löschfahrzeuge and Gerätewagen do.
/// </summary>
public sealed record VehicleType(string Code, string Name, VehicleMark Mark = VehicleMark.Keines, bool MiddleWheel = false);
