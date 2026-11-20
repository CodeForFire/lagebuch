using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LageBuch.Domain.Atemschutz;
using LageBuch.Domain.Etb;
using LageBuch.Sync;

namespace LageBuch.AppLogic.ViewModels;

/// <summary>
/// The Strahlenschutzüberwachung of one Strahlenschutztrupp (#994), opened from its row: per crew
/// member the numbers of OSL-Dosimeter, Alarmdosimeter, Maske and PA, and the Alarmdosimeter's
/// Ablesewert once the Dekon-Platz reports it. Buffered until SPEICHERN; <see cref="Closed"/> fires
/// on every outcome so the host clears the overlay.
/// <para>
/// A dosimeter that is not there is declared, never left blank: "nicht vorhanden" asks first,
/// because it breaks the chain of documentation, and the ETB and the Bericht say so.
/// </para>
/// </summary>
public sealed partial class RadiationDialogViewModel : ObservableObject
{
    private readonly IIncidentSession _session;
    private readonly Guid _truppId;
    private readonly Action? _onChanged;

    // The declaration waiting for its confirmation, while the warning is up.
    private (RadiationMemberRow Row, DosimeterKind Kind)? _pendingMissing;

    /// <param name="session">The Einsatz holding the Trupp; its current records seed the rows.</param>
    /// <param name="truppId">A Trupp under Strahlenschutzüberwachung.</param>
    /// <param name="onChanged">Called after a save that went through, so the host can persist.</param>
    public RadiationDialogViewModel(IIncidentSession session, Guid truppId, Action? onChanged = null)
    {
        ArgumentNullException.ThrowIfNull(session);
        _session = session;
        _truppId = truppId;
        _onChanged = onChanged;
        var trupp = session.Incident.ScbaTrupps.First(t => t.Id == truppId);
        Title = $"Strahlenschutz · {trupp.DisplayName}";
        Members = trupp.Members.Select(m => new RadiationMemberRow(m, RequestMissing)).ToList();
    }

    public event EventHandler? Closed;

    /// <summary>"Strahlenschutz · HLF 1 · Strahlenschutztrupp".</summary>
    public string Title { get; }

    /// <summary>One row per crew member, in position order.</summary>
    public IReadOnlyList<RadiationMemberRow> Members { get; }

    /// <summary>Why the last save was refused, in German; null otherwise.</summary>
    [ObservableProperty]
    private string? _saveError;

    /// <summary>The warning shown before a dosimeter is declared missing; null while none is pending.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsMissingPending))]
    private string? _missingWarning;

    public bool IsMissingPending => MissingWarning is not null;

    private bool CanSave => !_session.IsReadOnly;

    private void RequestMissing(RadiationMemberRow row, DosimeterKind kind)
    {
        _pendingMissing = (row, kind);
        MissingWarning =
            $"{DosimeterName(kind)} von {row.Name} nicht vorhanden? Damit ist die Dokumentationskette unterbrochen. "
            + "Das wird im ETB und im Bericht vermerkt.";
    }

    /// <summary>NICHT VORHANDEN, confirmed: the row takes the declaration.</summary>
    [RelayCommand]
    private void ConfirmMissing()
    {
        if (_pendingMissing is { } pending)
        {
            pending.Row.SetMissing(pending.Kind, true);
        }

        _pendingMissing = null;
        MissingWarning = null;
    }

    [RelayCommand]
    private void CancelMissing()
    {
        _pendingMissing = null;
        MissingWarning = null;
    }

    [RelayCommand(CanExecute = nameof(CanSave))]
    private void Save()
    {
        SaveError = null;
        if (!Members.All(m => m.Validate()))
        {
            return;
        }

        // A Trupp is never taken out of an Einsatz, so the one this dialog was opened for is there.
        var trupp = _session.Incident.ScbaTrupps.First(t => t.Id == _truppId);
        try
        {
            var changes = Members
                .Select(row => (Row: row, Before: trupp.Members.First(m => m.Role == row.Role).Radiation ?? RadiationRecord.Empty, After: row.ToRecord()))
                .Where(change => change.After != change.Before)
                .ToList();
            if (changes.Count == 0)
            {
                Closed?.Invoke(this, EventArgs.Empty);
                return;
            }

            foreach (var (row, before, after) in changes)
            {
                _session.RecordScbaRadiation(_truppId, row.Role, after);
                foreach (var line in JournalLines(trupp.DisplayName, row.Name, before, after))
                {
                    _session.AddJournalEntry(EtbDirection.System, line, from: trupp.CallSign, to: null);
                }
            }
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException)
        {
            SaveError = DomainRefusal.Message(ex);
            return;
        }

        _onChanged?.Invoke();
        Closed?.Invoke(this, EventArgs.Empty);
    }

    [RelayCommand]
    private void Cancel() => Closed?.Invoke(this, EventArgs.Empty);

    /// <summary>
    /// The ETB lines one crew member's change writes: the numbers when they changed, a declared
    /// missing dosimeter as its own ACHTUNG line, and the Ablesewert once it is there. These are
    /// persisted and read back off the Einsatzbericht, so the wording is a data format.
    /// </summary>
    public static IEnumerable<string> JournalLines(string trupp, string name, RadiationRecord before, RadiationRecord after)
    {
        ArgumentNullException.ThrowIfNull(before);
        ArgumentNullException.ThrowIfNull(after);
        if (before.OslDosimeterNumber != after.OslDosimeterNumber
            || before.AlarmDosimeterNumber != after.AlarmDosimeterNumber
            || before.MaskNumber != after.MaskNumber
            || before.PaNumber != after.PaNumber)
        {
            yield return $"{trupp}: Strahlenschutz {name} – {after.NumbersDisplay}";
        }

        if (after.OslDosimeterMissing && !before.OslDosimeterMissing)
        {
            yield return $"ACHTUNG {trupp}: OSL-Dosimeter von {name} nicht vorhanden – Dokumentationskette unterbrochen";
        }

        if (after.AlarmDosimeterMissing && !before.AlarmDosimeterMissing)
        {
            yield return $"ACHTUNG {trupp}: Alarmdosimeter von {name} nicht vorhanden – Dokumentationskette unterbrochen";
        }

        if (after.AlarmReading is { } reading && reading != before.AlarmReading)
        {
            yield return $"{trupp}: Ablesewert Alarmdosimeter {name}: {reading.Display}";
        }
    }

    private static string DosimeterName(DosimeterKind kind) =>
        kind == DosimeterKind.Osl ? "OSL-Dosimeter" : "Alarmdosimeter";
}
