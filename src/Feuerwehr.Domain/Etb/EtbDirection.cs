namespace Feuerwehr.Domain.Etb;

public enum EtbDirection
{
    Incoming,
    Outgoing,
    Internal,
    // Auto-generated events (Kräfte, Atemschutz, Einsatz-Lebenszyklus). Distinct from Internal,
    // which is reserved for human "Intern" notes. Appended last on purpose: the direction is
    // persisted by ordinal, so 0/1/2 are a wire contract and System must take 3.
    System
}
