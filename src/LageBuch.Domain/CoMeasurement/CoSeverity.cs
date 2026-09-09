namespace LageBuch.Domain.CoMeasurement;

/// <summary>How dangerous a ppm reading is, independent of <see cref="DwellingStatus"/> (which
/// tracks search progress, not the measured value). Bands anchor on the German AGW
/// (Arbeitsplatzgrenzwert, 30 ppm 8h-TWA) at the low end and the commonly-cited CO lethality
/// range (800+ ppm: collapse/death within 2-3h) at the high end.</summary>
public enum CoSeverity
{
    Normal,
    Elevated,
    Dangerous,
    Lethal,
}
