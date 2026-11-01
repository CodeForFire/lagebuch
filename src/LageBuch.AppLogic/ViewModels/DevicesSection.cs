using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.Input;
using LageBuch.Persistence.MasterData;

namespace LageBuch.AppLogic.ViewModels;

/// <summary>
/// Editor for the Geräte Stammdaten: rows of name + Anzahl, laid out like the Trupp-Typen. The
/// Ausgabeliste checks the Anzahl against what is still out.
/// <para>
/// The rows are alphabetical (#830): a Gerät is found by reading down the list. They are sorted
/// when the section is built -- on opening, SPEICHERN, Verwerfen and Import alike -- and not while
/// a name is typed: a row moving under the cursor would take the focus with it, mid-Tab (#773).
/// A new or renamed Gerät therefore moves into place on SPEICHERN, as a person does in the Personal.
/// </para>
/// </summary>
public sealed partial class DevicesSection : EditorSection
{
    // German order, because a person reads this list: "Äxte" next to "Axt", not after "Z". De is
    // the fixed culture Formatting applies to everything read on screen, never the device's.
    // Numbers by value, so "HRT 2" comes before "HRT 10".
    private static readonly StringComparer Alphabetical = StringComparer.Create(
        CultureInfo.GetCultureInfo("de-DE"), CompareOptions.IgnoreCase | CompareOptions.NumericOrdering);

    private readonly Action _onChanged;

    public DevicesSection(string title, IEnumerable<Device> devices, Action onChanged, Action<string, Action>? requestConfirm = null)
        : base(title, requestConfirm)
    {
        _onChanged = onChanged;

        // OrderBy is stable, so case-only duplicates keep their order and ToValues' first spelling
        // is still the one written first.
        Rows = new ObservableCollection<DeviceRow>(
            devices.OrderBy(d => d.Name.Trim(), Alphabetical).Select(d => new DeviceRow(d.Name, d.Count, _onChanged)));
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
    /// count a coin toss. The Anzahl is clamped to 0..<see cref="Device.MaxCount"/>. Alphabetical,
    /// so the store -- and with it the Ausgabeliste's picker and the JSON export -- holds them in the
    /// order the editor shows.
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

        return result.OrderBy(d => d.Name, Alphabetical).ToList();
    }
}
