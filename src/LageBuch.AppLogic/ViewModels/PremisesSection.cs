using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.Input;
using LageBuch.Persistence.MasterData;

namespace LageBuch.AppLogic.ViewModels;

/// <summary>
/// Editor for the Objekte Stammdaten (#420): buildings in the Ausrückebereich with their address
/// and the structure and Wohnungsbezeichnungen a Haus in the CO-Messprotokoll is created with.
/// </summary>
public sealed partial class PremisesSection : EditorSection
{
    private readonly Action _onChanged;

    public PremisesSection(string title, IEnumerable<Premises> premises, Action onChanged, Action<string, Action>? requestConfirm = null)
        : base(title, requestConfirm)
    {
        _onChanged = onChanged;
        Rows = new ObservableCollection<PremisesRow>(premises.Select(p => new PremisesRow(p, _onChanged)));
    }

    public ObservableCollection<PremisesRow> Rows { get; }

    [RelayCommand]
    private void Add()
    {
        var row = new PremisesRow(new Premises(string.Empty), _onChanged);
        Rows.Add(row);
        _onChanged();
        OnRowAdded(row);
    }

    [RelayCommand]
    private void Remove(PremisesRow row)
    {
        if (!Rows.Contains(row))
        {
            return;
        }

        RemoveAfterConfirm(row, row.Name, row.IsBlank, () =>
        {
            if (Rows.Remove(row))
            {
                _onChanged();
            }
        });
    }

    /// <summary>The rows as they are saved, through the same <see cref="Premises.Normalize"/> an
    /// import takes: a row without a name dropped, a second row of the same name too.</summary>
    public IReadOnlyList<Premises> ToValues() => Premises.Normalize(Rows.Select(r => r.ToPremises()));
}
