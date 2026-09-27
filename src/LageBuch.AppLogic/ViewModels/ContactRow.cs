using LageBuch.Persistence.MasterData;

namespace LageBuch.AppLogic.ViewModels;

/// <summary>
/// One person as the Kontakte tab shows them: the roster entry plus the name and Notiz already
/// split into runs around the current search hits.
/// </summary>
public sealed record ContactRow(
    Person Person,
    IReadOnlyList<TextSegment> NameSegments,
    IReadOnlyList<TextSegment> NoteSegments)
{
    /// <summary>From a neighbouring brigade (#458), which the list marks so nobody is surprised.</summary>
    public bool IsForeign => !Person.IsOwn;
}
