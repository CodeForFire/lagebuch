using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.Input;
using LageBuch.Domain;
using LageBuch.Persistence.MasterData;

namespace LageBuch.AppLogic.ViewModels;

/// <summary>
/// Editor for the Fahrzeugkennungen: rows of the middle number of a Funkrufname and the
/// Fahrzeugtyp it stands for. A Fahrzeug whose type is empty takes it from here as its Funkrufname
/// is typed. Only a repeated Kennung makes the order matter (the first one wins), so the rows
/// cannot be moved.
/// </summary>
public sealed partial class VehicleTypeCodesSection : EditorSection
{
    private readonly Action _onChanged;

    public VehicleTypeCodesSection(string title, IReadOnlyList<VehicleTypeCode> codes, Action onChanged, Action<string, Action>? requestConfirm = null)
        : base(title, requestConfirm)
    {
        ArgumentNullException.ThrowIfNull(title);
        ArgumentNullException.ThrowIfNull(codes);
        ArgumentNullException.ThrowIfNull(onChanged);
        _onChanged = onChanged;
        Rows = new ObservableCollection<VehicleTypeCodeRow>(
            codes.Select(c => new VehicleTypeCodeRow(c.Kennung, c.VehicleType, onChanged)));
    }

    public ObservableCollection<VehicleTypeCodeRow> Rows { get; }

    [RelayCommand]
    private void Add()
    {
        var row = new VehicleTypeCodeRow(string.Empty, null, _onChanged);
        Rows.Add(row);
        _onChanged();
        OnRowAdded(row);
    }

    [RelayCommand]
    private void Remove(VehicleTypeCodeRow row)
    {
        if (!Rows.Contains(row))
        {
            return;
        }

        RemoveAfterConfirm(row, row.Kennung, AllBlank(row.Kennung, row.VehicleType), () =>
        {
            if (Rows.Remove(row))
            {
                _onChanged();
            }
        });
    }

    /// <summary>
    /// Rows with both a Kennung and a Fahrzeugtyp, trimmed, in current order; the type cut to its
    /// cap and spelled as the catalogue does, as a Fahrzeug row stores it.
    /// </summary>
    public IReadOnlyList<VehicleTypeCode> ToValues()
    {
        var result = new List<VehicleTypeCode>();
        foreach (var row in Rows)
        {
            var kennung = row.Kennung.Trim();
            var type = row.VehicleType?.Trim() ?? string.Empty;
            if (kennung.Length > 0 && type.Length > 0)
            {
                if (type.Length > VehicleTypes.MaxLength)
                {
                    type = type[..VehicleTypes.MaxLength].TrimEnd();
                }

                result.Add(new VehicleTypeCode(kennung, VehicleTypes.Find(type)?.Code ?? type));
            }
        }

        return result;
    }
}
