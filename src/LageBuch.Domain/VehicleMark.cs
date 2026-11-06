namespace LageBuch.Domain;

/// <summary>
/// What a Fahrzeugtyp's taktisches Zeichen draws inside the Kraftfahrzeug Grundzeichen (DV 102),
/// so an LF, a DLK and an RTW read apart on the Lagekarte before anyone reads their text. Only
/// ever drawn, never stored: the incident keeps the Fahrzeugtyp as text.
/// </summary>
public enum VehicleMark
{
    /// <summary>The bare Grundzeichen, the Kurzbezeichnung in its middle: ELW, KdoW, MTW, Gerätewagen.</summary>
    Keines,

    /// <summary>A line across the middle forking to the front: the Löschfahrzeug, the code above it.</summary>
    Loeschfahrzeug,

    /// <summary>The Löschfahrzeug's line and fork with a hose laid in waves above it, without a code.</summary>
    Schlauchwagen,

    /// <summary>A ladder rising to the front with the Korb at its top, without a code.</summary>
    Hubrettung,

    /// <summary>A winch and its rope, the code above it.</summary>
    Ruestwagen,

    /// <summary>A frame under and behind the body: the Wechselladerfahrzeug, the code in the middle.</summary>
    Wechsellader,

    /// <summary>A cross through the body, the code in its upper left: RTW and KTW.</summary>
    Sanitaet,

    /// <summary>The Sanität cross with a bar beneath it: the Notarzteinsatzfahrzeug.</summary>
    Notarzt,
}
