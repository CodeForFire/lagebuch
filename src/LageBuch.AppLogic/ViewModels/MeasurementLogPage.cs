namespace LageBuch.AppLogic.ViewModels;

/// <summary>The four parts of the Messprotokoll (#930), one segmented control above them.</summary>
public enum MeasurementLogPage
{
    /// <summary>The Messreihe: tiles, the list of Messungen and the form that records one.</summary>
    Messreihe = 0,

    /// <summary>The Messpunkte and the weather log.</summary>
    MesspunkteWetter = 1,

    /// <summary>What is measured, with its unit and bands.</summary>
    Messgroessen = 2,

    /// <summary>The Messgeräte the Messgrößen are read from.</summary>
    Geraete = 3,
}
