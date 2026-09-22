using CommunityToolkit.Mvvm.ComponentModel;

namespace LageBuch.AppLogic.ViewModels;

/// <summary>One editable personnel entry. Every field reports edits via <c>onChanged</c>.</summary>
public sealed partial class PersonRow : ObservableObject
{
    private readonly Action _onChanged;

    public PersonRow(
        string lastName,
        string firstName,
        string? role,
        string? callSign,
        string? phone,
        string? email,
        string? note,
        Action onChanged)
    {
        _onChanged = onChanged;
        _lastName = lastName;
        _firstName = firstName;
        _role = role;
        _callSign = callSign;
        _phone = phone;
        _email = email;
        _note = note;
    }

    [ObservableProperty]
    private string _lastName;
    [ObservableProperty]
    private string _firstName;
    [ObservableProperty]
    private string? _role;
    [ObservableProperty]
    private string? _callSign;
    [ObservableProperty]
    private string? _phone;
    [ObservableProperty]
    private string? _email;
    [ObservableProperty]
    private string? _note;

    partial void OnLastNameChanged(string value) => _onChanged();

    partial void OnFirstNameChanged(string value) => _onChanged();

    partial void OnRoleChanged(string? value) => _onChanged();

    partial void OnCallSignChanged(string? value) => _onChanged();

    partial void OnPhoneChanged(string? value) => _onChanged();

    partial void OnEmailChanged(string? value) => _onChanged();

    partial void OnNoteChanged(string? value) => _onChanged();
}
