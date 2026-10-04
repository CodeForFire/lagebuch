namespace LageBuch.AppLogic.ViewModels;

/// <summary>
/// A module whose dock adds one entry at a time: ETB, Kräfte, Atemschutz, Rollen, Aufgaben,
/// Beteiligte (#540).
/// </summary>
/// <remarks>
/// The view decides where focus goes after a submit (the form's first field, or the first invalid
/// one), but only the view model knows whether the submit went through. This event carries that
/// outcome, whichever way the submit was triggered — Enter in a field or a click on the button.
/// A submit that hands over to an overlay instead (ETB "Hinzufügen &amp; Aufgabe", a Rollen transfer)
/// raises nothing: the overlay owns focus then.
/// </remarks>
public interface IEntryForm
{
    /// <summary>Raised after the dock's add command ran to an outcome.</summary>
    event EventHandler<EntrySubmittedEventArgs>? EntrySubmitted;
}
