using CommunityToolkit.Mvvm.ComponentModel;

namespace Feuerwehr.AppLogic.ViewModels;

/// <summary>One editable string entry in a master-data list. Reports edits via <c>onChanged</c>.</summary>
public sealed partial class MasterDataItem : ObservableObject
{
    private readonly Action _onChanged;

    public MasterDataItem(string value, Action onChanged)
    {
        _onChanged = onChanged;
        _value = value; // backing field: construction must not flag a change
    }

    [ObservableProperty]
    private string _value;

    partial void OnValueChanged(string value) => _onChanged();
}
