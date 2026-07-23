using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Feuerwehr.AppLogic.Services;
using Feuerwehr.Persistence.MasterData;

namespace Feuerwehr.AppLogic.ViewModels;

/// <summary>
/// The Stammdaten editor. Loads every editable category from the provider into its own section,
/// tracks a single dirty flag across them, and writes the whole set back on Save. Streets are read
/// but not editable, so they are carried through untouched.
/// </summary>
public sealed partial class MasterDataEditorViewModel : ObservableObject
{
    private readonly IMasterDataProvider _provider;
    private MasterDataSet _original = MasterDataSet.Empty;

    // Typed handles kept so BuildSet reads each section without fragile positional casts.
    private EditableListSection _roles = null!, _status = null!, _unitStatus = null!, _equipment = null!,
        _districts = null!, _brigades = null!, _callSigns = null!, _truppTypes = null!, _checklist = null!;
    private PersonnelSection _personnel = null!;

    public MasterDataEditorViewModel(IMasterDataProvider provider)
    {
        _provider = provider;
        Load();
    }

    public ObservableCollection<EditorSection> Sections { get; } = new();

    [ObservableProperty]
    private EditorSection? _selectedSection;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    [NotifyCanExecuteChangedFor(nameof(DiscardCommand))]
    private bool _isDirty;

    [ObservableProperty]
    private ConfirmDialogViewModel? _pendingConfirm;

    private void MarkDirty() => IsDirty = true;

    private void Load()
    {
        _original = _provider.Get();
        var previousIndex = SelectedSection is null ? 0 : Sections.IndexOf(SelectedSection);

        Sections.Clear();
        Sections.Add(_roles = new EditableListSection("Rollen", _original.Roles, MarkDirty));
        Sections.Add(_status = new EditableListSection("Status", _original.Status, MarkDirty));
        Sections.Add(_unitStatus = new EditableListSection("Einheiten-Status", _original.UnitStatus, MarkDirty));
        Sections.Add(_equipment = new EditableListSection("Ausrüstung", _original.Equipment, MarkDirty));
        Sections.Add(_districts = new EditableListSection("Bezirke", _original.Districts, MarkDirty));
        Sections.Add(_brigades = new EditableListSection("Wachen", _original.Brigades, MarkDirty));
        Sections.Add(_callSigns = new EditableListSection("Funkrufnamen", _original.RadioCallSigns, MarkDirty));
        Sections.Add(_truppTypes = new EditableListSection("Trupp-Typen", _original.TruppTypes, MarkDirty));
        Sections.Add(_checklist = new EditableListSection("Checkliste", _original.ChecklistTemplate, MarkDirty));
        Sections.Add(_personnel = new PersonnelSection("Personal", _original.Personnel, MarkDirty));

        SelectedSection = Sections[Math.Clamp(previousIndex < 0 ? 0 : previousIndex, 0, Sections.Count - 1)];
        IsDirty = false;
    }

    private MasterDataSet BuildSet() => _original with
    {
        Roles = _roles.ToValues(),
        Status = _status.ToValues(),
        UnitStatus = _unitStatus.ToValues(),
        Equipment = _equipment.ToValues(),
        Districts = _districts.ToValues(),
        Brigades = _brigades.ToValues(),
        RadioCallSigns = _callSigns.ToValues(),
        TruppTypes = _truppTypes.ToValues(),
        ChecklistTemplate = _checklist.ToValues(),
        Personnel = _personnel.ToPeople(),
        // Streets are not editable here; _original carries them through unchanged.
    };

    [RelayCommand(CanExecute = nameof(IsDirty))]
    private void Save()
    {
        _provider.Save(BuildSet());
        Load(); // reflect normalization (trim/dedupe, name-sorted personnel) and clear dirty
    }

    [RelayCommand(CanExecute = nameof(IsDirty))]
    private void Discard() => Load();

    /// <summary>
    /// Navigation guard for the shell: when clean, runs <paramref name="proceed"/> at once; when
    /// dirty, raises a confirm overlay and, on confirm, discards the edits then proceeds.
    /// </summary>
    public void ConfirmDiscardThen(Action proceed)
    {
        if (!IsDirty)
        {
            proceed();
            return;
        }

        var dialog = new ConfirmDialogViewModel(
            "Änderungen verwerfen?",
            "Die Stammdaten wurden geändert. Beim Verlassen gehen die nicht gespeicherten Änderungen verloren.",
            "VERWERFEN",
            () => { Load(); proceed(); });
        dialog.Closed += (_, _) => PendingConfirm = null;
        PendingConfirm = dialog;
    }
}
