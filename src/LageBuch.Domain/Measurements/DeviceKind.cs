namespace LageBuch.Domain.Measurements;

/// <summary>What sort of Messgerät a device is (#930). Persisted by value.</summary>
public enum DeviceKind
{
    /// <summary>"Mehrgasmessgerät".</summary>
    MultiGas = 0,

    /// <summary>"Eingasmessgerät".</summary>
    SingleGas = 1,

    /// <summary>"Prüfröhrchen / Gasspürpumpe".</summary>
    TubePump = 2,

    /// <summary>"Röhrchen-Analysegerät (digital)".</summary>
    TubeAnalyser = 3,

    /// <summary>"Photoionisationsdetektor (PID)".</summary>
    Pid = 4,

    /// <summary>"Dosisleistungsmessgerät".</summary>
    DoseRate = 5,

    /// <summary>"Kontaminationsnachweisgerät".</summary>
    Contamination = 6,

    /// <summary>"pH-Indikator".</summary>
    Ph = 7,

    /// <summary>"Sonstiges".</summary>
    Other = 8,
}
