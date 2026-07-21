using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Feuerwehr.Documents;
using Feuerwehr.Domain.Etb;
using Feuerwehr.Domain.Time;

namespace Feuerwehr.AppLogic.ViewModels;

public sealed record EtbEntryRow(string Time, string Direction, string? From, string? To, string Text, string EnteredBy);

/// <summary>
/// An <see cref="EtbDirection"/> paired with its German label, so the picker shows the same
/// wording as the grid and the PDF. Binding the raw enum makes Avalonia fall back to
/// <see cref="Enum.ToString()"/>, which leaks the English identifiers into the UI.
/// </summary>
public sealed record EtbDirectionOption(EtbDirection Value, string Label);

public sealed partial class EtbViewModel : ObservableObject
{
    private readonly IncidentSession _session;
    private readonly IClock _clock;
    private readonly Action _onChanged;

    public EtbViewModel(IncidentSession session, IClock clock, Action onChanged)
    {
        _session = session;
        _clock = clock;
        _onChanged = onChanged;
        IsReadOnly = session.IsReadOnly;
        Entries = new ObservableCollection<EtbEntryRow>();
        Sync();
    }

    /// <summary>
    /// Brings the list up to date with the journal. Entries reach the journal from every module --
    /// Kräfte, Atemschutz, the ILS reminder -- not only from this tab, and without this they stayed
    /// invisible until the Einsatz was closed, resumed or reopened.
    ///
    /// The journal is append-only, so this renders just the tail it has not rendered yet and
    /// inserts at the top to keep the newest-first order. That leaves the existing rows untouched,
    /// which matters because rebuilding the collection resets the grid's scroll and selection --
    /// and it makes the method idempotent, so calling it on every save is free.
    /// </summary>
    public void Sync()
    {
        var journal = _session.Incident.Journal;
        for (var i = Entries.Count; i < journal.Count; i++)
            Entries.Insert(0, ToRow(journal[i]));
    }

    public bool IsReadOnly { get; }
    public ObservableCollection<EtbEntryRow> Entries { get; }
    public IReadOnlyList<EtbDirectionOption> DirectionOptions { get; } =
        Enum.GetValues<EtbDirection>()
            .Select(d => new EtbDirectionOption(d, Formatting.Direction(d)))
            .ToArray();

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(AddEntryCommand))]
    private string _newText = string.Empty;

    [ObservableProperty]
    private string? _newFrom;

    [ObservableProperty]
    private string? _newTo;

    [ObservableProperty]
    private EtbDirection _newDirection = EtbDirection.Incoming;

    private bool CanAddEntry => !IsReadOnly && !string.IsNullOrWhiteSpace(NewText);

    [RelayCommand(CanExecute = nameof(CanAddEntry))]
    private void AddEntry()
    {
        _session.Incident.AddJournalEntry(_clock, _session.Operator!, NewDirection, NewText, NewFrom, NewTo);
        Sync();
        NewText = string.Empty;
        NewFrom = null;
        NewTo = null;
        _onChanged();
    }

    private static EtbEntryRow ToRow(EtbEntry e) =>
        new(Formatting.Timestamp(e.Timestamp), Formatting.Direction(e.Direction), e.From, e.To, e.Text, e.EnteredBy);
}
