using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Feuerwehr.Documents;
using Feuerwehr.Domain.Etb;
using Feuerwehr.Domain.Time;

namespace Feuerwehr.AppLogic.ViewModels;

public sealed record EtbEntryRow(string Time, string Direction, string? From, string? To, string Text, string EnteredBy);

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
        Entries = new ObservableCollection<EtbEntryRow>(
            session.Incident.Journal.Reverse().Select(ToRow));
    }

    public bool IsReadOnly { get; }
    public ObservableCollection<EtbEntryRow> Entries { get; }

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
        var entry = _session.Incident.AddJournalEntry(_clock, _session.Operator!, NewDirection, NewText, NewFrom, NewTo);
        Entries.Insert(0, ToRow(entry));
        NewText = string.Empty;
        NewFrom = null;
        NewTo = null;
        _onChanged();
    }

    private static EtbEntryRow ToRow(EtbEntry e) =>
        new(Formatting.Timestamp(e.Timestamp), Formatting.Direction(e.Direction), e.From, e.To, e.Text, e.EnteredBy);
}
