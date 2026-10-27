using LageBuch.Domain.ValueObjects;

namespace LageBuch.Persistence.Map;

/// <summary>An address from a Kartenpaket, where OpenStreetMap puts it. Empty text means untagged.</summary>
public sealed record AddressHit(string Street, string HouseNumber, string Postcode, string City, string Suburb, GeoPoint Position);
