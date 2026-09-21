namespace LageBuch.Domain.Etb;

public enum EtbDirection
{
    Incoming,
    Outgoing,
    Internal,

    // Auto-generated events (Kräfte, Atemschutz, Einsatz-Lebenszyklus). Distinct from Internal,
    // which is reserved for human "Intern" notes. Appended last on purpose: the direction is
    // persisted by ordinal, so 0/1/2 are a wire contract and System must take 3.
    System,

    // Automatic entries that are a professional record rather than bookkeeping: a CO reading is
    // what the Trupp measured, not "Einsatz begonnen". Separate from System (#424) purely so the
    // ETB's "Systemmeldungen ausblenden" -- on by default since #223 -- cannot hide it. Appended
    // after System for the same ordinal-is-a-wire-contract reason: Measurement must take 4.
    Measurement,
}
