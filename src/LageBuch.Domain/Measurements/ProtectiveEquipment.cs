namespace LageBuch.Domain.Measurements;

/// <summary>What the Messtrupp wore (#930). Persisted by value.</summary>
public enum ProtectiveEquipment
{
    /// <summary>"ohne Sonderausrüstung".</summary>
    None = 0,

    /// <summary>"Filter + Form 1".</summary>
    FilterForm1 = 1,

    /// <summary>"PA + Form 1".</summary>
    ScbaForm1 = 2,

    /// <summary>"PA + Form 2".</summary>
    ScbaForm2 = 3,

    /// <summary>"Form 3 (CSA)".</summary>
    Form3 = 4,
}
