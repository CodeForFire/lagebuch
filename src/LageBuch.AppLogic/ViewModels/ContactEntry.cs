using LageBuch.Persistence.MasterData;

namespace LageBuch.AppLogic.ViewModels;

/// <summary>
/// Somebody the Kontakte tab can reach: a person off the roster, or a vehicle that carries a
/// Fahrzeugtelefon. Both read the same on a card -- a name, a caption beside it, a number -- so the
/// tab searches and dials one shape rather than two. Name is the headline: a person's
/// "Nachname, Vorname", a vehicle's Funkrufname. Caption sits beside it: a person's Rolle, a
/// vehicle's Wache. CallSign is a person's Funkrufname, and null for a vehicle, whose name
/// already is one.
/// </summary>
public sealed record ContactEntry(
    string Name,
    string? Caption,
    string? CallSign,
    string? Phone,
    string? Email,
    string? Note,
    bool IsOwn,
    bool IsVehicle)
{
    /// <inheritdoc cref="Person.HasPhone"/>
    public bool HasPhone => !string.IsNullOrWhiteSpace(Phone);

    /// <inheritdoc cref="Person.HasEmail"/>
    public bool HasEmail => !string.IsNullOrWhiteSpace(Email);

    public static ContactEntry FromPerson(Person person)
    {
        ArgumentNullException.ThrowIfNull(person);
        return new(person.DisplayName, person.Role, person.CallSign, person.Phone, person.Email, person.Note, person.IsOwn, IsVehicle: false);
    }

    public static ContactEntry FromVehicle(Vehicle vehicle)
    {
        ArgumentNullException.ThrowIfNull(vehicle);
        return new(vehicle.CallSign, vehicle.Wache, null, vehicle.Phone, null, null, vehicle.IsOwn, IsVehicle: true);
    }
}
