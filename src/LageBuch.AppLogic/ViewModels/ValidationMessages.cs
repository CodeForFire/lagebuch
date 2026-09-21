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

    /// <summary>The Wache a Kräfte row belongs to, without which the row reports nobody.</summary>
    public const string BrigadeRequired = "Wache eingeben";

    /// <summary>A headcount below zero, which no Stärke box can mean.</summary>
    public const string NegativeStrength = "Stärke darf nicht negativ sein";

    /// <summary>
    /// A Kräfte row with a Wache but nobody counted: it reports nothing, and is almost always a
    /// stray click rather than an intentional entry (#220).
    /// </summary>
    public const string NoPersonnel = "Mindestens eine Person eintragen";

    /// <summary>More Atemschutzgeräteträger than people, which the domain rejects outright.</summary>
    public const string ScbaExceedsStrength = "AGT darf die Stärke nicht überschreiten";

    /// <summary>
    /// A Trupp bereitgestellt without a starting pressure. Zero is not a reading: the whole
    /// Überwachung -- Restzeit, Alarm, Druckabfrage -- is computed from this number.
    /// </summary>
    public const string EntryPressure = "Einstiegsdruck eintragen";

    /// <summary>A PDF export with every section unticked, which would produce an empty document.</summary>
    public const string NoPdfSection = "Mindestens einen Abschnitt wählen";
}
