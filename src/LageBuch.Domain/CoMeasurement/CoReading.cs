namespace LageBuch.Domain.CoMeasurement;

/// <summary>
/// One gas reading taken in a Wohnung (#424, per gas since #410) — the CO sibling of
/// <see cref="LageBuch.Domain.ForceUnitStrengthEdit"/>: what was measured, when, by whom.
/// </summary>
/// <remarks>
/// <para>
/// The series a <see cref="Dwelling"/> carries is the *whole* run, current value included, and
/// not the ForceUnit-style "superseded values only" pile. A strength edit dates the moment of a
/// correction; a Messreihe has to date the moment of each measurement, and the reading an
/// Einsatzleiter most wants a time for is the latest one. Keeping the current value out would
/// leave exactly that one undated, and would force every renderer to special-case "…and then the
/// parent's value, at an unknown time".
/// </para>
/// <para>
/// <see cref="GasId"/> points into the incident's own gas list (<see cref="IncidentGas"/>), never
/// into the Stammdaten. <see cref="Value"/> is decimal because 20,9 Vol-% O₂ is not an integer.
/// </para>
/// <para>
/// <see cref="Value"/> is nullable because clearing a reading is a correction worth keeping, not
/// an absence: after a deletion the Wohnung shows "kein Messwert" and the series is the only
/// remaining record that 120 ppm was ever there.
/// </para>
/// <para>
/// No identity of its own. A reading is immutable and never addressed singly; its identity is
/// (Wohnung, position in the series), exactly as with <c>force_unit_edits</c>. Readings of several
/// gases taken at one FERTIG share their <see cref="MeasuredAt"/>.
/// </para>
/// </remarks>
public sealed record CoReading(DateTimeOffset MeasuredAt, Guid GasId, decimal? Value, string RecordedBy);
