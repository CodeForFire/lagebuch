using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LageBuch.AppLogic.Services;
using LageBuch.Persistence.MasterData;

namespace LageBuch.AppLogic.ViewModels;

/// <summary>
/// The Stammdaten editor. Loads every editable category from the provider into its own section,
/// tracks a single dirty flag across them, and writes the whole set back on Save. Streets are read
/// but not editable, so they are carried through untouched. Import (offered only while the data is
/// empty) fills the editor from a JSON file for review; Export writes the current set back out.
/// </summary>
public sealed partial class MasterDataEditorViewModel : ObservableObject
{
    private readonly IMasterDataProvider _provider;
    private readonly IFileDialogService _dialogs;
    private readonly IMasterDataFileService _files;
    private MasterDataSet _original = MasterDataSet.Empty;
    private bool _originalIsEmpty = true;

    // Typed handles kept so BuildSet reads each section without fragile positional casts.
    private EditableListSection _roles = null!, _status = null!, _unitStatus = null!, _equipment = null!,
        _districts = null!, _brigades = null!, _callSigns = null!, _truppTypes = null!, _einsatzarten = null!;
    private ChecklistTemplateSection _checklistAufbau = null!, _checklistAbbau = null!;
    private LinksSection _links = null!;
    private PersonnelSection _personnel = null!;
    private VehiclesSection _vehicles = null!;
    private SettingsSection _settings = null!;
    private EinsatzgebietSection _einsatzgebiet = null!;

    public MasterDataEditorViewModel(IMasterDataProvider provider, IFileDialogService dialogs, IMasterDataFileService files)
    {
        _provider = provider;
        _dialogs = dialogs;
        _files = files;
        Load();
    }

    public ObservableCollection<EditorSection> Sections { get; } = new();

    [ObservableProperty]
    private EditorSection? _selectedSection;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    [NotifyCanExecuteChangedFor(nameof(DiscardCommand))]
    [NotifyCanExecuteChangedFor(nameof(ImportCommand))]
    private bool _isDirty;

    /// <summary>A message shown when an import or export fails; cleared when the next one starts.</summary>
    [ObservableProperty]
    private string? _fileError;

    /// <summary>
    /// Fahrzeuge sind eindeutig (#76 follow-up): der Funkrufname identifiziert das Fahrzeug und darf
    /// nur einmal vorkommen — unabhängig von der Wache, getrimmt und ohne Groß-/Kleinschreibung.
    /// Doppelte benennt diese Meldung und blockiert das Speichern, statt sie still zu deduplizieren.
    /// </summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    private string? _vehicleConflicts;

    public bool HasVehicleConflicts => VehicleConflicts is not null;

    [ObservableProperty]
    private ConfirmDialogViewModel? _pendingConfirm;

    private void MarkDirty() => IsDirty = true;

    private void Load()
    {
        _original = _provider.Get();
        _originalIsEmpty = _original.IsEmpty;
        FileError = null;
        PopulateSections(_original);
        RefreshVehicleConflicts();
        IsDirty = false;
        ImportCommand.NotifyCanExecuteChanged();
    }

    private void PopulateSections(MasterDataSet set)
    {
        var previousIndex = SelectedSection is null ? 0 : Sections.IndexOf(SelectedSection);

        Sections.Clear();
        Sections.Add(_settings = new SettingsSection("Einstellungen", set.Settings, MarkDirty));
        Sections.Add(_roles = new EditableListSection("Rollen", set.Roles, MarkDirty));
        Sections.Add(_status = new EditableListSection("Status", set.Status, MarkDirty));
        Sections.Add(_unitStatus = new EditableListSection("Einheiten-Status", set.UnitStatus, MarkDirty));
        Sections.Add(_equipment = new EditableListSection("Ausrüstung", set.Equipment, MarkDirty));
        Sections.Add(_districts = new EditableListSection("Bezirke", set.Districts, MarkDirty));
        Sections.Add(_brigades = new EditableListSection("Wachen", set.Brigades, MarkDirty));
        Sections.Add(_callSigns = new EditableListSection("Funkrufnamen", set.RadioCallSigns, MarkDirty));
        Sections.Add(_truppTypes = new EditableListSection("Trupp-Typen", set.TruppTypes, MarkDirty));
        Sections.Add(_einsatzarten = new EditableListSection("Einsatzarten", set.Einsatzarten, MarkDirty));
        Sections.Add(_links = new LinksSection("Links", set.Links, MarkDirty));
        Sections.Add(_checklistAufbau = new ChecklistTemplateSection("Checkliste Aufbau", set.ChecklistTemplateAufbau, MarkDirty));
        Sections.Add(_checklistAbbau = new ChecklistTemplateSection("Checkliste Abbau", set.ChecklistTemplateAbbau, MarkDirty));
        Sections.Add(_personnel = new PersonnelSection("Personal", set.Personnel, MarkDirty));
        Sections.Add(_vehicles = new VehiclesSection("Fahrzeuge", set.Vehicles, set.Brigades, set.RadioCallSigns, OnVehiclesChanged));
        Sections.Add(_einsatzgebiet = new EinsatzgebietSection("Einsatzgebiet", set.Einsatzgebiet, MarkDirty));

        SelectedSection = Sections[Math.Clamp(previousIndex < 0 ? 0 : previousIndex, 0, Sections.Count - 1)];
    }

    private void OnVehiclesChanged()
    {
        IsDirty = true;
        RefreshVehicleConflicts();
    }

    /// <summary>Recomputes the Funkrufnamen conflict list from the current rows.</summary>
    private void RefreshVehicleConflicts()
    {
        var duplicates = _vehicles.ToValues()
            .Where(v => !string.IsNullOrWhiteSpace(v.CallSign))
            .GroupBy(v => v.CallSign.Trim(), StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .ToArray();
        VehicleConflicts = duplicates.Length == 0
            ? null
            : $"Doppelte Funkrufnamen: {string.Join(", ", duplicates)}";
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
        Einsatzarten = _einsatzarten.ToValues(),
        Links = _links.ToValues(),
        ChecklistTemplateAufbau = _checklistAufbau.ToValues(),
        ChecklistTemplateAbbau = _checklistAbbau.ToValues(),
        Personnel = _personnel.ToPeople(),
        Vehicles = _vehicles.ToValues(),
        Settings = _settings.ToSettings(),
        Einsatzgebiet = _einsatzgebiet.ToEinsatzgebiet(),
        // Streets are not editable here; _original carries them through unchanged.
    };

    [RelayCommand(CanExecute = nameof(CanSave))]
    private void Save()
    {
        _provider.Save(BuildSet());
        Load(); // reflect normalization (trim/dedupe, name-sorted personnel) and clear dirty
    }

    // Dirty alone is not enough: a duplicate Funkrufname must be resolved before the set can be
    // written (#76 follow-up).
    private bool CanSave => IsDirty && !HasVehicleConflicts;

    [RelayCommand(CanExecute = nameof(IsDirty))]
    private void Discard() => Load();

    private bool CanImport => !IsDirty && _originalIsEmpty;

    /// <summary>
    /// Bootstrap a fresh, empty install from a JSON file. Loads the file into the sections as unsaved
    /// changes for review — nothing reaches the database until the user presses Save. Imported streets
    /// ride along in <see cref="_original"/> (there is no streets section) and are persisted by Save.
    /// Offered only while the data is empty, so there is nothing to overwrite.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanImport))]
    private async Task Import()
    {
        FileError = null;
        var path = await _dialogs.PickImportJsonAsync();
        if (string.IsNullOrWhiteSpace(path)) return;

        MasterDataSet imported;
        try
        {
            imported = _files.Read(path);
        }
        catch (Exception ex)
        {
            FileError = $"Import fehlgeschlagen: {ex.Message}";
            return;
        }

        _original = imported;
        PopulateSections(imported);
        IsDirty = true; // user reviews, then Save (or Discard to revert to empty)
    }

    /// <summary>Writes the current editor contents (including unsaved edits and carried-through streets) to a JSON file.</summary>
    [RelayCommand]
    private async Task Export()
    {
        FileError = null;
        var path = await _dialogs.PickExportJsonAsync("stammdaten.json");
        if (string.IsNullOrWhiteSpace(path)) return;

        try
        {
            _files.Write(path, BuildSet());
            await _dialogs.ShareFileAsync(path, "application/json");
        }
        catch (Exception ex)
        {
            FileError = $"Export fehlgeschlagen: {ex.Message}";
        }
    }

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
