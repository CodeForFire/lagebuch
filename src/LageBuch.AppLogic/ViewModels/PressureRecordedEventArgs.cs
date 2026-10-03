namespace LageBuch.AppLogic.ViewModels;

/// <summary>A Druckkontrolle was recorded (#539); see <see cref="ScbaViewModel.PressureRecorded"/>.</summary>
public sealed class PressureRecordedEventArgs(ScbaTruppRow? nextDue) : EventArgs
{
    /// <summary>The Trupp whose Druckabfrage is due next, the most overdue first; null when none is.</summary>
    public ScbaTruppRow? NextDue { get; } = nextDue;
}
