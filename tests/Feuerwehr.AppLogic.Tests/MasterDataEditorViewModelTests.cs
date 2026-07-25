using Feuerwehr.AppLogic.Services;
using Feuerwehr.AppLogic.ViewModels;
using Feuerwehr.Persistence.MasterData;

namespace Feuerwehr.AppLogic.Tests;

public class MasterDataEditorViewModelTests
{
    private sealed class InMemoryProvider : IMasterDataProvider
    {
        private static readonly MasterDataSet DefaultSet = MasterDataSet.Empty with
        {
            Roles = new[] { "EL", "ZF" },
            Streets = new[] { new Street("Bahnhofstr.", "FFB") },
            Personnel = new[] { new Person("Mustermann", "Max", "ZF", "Land 1", "01 71 / 1 23 45 67") },
        };

        private MasterDataSet _set;

        public InMemoryProvider() : this(DefaultSet) { }

        public InMemoryProvider(MasterDataSet initial) => _set = initial;

        public int SaveCount { get; private set; }
        public MasterDataSet Get() => _set;
        public void Save(MasterDataSet set) { _set = set; SaveCount++; }
    }

    private sealed class FakeDialogs : IFileDialogService
    {
        public string? ImportPath { get; set; }
        public string? ExportPath { get; set; }
        public Task<string?> PickSaveAsync(string s) => Task.FromResult<string?>(null);
        public Task<string?> PickOpenAsync() => Task.FromResult<string?>(null);
        public Task<string?> PickExportPdfAsync(string s) => Task.FromResult<string?>(null);
        public Task<string?> PickImportJsonAsync() => Task.FromResult(ImportPath);
        public Task<string?> PickExportJsonAsync(string s) => Task.FromResult(ExportPath);
    }

    private sealed class FakeFileService : IMasterDataFileService
    {
        private readonly MasterDataSet? _read;
        private readonly Exception? _readError;

        public FakeFileService(MasterDataSet? read = null, Exception? readError = null)
        {
            _read = read;
            _readError = readError;
        }

        public string? WrittenPath { get; private set; }
        public MasterDataSet? Written { get; private set; }
        public bool WriteThrows { get; set; }

        public MasterDataSet Read(string path) =>
            _readError is not null ? throw _readError : _read ?? MasterDataSet.Empty;

        public void Write(string path, MasterDataSet set)
        {
            if (WriteThrows) throw new IOException("disk voll");
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

    private static PersonnelSection Personnel(MasterDataEditorViewModel vm) =>
        vm.Sections.OfType<PersonnelSection>().First(s => s.Title == "Personal");

    [Fact]
    public void Loads_a_section_per_category_and_starts_clean()
    {
        var vm = Vm(new InMemoryProvider());
        Assert.Equal(10, vm.Sections.Count);
        Assert.False(vm.IsDirty);
        Assert.False(vm.SaveCommand.CanExecute(null));
        Assert.NotNull(vm.SelectedSection);
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
    public void Save_persists_edits_carries_streets_through_and_clears_dirty()
    {
        var provider = new InMemoryProvider();
        var vm = Vm(provider);
        var roles = Roles(vm);
        roles.RemoveCommand.Execute(roles.Items.First(i => i.Value == "EL"));

        vm.SaveCommand.Execute(null);

        Assert.False(vm.IsDirty);
        Assert.Equal(1, provider.SaveCount);
        Assert.DoesNotContain("EL", provider.Get().Roles);
        Assert.Single(provider.Get().Streets); // streets untouched by the editor
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
    /// Every one of the ten sections must land in its own category on Save. Each section gets a
    /// marker value unique to that category, so a swapped mapping in BuildSet (e.g. writing
    /// _status where _unitStatus belongs) puts a marker in the wrong list and fails the assertion
    /// for the category that should have received it.
    /// </summary>
    [Fact]
    public void Save_maps_every_category_to_its_own_list_in_BuildSet()
    {
        var listTitles = new[]
        {
            "Rollen", "Status", "Einheiten-Status", "Ausrüstung", "Bezirke",
            "Wachen", "Funkrufnamen", "Trupp-Typen", "Checkliste",
        };

        var provider = new InMemoryProvider(MasterDataSet.Empty);
        var vm = Vm(provider);

        foreach (var title in listTitles)
        {
            var section = Section(vm, title);
            section.AddCommand.Execute(null);
            section.Items[^1].Value = $"MARK-{title}";
        }

        var personnel = Personnel(vm);
        personnel.AddCommand.Execute(null);
        personnel.Rows[^1].LastName = "MarkPersonal";

        vm.SaveCommand.Execute(null);

        var set = provider.Get();
        Assert.Contains("MARK-Rollen", set.Roles);
        Assert.Contains("MARK-Status", set.Status);
        Assert.Contains("MARK-Einheiten-Status", set.UnitStatus);
        Assert.Contains("MARK-Ausrüstung", set.Equipment);
        Assert.Contains("MARK-Bezirke", set.Districts);
        Assert.Contains("MARK-Wachen", set.Brigades);
        Assert.Contains("MARK-Funkrufnamen", set.RadioCallSigns);
        Assert.Contains("MARK-Trupp-Typen", set.TruppTypes);
        Assert.Contains("MARK-Checkliste", set.ChecklistTemplate);
        Assert.Contains(set.Personnel, p => p.LastName == "MarkPersonal");
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
        var vm = Vm(provider,
            new FakeDialogs { ImportPath = "/import.json" },
            new FakeFileService(read: imported));

        await vm.ImportCommand.ExecuteAsync(null);

        Assert.Equal(new[] { "EL", "ZF" }, Roles(vm).ToValues());
        Assert.True(vm.IsDirty);
        Assert.Equal(0, provider.SaveCount);          // nothing written until Save
        Assert.False(vm.ImportCommand.CanExecute(null)); // no second import while dirty
    }

    [Fact]
    public async Task Save_after_import_persists_the_imported_set_including_streets()
    {
        var provider = new InMemoryProvider(MasterDataSet.Empty);
        var imported = MasterDataSet.Empty with
        {
            Roles = new[] { "EL" },
            Streets = new[] { new Street("Bahnhofstr.", "FFB") }, // no streets UI: must ride through _original
        };
        var vm = Vm(provider,
            new FakeDialogs { ImportPath = "/import.json" },
            new FakeFileService(read: imported));

        await vm.ImportCommand.ExecuteAsync(null);
        vm.SaveCommand.Execute(null);

        Assert.Equal(1, provider.SaveCount);
        Assert.Equal(new[] { "EL" }, provider.Get().Roles);
        Assert.Contains(provider.Get().Streets, s => s.Name == "Bahnhofstr." && s.District == "FFB");
    }

    [Fact]
    public async Task A_failed_import_surfaces_an_error_and_writes_nothing()
    {
        var provider = new InMemoryProvider(MasterDataSet.Empty);
        var vm = Vm(provider,
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
    public async Task Export_writes_the_current_editor_contents_including_unsaved_edits_and_streets()
    {
        var provider = new InMemoryProvider(); // DefaultSet: EL, ZF + a street + a person
        var files = new FakeFileService();
        var vm = Vm(provider, new FakeDialogs { ExportPath = "/out.json" }, files);
        Section(vm, "Rollen").AddCommand.Execute(null);
        Section(vm, "Rollen").Items[^1].Value = "NEU"; // an unsaved edit

        await vm.ExportCommand.ExecuteAsync(null);

        Assert.Equal("/out.json", files.WrittenPath);
        Assert.NotNull(files.Written);
        Assert.Contains("NEU", files.Written!.Roles);
        Assert.Contains("EL", files.Written.Roles);
        Assert.Contains(files.Written.Streets, s => s.Name == "Bahnhofstr."); // carried through unchanged
    }

    [Fact]
    public async Task A_failed_export_surfaces_an_error()
    {
        var vm = Vm(new InMemoryProvider(),
            new FakeDialogs { ExportPath = "/out.json" },
            new FakeFileService { WriteThrows = true });

        await vm.ExportCommand.ExecuteAsync(null);

        Assert.NotNull(vm.FileError);
    }
}
