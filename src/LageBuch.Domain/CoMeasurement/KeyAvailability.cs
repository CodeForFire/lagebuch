namespace LageBuch.Domain.CoMeasurement;

/// <summary>Whether the crew has a Schlüssel to a Wohnung. Three states, not a <c>bool?</c>:
/// "nobody asked yet" is an answer of its own, and the editor has to be able to go back to it.
/// The ordinal is never persisted or sent — the file and the wire keep the older <c>bool?</c>
/// encoding through <see cref="KeyAvailabilityEncoding"/>.</summary>
public enum KeyAvailability
{
    Unknown = 0,      // nicht erfasst
    Available = 1,    // Schlüssel vorhanden
    NotAvailable = 2, // kein Schlüssel
}
