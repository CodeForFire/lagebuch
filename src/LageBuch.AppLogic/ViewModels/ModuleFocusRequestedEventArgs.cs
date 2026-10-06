namespace LageBuch.AppLogic.ViewModels;

/// <summary>Carries how far a "user asked for this module" request reaches (#542, #544).</summary>
/// <param name="toStartField">
/// True when the caret has to go to the module's first field even if focus is already inside the
/// module, as for Ctrl+N; false when focus already in the module is left where it is.
/// </param>
public sealed class ModuleFocusRequestedEventArgs(bool toStartField) : EventArgs
{
    public bool ToStartField { get; } = toStartField;
}
