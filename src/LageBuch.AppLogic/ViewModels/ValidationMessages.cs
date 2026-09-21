namespace LageBuch.AppLogic.ViewModels;

/// <summary>
/// The messages a form puts under a field that has to be filled before its button can do anything
/// (#412). Until now every such rule lived silently inside a <c>CanExecute</c> guard, so the button
/// went grey and nothing said which field was meant.
/// <para>
/// These explain, they never block. An Einsatz in progress must never be stopped by a rule -- the
/// same position <c>StammdatenCatalogue</c> takes about unknown catalogue values -- so a form shows
/// its message and stays exactly as the operator left it.
/// </para>
/// <para>
/// Hardcoded German, like every other user-facing string in the application; localization is #350.
/// </para>
/// </summary>
public static class ValidationMessages
{
    /// <summary>A field that means nothing when empty -- the plain case behind most forms.</summary>
    public const string Required = "Pflichtfeld — bitte ausfüllen";

    /// <summary>A timer box emptied, or holding something that is not a count of minutes.</summary>
    public const string TimerMinutes = "Minuten eintragen (0 oder mehr)";
}
