using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Feuerwehr.Persistence.MasterData;

namespace Feuerwehr.AppLogic.ViewModels;

public sealed record RoleRow(string Role, string PersonName, string? CallSign);

public sealed partial class RolesViewModel : ObservableObject
{
    private readonly IncidentSession _session;
    private readonly Action _onChanged;

    public RolesViewModel(IncidentSession session, MasterDataSet masterData, Action onChanged)
    {
        _session = session;
        _onChanged = onChanged;
        IsReadOnly = session.IsReadOnly;
        RoleOptions = masterData.Roles;
        CallSignOptions = masterData.RadioCallSigns;
        Roles = new ObservableCollection<RoleRow>(
            session.Incident.Roles.Select(r => new RoleRow(r.Role, r.PersonName, r.CallSign)));
    }

    public bool IsReadOnly { get; }
    public IReadOnlyList<string> RoleOptions { get; }
    public IReadOnlyList<string> CallSignOptions { get; }
    public ObservableCollection<RoleRow> Roles { get; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(AddRoleCommand))]
    private string _newRole = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(AddRoleCommand))]
    private string _newPersonName = string.Empty;

    [ObservableProperty]
    private string? _newCallSign;

    private bool CanAddRole =>
        !IsReadOnly && !string.IsNullOrWhiteSpace(NewRole) && !string.IsNullOrWhiteSpace(NewPersonName);

    [RelayCommand(CanExecute = nameof(CanAddRole))]
    private void AddRole()
    {
        var role = _session.Incident.AssignRole(NewRole, NewPersonName, NewCallSign);
        Roles.Add(new RoleRow(role.Role, role.PersonName, role.CallSign));
        NewRole = string.Empty;
        NewPersonName = string.Empty;
        NewCallSign = null;
        _onChanged();
    }
}
