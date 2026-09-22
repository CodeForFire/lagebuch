using System.Collections.ObjectModel;
using System.Diagnostics.CodeAnalysis;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LageBuch.AppLogic.Services;
using LageBuch.Domain;
using LageBuch.Persistence.MasterData;

namespace LageBuch.AppLogic.ViewModels;

/// <summary>
/// The Stammdaten editor. Loads every editable category from the provider into its own section,
/// tracks a single dirty flag across them, and writes the whole set back on Save. Import (offered
/// only while the data is empty) fills the editor from a JSON file for review; Export writes the
/// current set back out.
/// </summary>
public sealed partial class MasterDataEditorViewModel : ObservableObject
{
    private readonly IMasterDataProvider _provider;
    private readonly IFileDialogService _dialogs;
    private readonly IMasterDataFileService _files;

    private MasterDataSet _original = MasterDataSet.Empty;
    private bool _originalIsEmpty = true;

    // Typed handles kept so BuildSet reads each section without fragile positional casts.
    private EditableListSection _roles = null!;

    // Typed handles kept so BuildSet reads each section without fragile positional casts.
    private EditableListSection _unitStatus = null!;

    // Typed handles kept so BuildSet reads each section without fragile positional casts.
    private TruppTypesSection _truppTypes = null!;

    private NavigationSection _navigation = null!;
    private LinksSection _links = null!;
    private PersonnelSection _personnel = null!;
    private VehiclesSection _vehicles = null!;
    private SettingsSection _settings = null!;

    public MasterDataEditorViewModel(IMasterDataProvider provider, IFileDialogService dialogs, IMasterDataFileService files)
    {
        _provider = provider;
        _dialogs = dialogs;
        _files = files;
        Load();
    }

    /// <summary>
    /// The app's own categories — a fixed, finite set. The rail's first group, shown unlabelled
    /// because the screen title already says what they are.
    /// </summary>
    public ObservableCollection<EditorSection> Sections { get; } = new();

    /// <summary>
    /// The Checklisten this brigade wrote itself — 0..n, in Stammdaten order, which is the
    /// operator's own rather than alphabetical. Kept apart from <see cref="Sections"/> so the rail
    /// can group them under a heading: holding both kinds in one collection is exactly why the
    /// view could not tell an entry the operator may rename and delete from one that is simply
    /// part of Lagebuch.
    /// </summary>
    public ObservableCollection<ChecklistTemplateSection> Checklists { get; } = new();

    /// <summary>
    /// The section the detail pane shows.
    /// </summary>
    /// <remarks>
    /// A null assignment is ignored: the editor always has exactly one section selected, and the
    /// rail is two ListBoxes over this one property. <c>SelectedItem</c> is a two-way direct
    /// property and Avalonia writes a target change straight back to the source with no
    /// re-entrancy guard, so the list that does <em>not</em> hold the selected item reports null
    /// here as it clears its own highlight. Honouring that null would instantly undo the selection
    /// the other list just made and leave both rails blank.
    /// </remarks>
    public EditorSection? SelectedSection
    {
        get => _selectedSection;
        set
        {
            if (value is null)
            {
                return;
            }

            SetProperty(ref _selectedSection, value);
        }
    }

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
    /// An informational note after a successful import: the legacy Wachen / Funkrufnamen entries
    /// of an older file that no vehicle or roster person covers, and which therefore were not taken
    /// over. Lets the user add a Fahrzeug for them before saving rather than losing them silently.
    /// Cleared when the editor reloads or the next import/export starts.
    /// </summary>
    [ObservableProperty]
    private string? _fileNotice;

    /// <summary>
    /// Fahrzeuge sind eindeutig (#76 follow-up): der Funkrufname identifiziert das Fahrzeug und darf
    /// nur einmal vorkommen — unabhängig von der Wache, getrimmt und ohne Groß-/Kleinschreibung.
    /// Doppelte benennt diese Meldung und blockiert das Speichern, statt sie still zu deduplizieren.
    /// </summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    private string? _vehicleConflicts;

    /// <summary>
    /// E-Mail-Adressen, die Lagebuch nicht öffnen würde. Eine Adresse aus den Stammdaten landet
    /// im Kontakte-Modul hinter einem Knopf, der sie an die Mail-App des Geräts übergibt, und
    /// <see cref="MailAddressValidator"/> lehnt dort ab, was in einer mailto:-URI etwas bedeutet.
    /// Das hier ist dieselbe Prüfung an der Stelle, an der die Adresse eingegeben wird -- sonst
    /// merkt es erst, wer an der Einsatzstelle jemanden erreichen will, und dort ist sie nicht
    /// mehr zu korrigieren.
    /// </summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    private string? _personnelConflicts;

    public bool HasVehicleConflicts => VehicleConflicts is not null;

    public bool HasPersonnelConflicts => PersonnelConflicts is not null;

    [ObservableProperty]
    private ConfirmDialogViewModel? _pendingConfirm;

    private void MarkDirty() => IsDirty = true;

    private void Load()
    {
        _original = _provider.Get();
        _originalIsEmpty = _original.IsEmpty;
        FileError = null;
        FileNotice = null;
        PopulateSections(_original);
        IsDirty = false;
        ImportCommand.NotifyCanExecuteChanged();
    }

    private void PopulateSections(MasterDataSet set)
    {
        // Restore by identity, not by index. Reload rebuilds every section object, and with the
        // Checklisten in their own group an index no longer names a section across a reload --
        // saving while editing "Aufbau ELW" would land you on Einstellungen. A Checkliste's id
        // survives Save where its position need not.
        var previousChecklistId = (SelectedSection as ChecklistTemplateSection)?.Id;
        var previousTitle = SelectedSection?.Title;

        Sections.Clear();
        Checklists.Clear();

        // Einstellungen and Navigation are meta sections (defaults, and what the Einsatz sidebar
        // shows), not data categories, so they stay pinned at the top in that order; the data
        // categories sort alphabetically below them.
        Sections.Add(_settings = new SettingsSection("Einstellungen", set.Settings, MarkDirty));
        Sections.Add(_navigation = new NavigationSection("Navigation", MarkDirty));

        EditorSection[] categories =
        {
            _roles = new EditableListSection("Rollen", "ROLLE", set.Roles, MarkDirty),
            _unitStatus = new EditableListSection("Einheiten-Status", "STATUS", set.UnitStatus, MarkDirty),
            _truppTypes = new TruppTypesSection("Trupp-Typen", set.TruppTypes, MarkDirty),
            _links = new LinksSection("Links", set.Links, MarkDirty),
            _personnel = new PersonnelSection("Personal", set.Personnel, OnPersonnelChanged),

            // Wachen and Funkrufnamen have no section of their own: they are derived from these
            // rows (plus the roster), so the vehicle list is the single place to maintain them.
            // The derived lists still serve as typing suggestions for further rows.
            _vehicles = new VehiclesSection("Fahrzeuge", set.Vehicles, set.Brigades, set.RadioCallSigns, OnVehiclesChanged),
        };

        foreach (var section in categories.OrderBy(s => s.Title, StringComparer.OrdinalIgnoreCase))
        {
            Sections.Add(section);
        }

        // The Checklisten are their own rail group, in Stammdaten order rather than alphabetical:
        // that order is the operator's own, expressed in the Navigation list.
        foreach (var template in set.ChecklistTemplates)
        {
            Checklists.Add(new ChecklistTemplateSection(template.Id, template.Title, template.Items, MarkDirty));
        }

        _navigation.Rebuild(set.Navigation, Checklists);

        SelectedSection = previousChecklistId is { } id
            ? Checklists.FirstOrDefault(c => c.Id == id) ?? Sections[0]
            : Sections.FirstOrDefault(s => s.Title == previousTitle) ?? Sections[0];

        // Here rather than in Load: Import populates the sections too, and used to leave both
        // conflict rules unevaluated until the operator happened to edit a row. An imported file
        // is exactly where a duplicate Funkrufname or an unusable address comes from, so the
        // answer has to be on screen before anything is touched.
        RefreshVehicleConflicts();
        RefreshPersonnelConflicts();
    }

    private void OnVehiclesChanged()
    {
        IsDirty = true;
        RefreshVehicleConflicts();
    }

    private void OnPersonnelChanged()
    {
        IsDirty = true;
        RefreshPersonnelConflicts();
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

    /// <summary>Recomputes the list of people whose address Lagebuch could not open.</summary>
    /// <remarks>
    /// Über <see cref="PersonnelSection.ToPeople"/> statt über die Zeilen: das ist, was SPEICHERN
    /// wirklich schreibt. Zeilen ohne Nachnamen fallen dort weg und eine nur aus Leerzeichen
    /// bestehende Adresse wird zu null -- beides darf keine Meldung über einen Wert auslösen, der
    /// nie in der Datei landet. Genannt wird die Person, nie die Adresse: die Meldung steht in
    /// einer schmalen Leiste, und der Name ist es, der die Zeile findet.
    /// </remarks>
    private void RefreshPersonnelConflicts()
    {
        var invalid = _personnel.ToPeople()
            .Where(p => p.HasEmail && !MailAddressValidator.TryGetMailtoUri(p.Email, out _))
            .Select(p => p.DisplayName)
            .ToArray();
        PersonnelConflicts = invalid.Length == 0
            ? null
            : $"Ungültige E-Mail-Adresse: {string.Join(", ", invalid)}";
    }

    private MasterDataSet BuildSet() => _original with
    {
        Roles = _roles.ToValues(),
        UnitStatus = _unitStatus.ToValues(),
        TruppTypes = _truppTypes.ToValues(),
        Links = _links.ToValues(),
        ChecklistTemplates = ChecklistTemplatesFromSections(),
        Navigation = _navigation.ToValues(),
        Personnel = _personnel.ToPeople(),
        Vehicles = _vehicles.ToValues(),
        Settings = _settings.ToSettings(),
    };

    /// <summary>
    /// A blank name gets a fallback rather than dropping the list: the operator cleared the name,
    /// not the items, and losing their Checkliste over it would be indefensible.
    /// </summary>
    private List<ChecklistTemplate> ChecklistTemplatesFromSections() =>
        Checklists
            .Select(c => new ChecklistTemplate(
                c.Id, ChecklistDefaults.TitleOrFallback(c.Title), c.ToValues()))
            .ToList();

    /// <summary>
    /// Adds an empty Checkliste and selects it, so the operator lands in the new list's editor
    /// with the name field ready.
    /// </summary>
    [RelayCommand]
    private void AddChecklist()
    {
        var section = new ChecklistTemplateSection(
            Guid.NewGuid(), "Neue Checkliste", Array.Empty<ChecklistTemplateItem>(), MarkDirty);
        Checklists.Add(section);

        // Rebuilt rather than appended to, so the new list reaches the Navigation layout: the
        // resolver's append rule rescues an Einsatz's own lists, never a layout's missing rows.
        _navigation.Rebuild(_navigation.ToValues(), Checklists);
        SelectedSection = section;
        MarkDirty();
    }

    /// <summary>
    /// Deletes a Checkliste template after confirming. Einsätze already started from it are
    /// untouched — each carries its own copy of the list, and the workspace appends any list its
    /// file holds that the layout no longer names.
    /// </summary>
    [RelayCommand]
    private void DeleteChecklist(ChecklistTemplateSection section)
    {
        if (section is null)
        {
            return;
        }

        var name = ChecklistDefaults.TitleOrFallback(section.Title);
        var message = $"„{name}“ wird aus den Stammdaten entfernt. "
            + "Bereits begonnene Einsätze behalten ihre Kopie.";
        var dialog = new ConfirmDialogViewModel(
            "Checkliste löschen?", message, "LÖSCHEN", () => RemoveChecklist(section));
        dialog.Closed += (_, _) => PendingConfirm = null;
        PendingConfirm = dialog;
    }

    private void RemoveChecklist(ChecklistTemplateSection section)
    {
        var index = Checklists.IndexOf(section);
        Checklists.Remove(section);
        _navigation.Rebuild(_navigation.ToValues(), Checklists);

        // The nearest surviving neighbour, else the rail entry directly above the group. Clamping
        // into Checklists unconditionally would index an empty collection when the last one goes.
        SelectedSection = Checklists.Count > 0
            ? Checklists[Math.Clamp(index, 0, Checklists.Count - 1)]
            : Sections[^1];
        MarkDirty();
    }

    [RelayCommand(CanExecute = nameof(CanSave))]
    private void Save()
    {
        _provider.Save(BuildSet());
        Load(); // reflect normalization (trim/dedupe, name-sorted personnel) and clear dirty
    }

    // Dirty alone is not enough: a duplicate Funkrufname must be resolved before the set can be
    // written (#76 follow-up).
    private bool CanSave => IsDirty && !HasVehicleConflicts && !HasPersonnelConflicts;

    [RelayCommand(CanExecute = nameof(IsDirty))]
    private void Discard() => Load();

    private bool CanImport => !IsDirty && _originalIsEmpty;

    /// <summary>
    /// Bootstrap a fresh, empty install from a JSON file. Loads the file into the sections as unsaved
    /// changes for review — nothing reaches the database until the user presses Save. Legacy
    /// Wachen / Funkrufnamen entries the file carries but nothing derives from are reported in
    /// <see cref="FileNotice"/>. Offered only while the data is empty, so there is nothing to overwrite.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanImport))]
    [SuppressMessage(
        "Design",
        "CA1031",
        Justification = "Imports read arbitrary user-chosen files; any parse/IO failure is shown as an error.")]
    private async Task Import()
    {
        FileError = null;
        FileNotice = null;

        // The pick itself can fail, not just the read: on Android the chosen content:// URI is
        // streamed into app-private storage before this returns, and a provider that hands back
        // nothing (or a full disk) faults the task rather than returning null.
        MasterDataImportResult imported;
        try
        {
            var path = await _dialogs.PickImportJsonAsync();
            if (string.IsNullOrWhiteSpace(path))
            {
                return;
            }

            imported = _files.Read(path);
        }
        catch (Exception ex)
        {
            FileError = $"Import fehlgeschlagen: {ex.Message}";
            return;
        }

        _original = imported.Set;
        PopulateSections(imported.Set);
        FileNotice = imported.DroppedLegacyEntries.Count == 0
            ? null
            : $"Nicht übernommen (kein Fahrzeug / keine Person dazu): {string.Join(", ", imported.DroppedLegacyEntries)}";
        IsDirty = true; // user reviews, then Save (or Discard to revert to empty)
    }

    /// <summary>Writes the current editor contents (including unsaved edits) to a JSON file.</summary>
    [RelayCommand]
    [SuppressMessage(
        "Design",
        "CA1031",
        Justification = "IO and share failures are shown as an error, never a crash.")]
    private async Task Export()
    {
        FileError = null;
        FileNotice = null;
        var path = await _dialogs.PickExportJsonAsync("stammdaten.json");
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

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
        ArgumentNullException.ThrowIfNull(proceed);
        if (!IsDirty)
        {
            proceed();
            return;
        }

        var dialog = new ConfirmDialogViewModel(
            "Änderungen verwerfen?",
            "Die Stammdaten wurden geändert. Beim Verlassen gehen die nicht gespeicherten Änderungen verloren.",
            "VERWERFEN",
            () =>
            {
                Load();
                proceed();
            });
        dialog.Closed += (_, _) => PendingConfirm = null;
        PendingConfirm = dialog;
    }
}
