using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.Input;
using LageBuch.Persistence.MasterData;

namespace LageBuch.AppLogic.ViewModels;

/// <summary>
/// Editor for the Geräte Stammdaten: rows of name + Anzahl, laid out like the Trupp-Typen. The
/// Ausgabeliste checks the Anzahl against what is still out.
/// </summary>
public sealed partial class DevicesSection : EditorSection
{
    private readonly Action _onChanged;

    public DevicesSection(string title, IEnumerable<Device> devices, Action onChanged, Action<string, Action>? requestConfirm = null)
        : base(title, requestConfirm)
    {
        _onChanged = onChanged;
        Rows = new ObservableCollection<DeviceRow>(devices.Select(d => new DeviceRow(d.Name, d.Count, _onChanged)));
    }

    public ObservableCollection<DeviceRow> Rows { get; }

    [RelayCommand]
    private void Add()
    {
        // One is what a new Gerät usually is: the ELW carries one AED, one Fernglas.
        var row = new DeviceRow(string.Empty, 1, _onChanged);
        Rows.Add(row);
        _onChanged();
        OnRowAdded(row);
    }

    [RelayCommand]
    private void Remove(DeviceRow row)
    {
        if (!Rows.Contains(row))
        {
            return;
        }

        RemoveAfterConfirm(row, row.Name, AllBlank(row.Name), () =>
        {
            if (Rows.Remove(row))
            {
                _onChanged();
            }
        });
    }

    /// <summary>
    /// Rows with a non-blank name, trimmed, first spelling winning a case-insensitive duplicate --
    /// the Ausgabeliste looks a Gerät up by name, so two rows differing only in case would make its
    /// count a coin toss. The Anzahl is clamped to 0..<see cref="Device.MaxCount"/>.
    /// </summary>
    public IReadOnlyList<Device> ToValues()
    {
        var result = new List<Device>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var row in Rows)
        {
            var name = row.Name?.Trim() ?? string.Empty;
            if (name.Length > 0 && seen.Add(name))
            {
                result.Add(new Device(name, Device.ClampCount(row.Count)));
            }
        }

        return result;
    }
}
