using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.Input;
using Feuerwehr.Persistence.MasterData;

namespace Feuerwehr.AppLogic.ViewModels;

/// <summary>Editor for the personnel roster (five fields per row).</summary>
public sealed partial class PersonnelSection : EditorSection
{
    private readonly Action _onChanged;

    public PersonnelSection(string title, IEnumerable<Person> people, Action onChanged) : base(title)
    {
        _onChanged = onChanged;
        Rows = new ObservableCollection<PersonRow>(
            people.Select(p => new PersonRow(p.LastName, p.FirstName, p.Role, p.CallSign, p.Phone, onChanged)));
    }

    public ObservableCollection<PersonRow> Rows { get; }

    [RelayCommand]
    private void Add()
    {
        Rows.Add(new PersonRow(string.Empty, string.Empty, null, null, null, _onChanged));
        _onChanged();
    }

    [RelayCommand]
    private void Remove(PersonRow row)
    {
        if (Rows.Remove(row)) _onChanged();
    }

    /// <summary>Rows with a non-blank last name; trimmed, with blank optionals collapsed to null.</summary>
    public IReadOnlyList<Person> ToPeople()
    {
        var result = new List<Person>();
        foreach (var r in Rows)
        {
            var last = r.LastName?.Trim() ?? string.Empty;
            if (last.Length == 0) continue;
            result.Add(new Person(last, r.FirstName?.Trim() ?? string.Empty, Nz(r.Role), Nz(r.CallSign), Nz(r.Phone)));
        }
        return result;

        static string? Nz(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
    }
}
