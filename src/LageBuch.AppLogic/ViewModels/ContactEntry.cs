using LageBuch.Persistence.MasterData;

namespace LageBuch.AppLogic.ViewModels;

/// <summary>
/// Somebody the Kontakte tab can reach: a person off the roster, or a vehicle that carries a
/// Fahrzeugtelefon or an E-Mail. Both read the same on a card -- a name, a caption beside it, a
/// number, an address -- so the tab searches, dials and mails one shape rather than two. Name is the headline: a person's
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

    /// <summary>
    /// Everybody the Kontakte tab lists: the people first, then, in Stammdaten order, every vehicle
    /// with a number or an address. A vehicle with neither is nobody to reach.
    /// </summary>
    public static IReadOnlyList<ContactEntry> Roster(IReadOnlyList<Person> people, IReadOnlyList<Vehicle>? vehicles)
    {
        ArgumentNullException.ThrowIfNull(people);
        return people.Select(FromPerson)
            .Concat((vehicles ?? Array.Empty<Vehicle>()).Where(v => v.HasPhone || v.HasEmail).Select(FromVehicle))
            .ToArray();
    }

    public static ContactEntry FromPerson(Person person)
    {
        ArgumentNullException.ThrowIfNull(person);
        return new(person.DisplayName, person.Role, person.CallSign, person.Phone, person.Email, person.Note, person.IsOwn, IsVehicle: false);
    }

    public static ContactEntry FromVehicle(Vehicle vehicle)
    {
        ArgumentNullException.ThrowIfNull(vehicle);
        return new(vehicle.CallSign, vehicle.Wache, null, vehicle.Phone, vehicle.Email, null, vehicle.IsOwn, IsVehicle: true);
    }
}
