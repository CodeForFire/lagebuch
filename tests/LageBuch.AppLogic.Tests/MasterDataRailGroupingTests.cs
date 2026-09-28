using LageBuch.AppLogic.Services;
using LageBuch.AppLogic.ViewModels;
using LageBuch.Domain;
using LageBuch.Persistence.MasterData;

namespace LageBuch.AppLogic.Tests;

// The editor's rail holds two kinds of entry that used to look identical: the app's own fixed
// categories, and the Checklisten a brigade wrote itself. They are separate collections now, so
// the rail can group them -- and so the distinction lives in the view model rather than being
// painted on at render time.
public class MasterDataRailGroupingTests
{
    private sealed class InMemoryProvider : IMasterDataProvider
    {
        private MasterDataSet _set;

        public InMemoryProvider(MasterDataSet? set = null) => _set = set ?? MasterDataSet.Empty;

        public MasterDataSet Get() => _set;

        public void Save(MasterDataSet set) => _set = set;
    }

    private sealed class NoFiles : IMasterDataFileService
    {
        public MasterDataImportResult Read(string path) => new(MasterDataSet.Empty, Array.Empty<string>());

        public void Write(string path, MasterDataSet set)
        {
        }
    }

    private sealed class Dialogs : IFileDialogService
    {
        public Task<string?> PickSaveAsync(string s, string? initialFolder = null) => Task.FromResult<string?>(null);

        public Task<string?> PickOpenAsync() => Task.FromResult<string?>(null);

        public Task<string?> PickExportPdfAsync(string s) => Task.FromResult<string?>(null);

        public Task<string?> PickImportJsonAsync() => Task.FromResult<string?>(null);

        public Task<string?> PickExportJsonAsync(string s) => Task.FromResult<string?>(null);

        public Task<string?> PickAttachmentAsync() => Task.FromResult<string?>(null);

        public Task OpenFileAsync(string path) => Task.CompletedTask;

        public Task OpenUrlAsync(string url) => Task.CompletedTask;

        public Task OpenMailAsync(string address) => Task.CompletedTask;

        public Task OpenPhoneAsync(string number) => Task.CompletedTask;

        public Task ShareFileAsync(string path, string mimeType) => Task.CompletedTask;
    }

    private static readonly Guid Nachbereitung = new("b7f3c611-2a58-4c9e-8d40-5f1a2b3c4d5e");

    private static MasterDataSet WithTwoChecklists() => MasterDataSet.Empty with
    {
        ChecklistTemplates = new[]
        {
            new ChecklistTemplate(
                ChecklistDefaults.AufbauListId,
                "Aufbau ELW",
                new[] { new ChecklistTemplateItem("Aufstellort ELW frei?", true) }),
            new ChecklistTemplate(
                Nachbereitung,
                "Nachbereitung",
                new[] { new ChecklistTemplateItem("Bericht geschrieben?", false) }),
        },
    };

    private static MasterDataEditorViewModel Vm(MasterDataSet? set = null) =>
        new(new InMemoryProvider(set), new Dialogs(), new NoFiles());

    [Fact]
    public void The_fixed_categories_are_the_only_thing_in_Sections()
    {
        var vm = Vm(WithTwoChecklists());

        Assert.Equal(
            new[]
            {
                "Einstellungen", "Navigation", "Einheiten-Status",
                "Fahrzeuge", "Links", "Personal", "Rollen", "Trupp-Typen",
            },
            vm.Sections.Select(s => s.Title));
        Assert.DoesNotContain(vm.Sections, s => s is ChecklistTemplateSection);
    }

    [Fact]
    public void The_checklisten_are_their_own_group_in_stammdaten_order()
    {
        var vm = Vm(WithTwoChecklists());

        Assert.Equal(new[] { "Aufbau ELW", "Nachbereitung" }, vm.Checklists.Select(c => c.Title));
    }

    [Fact]
    public void A_brigade_with_no_checklisten_has_an_empty_group_not_a_missing_one()
    {
        var vm = Vm();

        Assert.Empty(vm.Checklists);
        Assert.Equal(8, vm.Sections.Count);
    }

    // The rail is two ListBoxes over one SelectedSection. Avalonia's SelectedItem is a two-way
    // direct property with no re-entrancy guard, so the list that does NOT hold the selection
    // writes null here as it clears its own highlight. Honouring that null would undo the
    // selection the other list just made, leaving both rails blank.
    [Fact]
    public void Selecting_nothing_is_ignored_so_the_two_rails_cannot_cancel_each_other()
    {
        var vm = Vm(WithTwoChecklists());
        var checklist = vm.Checklists[0];

        vm.SelectedSection = checklist;
        vm.SelectedSection = null;

        Assert.Same(checklist, vm.SelectedSection);
    }

    // Reload rebuilds every section object, so restoring by index would land on whatever now
    // occupies that slot -- and a Checkliste's index in its own group says nothing about where it
    // was. Identity is what survives a Save.
    [Fact]
    public void Saving_while_editing_a_checklist_keeps_that_checklist_selected()
    {
        var vm = Vm(WithTwoChecklists());
        vm.SelectedSection = vm.Checklists[1];
        vm.Checklists[1].AddCommand.Execute(null);

        vm.SaveCommand.Execute(null);

        var selected = Assert.IsType<ChecklistTemplateSection>(vm.SelectedSection);
        Assert.Equal(Nachbereitung, selected.Id);
    }

    [Fact]
    public void Saving_while_editing_a_fixed_category_keeps_that_category_selected()
    {
        var vm = Vm(WithTwoChecklists());
        var roles = (EditableListSection)vm.Sections.Single(s => s.Title == "Rollen");
        vm.SelectedSection = roles;
        roles.AddCommand.Execute(null);

        vm.SaveCommand.Execute(null);

        Assert.Equal("Rollen", vm.SelectedSection!.Title);
    }

    // Deleting the last Checkliste empties the group the selection was clamped into. Clamping
    // into an empty collection is an ArgumentOutOfRangeException, not a no-op.
    [Fact]
    public void Deleting_the_only_checklist_selects_a_surviving_section_instead_of_throwing()
    {
        var vm = Vm(MasterDataSet.Empty with
        {
            ChecklistTemplates = new[]
            {
                new ChecklistTemplate(Nachbereitung, "Nachbereitung", Array.Empty<ChecklistTemplateItem>()),
            },
        });
        var only = Assert.Single(vm.Checklists);
        vm.SelectedSection = only;

        vm.DeleteChecklistCommand.Execute(only);
        vm.PendingConfirm!.ConfirmCommand.Execute(null);

        Assert.Empty(vm.Checklists);
        Assert.NotNull(vm.SelectedSection);
        Assert.Contains(vm.SelectedSection!, vm.Sections);
    }

    [Fact]
    public void Deleting_one_of_several_checklists_selects_a_neighbour()
    {
        var vm = Vm(WithTwoChecklists());
        var first = vm.Checklists[0];
        vm.SelectedSection = first;

        vm.DeleteChecklistCommand.Execute(first);
        vm.PendingConfirm!.ConfirmCommand.Execute(null);

        Assert.Equal("Nachbereitung", Assert.Single(vm.Checklists).Title);
        Assert.Same(vm.Checklists[0], vm.SelectedSection);
    }
}
