namespace LageBuch.AppLogic.ViewModels;

/// <summary>The outcome of an entry form's submit; see <see cref="IEntryForm"/>.</summary>
public sealed class EntrySubmittedEventArgs(bool added) : EventArgs
{
    /// <summary>A submit that added the entry and cleared the form.</summary>
    public static readonly EntrySubmittedEventArgs Succeeded = new(added: true);

    /// <summary>A submit that validation stopped; the form keeps its values.</summary>
    public static readonly EntrySubmittedEventArgs Rejected = new(added: false);

    /// <summary>True when the entry was added; false when validation stopped it.</summary>
    public bool Added { get; } = added;
}
