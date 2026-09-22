using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LageBuch.Persistence.MasterData;

namespace LageBuch.AppLogic.ViewModels;

/// <summary>
/// The Stammdaten <em>Navigation</em> category: one ordered list of everything the Einsatz
/// sidebar can show — the built-in modules and every Checkliste — each switchable on or off.
/// </summary>
/// <remarks>
/// Rows cannot be added or removed here: they are derived from the modules this build has plus
/// the Checkliste sections, so «Neue Checkliste» and the delete button are what changes the set.
/// The list is rebuilt from the stored layout on load and whenever a Checkliste is added or
/// deleted, which is also what keeps a newly created list from being invisible.
/// </remarks>
public sealed partial class NavigationSection : EditorSection
{
    private readonly Action _onChanged;

    public NavigationSection(string title, Action onChanged)
        : base(title)
    {
        _onChanged = onChanged;
        Rows = new ObservableCollection<NavRow>();
    }

    public ObservableCollection<NavRow> Rows { get; }

    /// <summary>
    /// Rebuilds the rows from a stored layout plus the Checklisten that currently exist, keeping
    /// the layout's order and visibility and appending anything it does not mention.
    /// </summary>
    /// <remarks>
    /// The same reconciliation <c>NavigationLayout.Resolve</c> performs for the workspace, over
    /// templates instead of one Einsatz's own lists — an entry naming a deleted Checkliste
    /// disappears, and a Checkliste the layout predates is appended visible rather than hidden.
    /// </remarks>
    public void Rebuild(IReadOnlyList<NavEntry> layout, IReadOnlyList<ChecklistTemplateSection> checklists)
    {
        ArgumentNullException.ThrowIfNull(layout);
        ArgumentNullException.ThrowIfNull(checklists);

        var effective = layout.Count == 0 ? NavLayout.Default : layout;
        var byId = checklists.ToDictionary(c => c.Id);
        var seenLists = new HashSet<Guid>();
        var seenModules = new HashSet<string>(StringComparer.Ordinal);

        Rows.Clear();

        foreach (var entry in effective)
        {
            if (entry.IsChecklist)
            {
                var id = entry.ChecklistId!.Value;
                if (!seenLists.Add(id) || !byId.TryGetValue(id, out var section))
                {
                    continue;
                }

                Rows.Add(NavRow.ForChecklist(section, entry.IsVisible, _onChanged));
                continue;
            }

            if (NavModules.IsKnown(entry.ModuleKey) && seenModules.Add(entry.ModuleKey))
            {
                Rows.Add(NavRow.ForModule(entry.ModuleKey, entry.IsVisible, _onChanged));
            }
        }

        foreach (var section in checklists.Where(c => !seenLists.Contains(c.Id)))
        {
            Rows.Add(NavRow.ForChecklist(section, isVisible: true, _onChanged));
        }

        foreach (var key in NavModules.All.Where(k => !seenModules.Contains(k)))
        {
            Rows.Add(NavRow.ForModule(key, isVisible: true, _onChanged));
        }
    }

    public IReadOnlyList<NavEntry> ToValues() =>
        Rows.Select(r => new NavEntry(r.ModuleKey, r.ChecklistId, r.IsVisible)).ToList();

    [RelayCommand]
    private void MoveUp(NavRow row)
    {
        var i = Rows.IndexOf(row);
        if (i > 0)
        {
            Rows.Move(i, i - 1);
            _onChanged();
        }
    }

    [RelayCommand]
    private void MoveDown(NavRow row)
    {
        var i = Rows.IndexOf(row);
        if (i >= 0 && i < Rows.Count - 1)
        {
            Rows.Move(i, i + 1);
            _onChanged();
        }
    }
}

/// <summary>One row of the Navigation list: a module or a Checkliste, and whether it is shown.</summary>
public sealed partial class NavRow : ObservableObject
{
    private readonly Action _onChanged;

    private NavRow(string label, string moduleKey, Guid? checklistId, bool canHide, bool isVisible, Action onChanged)
    {
        _label = label;
        ModuleKey = moduleKey;
        ChecklistId = checklistId;
        CanHide = canHide;
        _isVisible = isVisible;
        _onChanged = onChanged;
    }

    /// <summary>What the row reads in the editor. Follows a Checkliste's name as it is typed.</summary>
    [ObservableProperty]
    private string _label;

    public string ModuleKey { get; }

    public Guid? ChecklistId { get; }

    /// <summary>
    /// False only for the ETB, whose checkbox is disabled: it is the legally relevant record and
    /// every Systemmeldung lands there, so hiding it would make the app lie about where they went.
    /// </summary>
    public bool CanHide { get; }

    [ObservableProperty]
    private bool _isVisible;

    public static NavRow ForModule(string moduleKey, bool isVisible, Action onChanged)
    {
        var isEtb = string.Equals(moduleKey, NavModules.Etb, StringComparison.Ordinal);
        return new NavRow(
            ModuleLabels.TryGetValue(moduleKey, out var label) ? label : moduleKey,
            moduleKey,
            checklistId: null,
            canHide: !isEtb,
            isVisible: isEtb || isVisible,
            onChanged);
    }

    // The label follows the section, so renaming a Checkliste renames its Navigation row too.
    public static NavRow ForChecklist(ChecklistTemplateSection section, bool isVisible, Action onChanged)
    {
        ArgumentNullException.ThrowIfNull(section);
        var row = new NavRow(
            section.Title, NavModules.Checklist, section.Id, canHide: true, isVisible, onChanged);
        section.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is null or nameof(EditorSection.Title))
            {
                row.Label = section.Title;
            }
        };
        return row;
    }

    private static readonly Dictionary<string, string> ModuleLabels =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [NavModules.Etb] = "ETB",
            [NavModules.Tasks] = "Aufgaben",
            [NavModules.Roles] = "Funktionen",
            [NavModules.Forces] = "Kräfte",
            [NavModules.Scba] = "Atemschutz",
            [NavModules.Co] = "CO-Messung",
            [NavModules.Files] = "Dateien",
            [NavModules.Links] = "Links",
            [NavModules.Contacts] = "Kontakte",
        };

    partial void OnIsVisibleChanged(bool value) => _onChanged();
}
