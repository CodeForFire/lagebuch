using CommunityToolkit.Mvvm.ComponentModel;

namespace Feuerwehr.AppLogic.ViewModels;

/// <summary>One editable Checkliste template entry: text plus whether it's mandatory.</summary>
public sealed partial class ChecklistTemplateRow : ObservableObject
{
    private readonly Action _onChanged;

    public ChecklistTemplateRow(string text, bool isMandatory, Action onChanged)
    {
        _onChanged = onChanged;
        _text = text;
        _isMandatory = isMandatory;
    }

    [ObservableProperty] private string _text;
    [ObservableProperty] private bool _isMandatory;

    partial void OnTextChanged(string value) => _onChanged();
    partial void OnIsMandatoryChanged(bool value) => _onChanged();
}
