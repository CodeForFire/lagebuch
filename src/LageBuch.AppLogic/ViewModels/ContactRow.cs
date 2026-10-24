using LageBuch.AppLogic.Services;
using LageBuch.Domain;

namespace LageBuch.AppLogic.ViewModels;

/// <summary>
/// One contact as the Kontakte tab shows them: the entry plus the name and Notiz already split
/// into runs around the current search hits.
/// </summary>
public sealed record ContactRow(
    ContactEntry Entry,
    IReadOnlyList<TextSegment> NameSegments,
    IReadOnlyList<TextSegment> NoteSegments)
{
    /// <summary>From a neighbouring brigade (#458), which the list marks so nobody is surprised.</summary>
    public bool IsForeign => !Entry.IsOwn;

    /// <summary>The number in its DIN 5008 reading form (#700); the Stammdaten keep it as typed.</summary>
    public string PhoneDisplay => PhoneNumber.Display(Entry.Phone);

    /// <summary>Whether the number has a global tel: form, and so a button that dials it.</summary>
    public bool CanDial => PhoneNumberValidator.TryGetTelUri(Entry.Phone, out _);

    /// <summary>
    /// A number with no global form ("112", an internal extension): still shown, as plain text,
    /// since a button that can only fail is worse than none.
    /// </summary>
    public bool ShowsUndialablePhone => Entry.HasPhone && !CanDial;
}
