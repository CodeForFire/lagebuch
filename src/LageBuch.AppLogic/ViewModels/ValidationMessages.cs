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

    /// <summary>
    /// A Kräfte row nobody can call. The message names the dropdown because picking a Fahrzeug
    /// fills this field and the Wache together -- the Wache's own message stays terse rather than
    /// printing the same advice twice (#220).
    /// </summary>
    public const string CallSignRequired = "Funkrufname eingeben oder Fahrzeug wählen";

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

    /// <summary>
    /// One form's active messages on a single line, or null when it has nothing to say.
    /// <para>
    /// An input dock cannot put a message under each field: its fields sit in a horizontal
    /// StackPanel, which measures children at infinite width, so a message never wraps and instead
    /// makes its field as wide as the text -- far enough to push the dock's action button out of
    /// the window. The fields there say <em>which</em> by turning red, and this says what is
    /// needed. Dialogs, whose fields are stacked vertically inside a fixed-width card, keep the
    /// message under the field where it points more precisely.
    /// </para>
    /// <para>
    /// The separator is the one the status bar already uses ("gespeichert 17:19:35 | Zuletzt
    /// exportiert: …"), rather than introducing a second convention for the same job.
    /// </para>
    /// </summary>
    public static string? Summarize(params string?[] messages)
    {
        ArgumentNullException.ThrowIfNull(messages);

        var active = messages.Where(m => !string.IsNullOrEmpty(m)).ToArray();
        return active.Length == 0 ? null : string.Join(" | ", active);
    }
}
