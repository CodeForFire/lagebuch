using LageBuch.AppLogic.Services;
using LageBuch.AppLogic.ViewModels;
using LageBuch.Persistence.MasterData;

namespace LageBuch.AppLogic.Tests;

public class MasterDataEditorViewModelTests
{
    private sealed class InMemoryProvider : IMasterDataProvider
    {
        private static readonly MasterDataSet DefaultSet = MasterDataSet.Empty with
        {
            Roles = new[] { "EL", "ZF" },
            Personnel = new[] { new Person("Mustermann", "Max", "ZF", "Land 1", "01 71 / 1 23 45 67") },
        };

        private MasterDataSet _set;

        public InMemoryProvider()
            : this(DefaultSet)
        {
        }

        public InMemoryProvider(MasterDataSet initial) => _set = initial;

        public int SaveCount { get; private set; }

        public MasterDataSet Get() => _set;

        public void Save(MasterDataSet set)
        {
            _set = set;
            SaveCount++;
        }
    }

    private sealed class FakeDialogs : IFileDialogService
    {
        public string? ImportPath { get; set; }

        public string? ExportPath { get; set; }

        public Task<string?> PickSaveAsync(string s, string? initialFolder = null) => Task.FromResult<string?>(null);

        public Task<string?> PickOpenAsync() => Task.FromResult<string?>(null);

        public Task<string?> PickExportPdfAsync(string s) => Task.FromResult<string?>(null);

        /// <summary>When set, the pick faults instead of returning a path — the Android shape.</summary>
        public Exception? ImportFailure { get; set; }

        public Task<string?> PickImportJsonAsync() => ImportFailure is null
            ? Task.FromResult(ImportPath)
            : Task.FromException<string?>(ImportFailure);

        public Task<string?> PickExportJsonAsync(string s) => Task.FromResult(ExportPath);

        public Task<string?> PickAttachmentAsync() => Task.FromResult<string?>(null);

        public Task OpenFileAsync(string path) => Task.CompletedTask;

        public Task OpenUrlAsync(string url) => Task.CompletedTask;

        public Task OpenMailAsync(string address) => Task.CompletedTask;

        public Task OpenPhoneAsync(string number) => Task.CompletedTask;

        public Task ShareFileAsync(string path, string mimeType) => Task.CompletedTask;
    }

    private sealed class FakeFileService : IMasterDataFileService
    {
        private readonly MasterDataSet? _read;
        private readonly IReadOnlyList<string> _dropped;
        private readonly Exception? _readError;

        public FakeFileService(MasterDataSet? read = null, Exception? readError = null, IReadOnlyList<string>? dropped = null)
        {
            _read = read;
            _readError = readError;
            _dropped = dropped ?? Array.Empty<string>();
        }

        public string? WrittenPath { get; private set; }

        public MasterDataSet? Written { get; private set; }

        public bool WriteThrows { get; set; }

        public MasterDataImportResult Read(string path) =>
            _readError is not null ? throw _readError : new MasterDataImportResult(_read ?? MasterDataSet.Empty, _dropped);

        public void Write(string path, MasterDataSet set)
        {
            if (WriteThrows)
            {
                throw new IOException("disk voll");
            }

            WrittenPath = path;
            Written = set;
        }
    }

    private static MasterDataEditorViewModel Vm(
        IMasterDataProvider provider, IFileDialogService? dialogs = null, IMasterDataFileService? files = null) =>
        new(provider, dialogs ?? new FakeDialogs(), files ?? new FakeFileService());

    private static EditableListSection Roles(MasterDataEditorViewModel vm) =>
        vm.Sections.OfType<EditableListSection>().First(s => s.Title == "Rollen");

    private static EditableListSection Section(MasterDataEditorViewModel vm, string title) =>
        vm.Sections.OfType<EditableListSection>().First(s => s.Title == title);

    private static ChecklistTemplateSection ChecklistSection(MasterDataEditorViewModel vm, string title) =>
        vm.Sections.OfType<ChecklistTemplateSection>().First(s => s.Title == title);

    private static LinksSection Links(MasterDataEditorViewModel vm) =>
        vm.Sections.OfType<LinksSection>().Single();

    private static PersonnelSection Personnel(MasterDataEditorViewModel vm) =>
        vm.Sections.OfType<PersonnelSection>().First(s => s.Title == "Personal");

    private static SettingsSection Settings(MasterDataEditorViewModel vm) =>
        vm.Sections.OfType<SettingsSection>().Single();

    private static VehiclesSection Vehicles(MasterDataEditorViewModel vm) =>
        vm.Sections.OfType<VehiclesSection>().Single();

    private static TruppTypesSection TruppTypes(MasterDataEditorViewModel vm) =>
        vm.Sections.OfType<TruppTypesSection>().Single();

    [Fact]
    public void Fahrzeuge_rows_suggest_the_waches_and_callsigns_derived_from_the_master_data()
    {
        // Dropdown-Vorschläge (#76): Wachen und Funkrufnamen sind aus den Fahrzeugen (und dem
        // Personal) abgeleitet -- Freitext bleibt trotzdem möglich (Fremdwehren), daher
        // AutoCompleteBox im View.
        var set = MasterDataSet.Empty with
        {
            Vehicles = new[]
            {
                new Vehicle("FFB Wache 1", "FFB 1/40/1", 9),
                new Vehicle("Aich", "Aich 42/1", 6),
            },
            Personnel = new[] { new Person("Mustermann", "Max", "ZF", "Land 1", null) },
        };
        var vm = Vm(new InMemoryProvider(set));
        var section = Vehicles(vm);

        var row = section.Rows[0];
        Assert.Equal(new[] { "FFB Wache 1", "Aich" }, row.WacheOptions);
        Assert.Equal(new[] { "FFB 1/40/1", "Aich 42/1", "Land 1" }, row.CallSignOptions);

        // A freshly added row carries the same suggestions.
        section.AddCommand.Execute(null);
        Assert.Equal(row.WacheOptions, section.Rows[2].WacheOptions);
        Assert.Equal(row.CallSignOptions, section.Rows[2].CallSignOptions);
    }

    [Fact]
    public void There_is_no_separate_Wachen_or_Funkrufnamen_section()
    {
        // Maintaining the Fahrzeuge alone must be sufficient: the two lists are derived.
        var vm = Vm(new InMemoryProvider());

        Assert.DoesNotContain(vm.Sections, s => s.Title is "Wachen" or "Funkrufnamen");
    }

    [Fact]
    public void Loads_a_section_per_category_and_starts_clean()
    {
        var vm = Vm(new InMemoryProvider());

        Assert.False(vm.IsDirty);
        Assert.False(vm.SaveCommand.CanExecute(null));
        Assert.NotNull(vm.SelectedSection);

        // Einstellungen and Navigation are pinned first, in that order -- both are meta sections
        // (defaults, and what the Einsatz sidebar shows) rather than data categories. The data
        // categories sort alphabetically below them. This provider has no Checklisten, and that is
        // now an ordinary state rather than an impossible one: a brigade may use none.
        Assert.Equal(
            new[]
            {
                "Einstellungen", "Navigation", "Einheiten-Status",
                "Fahrzeuge", "Links", "Personal", "Rollen", "Trupp-Typen",
            },
            vm.Sections.Select(s => s.Title));
    }

    [Fact]
    public void Editing_marks_dirty_and_enables_save()
    {
        var vm = Vm(new InMemoryProvider());
        Roles(vm).AddCommand.Execute(null);
        Assert.True(vm.IsDirty);
        Assert.True(vm.SaveCommand.CanExecute(null));
    }

    [Fact]
    public void Save_persists_edits_and_clears_dirty()
    {
        var provider = new InMemoryProvider();
        var vm = Vm(provider);
        var roles = Roles(vm);
        roles.RemoveCommand.Execute(roles.Items.First(i => i.Value == "EL"));

        vm.SaveCommand.Execute(null);

        Assert.False(vm.IsDirty);
        Assert.Equal(1, provider.SaveCount);
        Assert.DoesNotContain("EL", provider.Get().Roles);
    }

    [Fact]
    public void Discard_reverts_to_the_provider_and_clears_dirty()
    {
        var vm = Vm(new InMemoryProvider());
        Roles(vm).AddCommand.Execute(null);

        vm.DiscardCommand.Execute(null);

        Assert.False(vm.IsDirty);
        Assert.Equal(new[] { "EL", "ZF" }, Roles(vm).ToValues());
    }

    [Fact]
    public void ConfirmDiscardThen_runs_immediately_when_clean()
    {
        var vm = Vm(new InMemoryProvider());
        var proceeded = false;
        vm.ConfirmDiscardThen(() => proceeded = true);
        Assert.True(proceeded);
        Assert.Null(vm.PendingConfirm);
    }

    [Fact]
    public void ConfirmDiscardThen_prompts_when_dirty_and_proceeds_only_on_confirm()
    {
        var vm = Vm(new InMemoryProvider());
        Roles(vm).AddCommand.Execute(null);
        var proceeded = false;

        vm.ConfirmDiscardThen(() => proceeded = true);
        Assert.NotNull(vm.PendingConfirm);
        Assert.False(proceeded);

        vm.PendingConfirm!.ConfirmCommand.Execute(null);
        Assert.True(proceeded);
        Assert.Null(vm.PendingConfirm);
        Assert.False(vm.IsDirty); // discard happened as part of confirming
    }

    [Fact]
    public void ConfirmDiscardThen_cancel_keeps_edits_and_does_not_proceed()
    {
        var vm = Vm(new InMemoryProvider());
        Roles(vm).AddCommand.Execute(null);
        var proceeded = false;

        vm.ConfirmDiscardThen(() => proceeded = true);
        vm.PendingConfirm!.CancelCommand.Execute(null);

        Assert.False(proceeded);
        Assert.Null(vm.PendingConfirm);
        Assert.True(vm.IsDirty);
    }

    /// <summary>
    /// Every section must land in its own category on Save. Each section gets a
    /// marker value unique to that category, so a swapped mapping in BuildSet (e.g. writing
    /// _roles where _unitStatus belongs) puts a marker in the wrong list and fails the assertion
    /// for the category that should have received it.
    /// </summary>
    [Fact]
    public void Save_maps_every_category_to_its_own_list_in_BuildSet()
    {
        var listTitles = new[] { "Rollen", "Einheiten-Status" };

        var provider = new InMemoryProvider(MasterDataSet.Empty);
        var vm = Vm(provider);

        foreach (var title in listTitles)
        {
            var section = Section(vm, title);
            section.AddCommand.Execute(null);
            section.Items[^1].Value = $"MARK-{title}";
        }

        // An empty set has no Checklisten at all now, so they are created rather than looked up.
        vm.AddChecklistCommand.Execute(null);
        var checklistAufbau = (ChecklistTemplateSection)vm.SelectedSection!;
        checklistAufbau.Title = "Aufbau";
        checklistAufbau.AddCommand.Execute(null);
        checklistAufbau.Rows[^1].Text = "MARK-ChecklisteAufbau";

        vm.AddChecklistCommand.Execute(null);
        var checklistAbbau = (ChecklistTemplateSection)vm.SelectedSection!;
        checklistAbbau.Title = "Abbau";
        checklistAbbau.AddCommand.Execute(null);
        checklistAbbau.Rows[^1].Text = "MARK-ChecklisteAbbau";

        var personnel = Personnel(vm);
        personnel.AddCommand.Execute(null);
        personnel.Rows[^1].LastName = "MarkPersonal";

        var links = Links(vm);
        links.AddCommand.Execute(null);
        links.Rows[^1].Name = "MARK-Links";
        links.Rows[^1].Url = "https://example.org/mark";

        var vehicles = Vehicles(vm);
        vehicles.AddCommand.Execute(null);
        vehicles.Rows[^1].Wache = "MARK-Wache";
        vehicles.Rows[^1].CallSign = "MARK-Funkrufname";

        var truppTypes = TruppTypes(vm);
        truppTypes.AddCommand.Execute(null);
        truppTypes.Rows[^1].Name = "MARK-Trupp-Typen";
        truppTypes.Rows[^1].MemberCount = 3;
        truppTypes.Rows[^1].MaxDurationMinutes = 20;

        vm.SaveCommand.Execute(null);

        var set = provider.Get();
        Assert.Contains("MARK-Rollen", set.Roles);
        Assert.Contains("MARK-Einheiten-Status", set.UnitStatus);
        Assert.Contains(set.TruppTypes, t => t.Name == "MARK-Trupp-Typen" && t.MemberCount == 3 && t.MaxDurationMinutes == 20);
        Assert.Contains(set.Vehicles, v => v.Wache == "MARK-Wache" && v.CallSign == "MARK-Funkrufname");

        // The vehicle row is the one place Wachen and Funkrufnamen are maintained.
        Assert.Contains("MARK-Wache", set.Brigades);
        Assert.Contains("MARK-Funkrufname", set.RadioCallSigns);
        Assert.Contains(set.ChecklistTemplates.SelectMany(t => t.Items), i => i.Text == "MARK-ChecklisteAufbau");
        Assert.Contains(set.ChecklistTemplates.SelectMany(t => t.Items), i => i.Text == "MARK-ChecklisteAbbau");
        Assert.Contains(set.Personnel, p => p.LastName == "MarkPersonal");
        Assert.Contains(set.Links, l => l.Name == "MARK-Links" && l.Url == "https://example.org/mark");
    }

    [Fact]
    public void Save_persists_a_link_and_drops_rows_missing_a_name_or_url()
    {
        var provider = new InMemoryProvider(MasterDataSet.Empty);
        var vm = Vm(provider);
        var links = Links(vm);

        links.AddCommand.Execute(null);
        links.Rows[^1].Name = "Wetterdienst";
        links.Rows[^1].Url = "https://dwd.de";

        links.AddCommand.Execute(null);
        links.Rows[^1].Name = "Ohne URL";

        vm.SaveCommand.Execute(null);

        Assert.Equal(new Link("Wetterdienst", "https://dwd.de"), Assert.Single(provider.Get().Links));
    }

    [Fact]
    public void Settings_section_is_seeded_from_the_provider()
    {
        var provider = new InMemoryProvider(MasterDataSet.Empty with
        {
            Settings = new IncidentSettings(12, 33, 55),
        });
        var settings = Settings(Vm(provider));

        Assert.Equal(12, settings.IlsReminderIntervalMinutes);
        Assert.Equal(33, settings.IlsReminderFollowUpIntervalMinutes);
        Assert.Equal(55, settings.ReturnPressureBar);
    }

    [Fact]
    public void Editing_a_setting_marks_dirty_and_Save_persists_it()
    {
        var provider = new InMemoryProvider(MasterDataSet.Empty);
        var vm = Vm(provider);

        Settings(vm).ReturnPressureBar = 40;
        Assert.True(vm.IsDirty);

        vm.SaveCommand.Execute(null);

        Assert.Equal(1, provider.SaveCount);
        Assert.Equal(40, provider.Get().Settings.ReturnPressureBar);
    }

    [Fact]
    public void Editing_the_ILS_follow_up_interval_marks_dirty_and_Save_persists_it()
    {
        var provider = new InMemoryProvider(MasterDataSet.Empty);
        var vm = Vm(provider);

        Settings(vm).IlsReminderFollowUpIntervalMinutes = 45;
        Assert.True(vm.IsDirty);

        vm.SaveCommand.Execute(null);

        Assert.Equal(1, provider.SaveCount);
        Assert.Equal(45, provider.Get().Settings.IlsReminderFollowUpIntervalMinutes);
    }

    // --- Import / Export (issue #46 follow-up) ---
    [Fact]
    public void Import_is_disabled_when_master_data_already_exists()
    {
        var vm = Vm(new InMemoryProvider()); // DefaultSet is non-empty
        Assert.False(vm.ImportCommand.CanExecute(null));
    }

    [Fact]
    public void Import_is_enabled_on_a_fresh_empty_install()
    {
        var vm = Vm(new InMemoryProvider(MasterDataSet.Empty));
        Assert.True(vm.ImportCommand.CanExecute(null));
    }

    [Fact]
    public async Task Import_populates_the_sections_and_marks_dirty_without_saving()
    {
        var provider = new InMemoryProvider(MasterDataSet.Empty);
        var imported = MasterDataSet.Empty with { Roles = new[] { "EL", "ZF" } };
        var vm = Vm(
            provider,
            new FakeDialogs { ImportPath = "/import.json" },
            new FakeFileService(read: imported));

        await vm.ImportCommand.ExecuteAsync(null);

        Assert.Equal(new[] { "EL", "ZF" }, Roles(vm).ToValues());
        Assert.True(vm.IsDirty);
        Assert.Equal(0, provider.SaveCount);          // nothing written until Save
        Assert.False(vm.ImportCommand.CanExecute(null)); // no second import while dirty
    }

    [Fact]
    public async Task Save_after_import_persists_the_imported_set()
    {
        var provider = new InMemoryProvider(MasterDataSet.Empty);
        var imported = MasterDataSet.Empty with
        {
            Roles = new[] { "EL" },
        };
        var vm = Vm(
            provider,
            new FakeDialogs { ImportPath = "/import.json" },
            new FakeFileService(read: imported));

        await vm.ImportCommand.ExecuteAsync(null);
        vm.SaveCommand.Execute(null);

        Assert.Equal(1, provider.SaveCount);
        Assert.Equal(new[] { "EL" }, provider.Get().Roles);
    }

    [Fact]
    public async Task Import_names_legacy_entries_that_were_not_taken_over()
    {
        // An older export still carries "brigades"/"radioCallSigns". Entries no vehicle or person
        // covers are not imported; the notice names them so the user can add a Fahrzeug first.
        var vm = Vm(
            new InMemoryProvider(MasterDataSet.Empty),
            new FakeDialogs { ImportPath = "/alt.json" },
            new FakeFileService(
                read: MasterDataSet.Empty with { Vehicles = new[] { new Vehicle("FFB Wache 1", "FFB 1/40/1", 9) } },
                dropped: new[] { "Alt-Wache", "Leitstelle" }));

        await vm.ImportCommand.ExecuteAsync(null);

        Assert.Null(vm.FileError);
        Assert.Equal("Nicht übernommen (kein Fahrzeug / keine Person dazu): Alt-Wache, Leitstelle", vm.FileNotice);
        Assert.True(vm.IsDirty);

        // The notice is informational: it must not outlive the next reload.
        vm.DiscardCommand.Execute(null);
        Assert.Null(vm.FileNotice);
    }

    [Fact]
    public async Task Import_of_a_current_format_file_shows_no_notice()
    {
        var vm = Vm(
            new InMemoryProvider(MasterDataSet.Empty),
            new FakeDialogs { ImportPath = "/neu.json" },
            new FakeFileService(read: MasterDataSet.Empty with { Roles = new[] { "EL" } }));

        await vm.ImportCommand.ExecuteAsync(null);

        Assert.Null(vm.FileNotice);
    }

    [Fact]
    public async Task A_failed_import_surfaces_an_error_and_writes_nothing()
    {
        var provider = new InMemoryProvider(MasterDataSet.Empty);
        var vm = Vm(
            provider,
            new FakeDialogs { ImportPath = "/broken.json" },
            new FakeFileService(readError: new System.Text.Json.JsonException("kaputt")));

        await vm.ImportCommand.ExecuteAsync(null);

        Assert.NotNull(vm.FileError);
        Assert.False(vm.IsDirty);
        Assert.Equal(0, provider.SaveCount);
        Assert.True(vm.ImportCommand.CanExecute(null)); // still empty and clean: import stays available
    }

    [Fact]
    public async Task A_cancelled_import_dialog_changes_nothing()
    {
        var provider = new InMemoryProvider(MasterDataSet.Empty);
        var vm = Vm(provider, new FakeDialogs { ImportPath = null }, new FakeFileService());

        await vm.ImportCommand.ExecuteAsync(null);

        Assert.False(vm.IsDirty);
        Assert.Null(vm.FileError);
    }

    [Fact]
    public async Task Export_writes_the_current_editor_contents_including_unsaved_edits()
    {
        var provider = new InMemoryProvider(); // DefaultSet: EL, ZF + a person
        var files = new FakeFileService();
        var vm = Vm(provider, new FakeDialogs { ExportPath = "/out.json" }, files);
        Section(vm, "Rollen").AddCommand.Execute(null);
        Section(vm, "Rollen").Items[^1].Value = "NEU"; // an unsaved edit

        await vm.ExportCommand.ExecuteAsync(null);

        Assert.Equal("/out.json", files.WrittenPath);
        Assert.NotNull(files.Written);
        Assert.Contains("NEU", files.Written!.Roles);
        Assert.Contains("EL", files.Written.Roles);
    }

    [Fact]
    public async Task A_failed_export_surfaces_an_error()
    {
        var vm = Vm(
            new InMemoryProvider(),
            new FakeDialogs { ExportPath = "/out.json" },
            new FakeFileService { WriteThrows = true });

        await vm.ExportCommand.ExecuteAsync(null);

        Assert.NotNull(vm.FileError);
    }

    // Fahrzeuge sind eindeutig (#76 follow-up): der Funkrufname identifiziert das Fahrzeug, daher
    // darf er nur einmal vorkommen -- unabhängig von der Wache. Doppelte blockieren das Speichern,
    // statt still dedupliziert zu werden.
    private static MasterDataSet SetWithVehicles(params Vehicle[] vehicles) => MasterDataSet.Empty with
    {
        Vehicles = vehicles,
    };

    [Fact]
    public void A_duplicate_call_sign_blocks_saving_and_names_the_conflict()
    {
        var set = SetWithVehicles(
            new Vehicle("FFB Wache 1", "FFB 1/40/1", 9),
            new Vehicle("Aich", "Aich 42/1", 6));
        var vm = Vm(new InMemoryProvider(set));
        var section = Vehicles(vm);

        section.AddCommand.Execute(null);
        section.Rows[2].Wache = "FFB Wache 1";
        section.Rows[2].CallSign = "FFB 1/40/1"; // same call sign, different row

        Assert.False(vm.SaveCommand.CanExecute(null));
        Assert.True(vm.HasVehicleConflicts);
        Assert.Contains("FFB 1/40/1", vm.VehicleConflicts, StringComparison.Ordinal);
    }

    [Fact]
    public void The_call_sign_is_unique_across_waches_and_case_insensitively()
    {
        var set = SetWithVehicles(
            new Vehicle("FFB Wache 1", "FFB 1/40/1", 9),
            new Vehicle("Aich", "Aich 42/1", 6));
        var vm = Vm(new InMemoryProvider(set));
        var section = Vehicles(vm);

        section.AddCommand.Execute(null);
        section.Rows[2].Wache = "Aich"; // different Wache does not rescue the duplicate
        section.Rows[2].CallSign = "ffb 1/40/1";

        Assert.False(vm.SaveCommand.CanExecute(null));
    }

    [Fact]
    public void Empty_call_signs_never_count_as_duplicates()
    {
        var set = SetWithVehicles(new Vehicle("FFB Wache 1", string.Empty, 9), new Vehicle("Aich", "  ", 6));
        var vm = Vm(new InMemoryProvider(set));
        var section = Vehicles(vm);
        section.AddCommand.Execute(null); // third blank row

        Assert.Null(vm.VehicleConflicts);
        Assert.False(vm.HasVehicleConflicts);
        Assert.True(vm.SaveCommand.CanExecute(null)); // dirty from the added row
    }

    [Fact]
    public void Fixing_the_duplicate_re_enables_saving()
    {
        var set = SetWithVehicles(
            new Vehicle("FFB Wache 1", "FFB 1/40/1", 9),
            new Vehicle("Aich", "FFB 1/40/1", 6));
        var vm = Vm(new InMemoryProvider(set));

        Assert.False(vm.SaveCommand.CanExecute(null));
        Assert.Contains("FFB 1/40/1", vm.VehicleConflicts, StringComparison.Ordinal);

        Vehicles(vm).Rows[1].CallSign = "Aich 42/1";

        Assert.Null(vm.VehicleConflicts);
        Assert.True(vm.SaveCommand.CanExecute(null));
    }

    [Fact]
    public async Task Import_surfaces_a_failing_picker_instead_of_faulting_the_command()
    {
        // The pick itself can fail, not only the read: on Android it streams the chosen
        // content:// URI into app-private storage before returning. (#302)
        var dialogs = new FakeDialogs { ImportFailure = new IOException("Datei nicht lesbar.") };
        var vm = new MasterDataEditorViewModel(new FakeMasterData(), dialogs, new NoFiles());

        await vm.ImportCommand.ExecuteAsync(null);

        Assert.Equal("Import fehlgeschlagen: Datei nicht lesbar.", vm.FileError);
    }
}
