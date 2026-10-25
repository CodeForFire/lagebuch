using CommunityToolkit.Mvvm.ComponentModel;
using LageBuch.Persistence.MasterData;

namespace LageBuch.AppLogic.ViewModels;

/// <summary>
/// One editable Gerät: its name and how many of it the ELW carries, 0 for not counted.
/// </summary>
public sealed partial class DeviceRow : ObservableObject
{
    private readonly Action _onChanged;

    public DeviceRow(string name, int count, Action onChanged)
    {
        _onChanged = onChanged;
        _name = name;
        _count = count;
    }

    /// <summary>Largest count the view's NumericUpDown offers, bound so it cannot drift from the
    /// clamp every entrance applies.</summary>
    public static int MaxCount => Device.MaxCount;

    [ObservableProperty]
    private string _name;

    [ObservableProperty]
    private int _count;

    partial void OnNameChanged(string value) => _onChanged();

    partial void OnCountChanged(int value) => _onChanged();
}
