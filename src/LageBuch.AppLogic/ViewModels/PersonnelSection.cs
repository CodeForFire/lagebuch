using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.Input;
using LageBuch.Persistence.MasterData;

namespace LageBuch.AppLogic.ViewModels;

/// <summary>Editor for the personnel roster (seven fields and the own/foreign flag, on two lines per row).</summary>
public sealed partial class PersonnelSection : EditorSection
{
    private readonly Action _onChanged;

    public PersonnelSection(string title, IEnumerable<Person> people, Action onChanged, Action<string, Action>? requestConfirm = null)
        : base(title, requestConfirm)
    {
        _onChanged = onChanged;
        Rows = new ObservableCollection<PersonRow>(
            people.Select(p => new PersonRow(
                p.LastName, p.FirstName, p.Role, p.CallSign, p.Phone, p.IsOwn, p.Email, p.Note, onChanged)));
    }

    public ObservableCollection<PersonRow> Rows { get; }

    [RelayCommand]
    private void Add()
    {
        var row = new PersonRow(string.Empty, string.Empty, null, null, null, isOwn: true, null, null, _onChanged);
        Rows.Add(row);
        _onChanged();
        OnRowAdded(row);
    }

    [RelayCommand]
    private void Remove(PersonRow row)
    {
        if (!Rows.Contains(row))
        {
            return;
        }

        var name = string.Join(", ", new[] { row.LastName, row.FirstName }.Where(n => !string.IsNullOrWhiteSpace(n)));
        var isBlank = AllBlank(row.LastName, row.FirstName, row.Role, row.CallSign, row.Phone, row.Email, row.Note);
        RemoveAfterConfirm(name, isBlank, () =>
        {
            if (Rows.Remove(row))
            {
                _onChanged();
            }
        });
    }

    /// <summary>Rows with a non-blank last name; trimmed, with blank optionals collapsed to null.</summary>
    public IReadOnlyList<Person> ToPeople()
    {
        var result = new List<Person>();
        foreach (var r in Rows)
        {
            var last = r.LastName?.Trim() ?? string.Empty;
            if (last.Length == 0)
            {
                continue;
            }

            result.Add(new Person(
                last,
                r.FirstName?.Trim() ?? string.Empty,
                Nz(r.Role),
                Nz(r.CallSign),
                Nz(r.Phone),
                r.IsOwn,
                Nz(r.Email),
                Nz(r.Note)));
        }

        return result;

        static string? Nz(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
    }
}
