namespace LageBuch.Domain;

/// <summary>One Fahrzeugtyp of <see cref="VehicleTypes"/>: the Kurzbezeichnung the taktisches Zeichen carries, and its full name.</summary>
public sealed record VehicleType(string Code, string Name);
