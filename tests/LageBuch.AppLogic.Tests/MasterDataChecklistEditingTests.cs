using LageBuch.AppLogic.Services;
using LageBuch.AppLogic.ViewModels;
using LageBuch.Domain;
using LageBuch.Persistence.MasterData;

namespace LageBuch.AppLogic.Tests;

// Checklisten and the rail layout are user data now, so the editor has to create, rename, reorder
// and delete them -- and keep the Navigation list in step, because a Checkliste that never reaches
// the layout would be invisible in every Einsatz.
public class MasterDataChecklistEditingTests
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

    private sealed class ConfirmingDialogs : IFileDialogService
    {
        public Task<string?> PickSaveAsync(string s, string? initialFolder = null) => Task.FromResult<string?>(null);

        public Task<string?> PickOpenAsync() => Task.FromResult<string?>(null);

        public Task<string?> PickExportPdfAsync(string s) => Task.FromResult<string?>(null);

        public Task<string?> PickImportJsonAsync() => Task.FromResult<string?>(null);

        public Task<string?> PickExportJsonAsync(string s) => Task.FromResult<string?>(null);

        public Task<string?> PickAttachmentAsync() => Task.FromResult<string?>(null);

        public Task OpenFileAsync(string path) => Task.CompletedTask;

        public Task OpenUrlAsync(string url) => Task.CompletedTask;

        public Task ShareFileAsync(string path, string mimeType) => Task.CompletedTask;
    }

    private static MasterDataEditorViewModel Vm(MasterDataSet? set = null) =>
        new(new InMemoryProvider(set), new ConfirmingDialogs(), new NoFiles());

    private static NavigationSection Nav(MasterDataEditorViewModel vm) =>
        vm.Sections.OfType<NavigationSection>().Single();

    /// <summary>
    /// Saves, then reads the set back out of the reloaded sections — Save writes through the
    /// provider and reloads, so this is what actually reached Stammdaten.
    /// </summary>
    private static MasterDataSet SavedSet(MasterDataEditorViewModel vm)
    {
        vm.SaveCommand.Execute(null);
        return MasterDataSet.Empty with
        {
            ChecklistTemplates = vm.Sections.OfType<ChecklistTemplateSection>()
                .Select(c => new ChecklistTemplate(c.Id, c.Title, c.ToValues())).ToList(),
            Navigation = Nav(vm).ToValues(),
        };
    }

    [Fact]
    public void A_fresh_install_has_no_checklisten_and_that_is_a_valid_state()
    {
        var vm = Vm();

        Assert.Empty(vm.Sections.OfType<ChecklistTemplateSection>());
        Assert.Contains(vm.Sections, s => s.Title == "Navigation");
    }

    [Fact]
    public void Adding_a_checklist_creates_a_section_selects_it_and_marks_dirty()
    {
        var vm = Vm();

        vm.AddChecklistCommand.Execute(null);

        var section = Assert.Single(vm.Sections.OfType<ChecklistTemplateSection>());
        Assert.Equal("Neue Checkliste", section.Title);
        Assert.Same(section, vm.SelectedSection);
        Assert.True(vm.IsDirty);
    }

    // The append rule in NavigationLayout rescues an Einsatz's own lists, never a layout's missing
    // rows -- so a new Checkliste has to reach the Navigation list here, or it is invisible.
    [Fact]
    public void A_new_checklist_appears_in_the_navigation_list()
    {
        var vm = Vm();

        vm.AddChecklistCommand.Execute(null);

        var row = Assert.Single(Nav(vm).Rows, r => r.ChecklistId is not null);
        Assert.Equal("Neue Checkliste", row.Label);
        Assert.True(row.IsVisible);
    }

    [Fact]
    public void Renaming_a_checklist_renames_its_rail_entry_and_its_navigation_row()
    {
        var vm = Vm();
        vm.AddChecklistCommand.Execute(null);
        var section = Assert.Single(vm.Sections.OfType<ChecklistTemplateSection>());

        section.Title = "Nachbereitung";

        Assert.Equal("Nachbereitung", vm.Sections.OfType<ChecklistTemplateSection>().Single().Title);
        Assert.Equal("Nachbereitung", Assert.Single(Nav(vm).Rows, r => r.ChecklistId is not null).Label);
    }

    [Fact]
    public void Renaming_marks_the_editor_dirty()
    {
        var vm = Vm();
        vm.AddChecklistCommand.Execute(null);
        vm.SaveCommand.Execute(null);
        Assert.False(vm.IsDirty);

        vm.Sections.OfType<ChecklistTemplateSection>().Single().Title = "Nachbereitung";

        Assert.True(vm.IsDirty);
    }

    [Fact]
    public void Deleting_a_checklist_asks_first_and_does_nothing_until_confirmed()
    {
        var vm = Vm();
        vm.AddChecklistCommand.Execute(null);
        var section = Assert.Single(vm.Sections.OfType<ChecklistTemplateSection>());

        vm.DeleteChecklistCommand.Execute(section);

        Assert.NotNull(vm.PendingConfirm);
        Assert.Single(vm.Sections.OfType<ChecklistTemplateSection>());
    }

    [Fact]
    public void Confirming_the_delete_removes_the_section_and_its_navigation_row()
    {
        var vm = Vm();
        vm.AddChecklistCommand.Execute(null);
        var section = Assert.Single(vm.Sections.OfType<ChecklistTemplateSection>());
        vm.DeleteChecklistCommand.Execute(section);

        vm.PendingConfirm!.ConfirmCommand.Execute(null);

        Assert.Empty(vm.Sections.OfType<ChecklistTemplateSection>());
        Assert.DoesNotContain(Nav(vm).Rows, r => r.ChecklistId is not null);
        Assert.NotNull(vm.SelectedSection);
    }

    [Fact]
    public void A_blank_name_falls_back_rather_than_losing_the_list()
    {
        var vm = Vm();
        vm.AddChecklistCommand.Execute(null);
        var section = Assert.Single(vm.Sections.OfType<ChecklistTemplateSection>());
        section.AddCommand.Execute(null);
        section.Rows[0].Text = "Bericht schreiben";
        section.Title = "   ";

        var saved = SavedSet(vm);

        var template = Assert.Single(saved.ChecklistTemplates);
        Assert.Equal(ChecklistDefaults.FallbackTitle, template.Title);
        Assert.Equal("Bericht schreiben", Assert.Single(template.Items).Text);
    }

    [Fact]
    public void A_checklist_keeps_its_id_across_a_rename_and_a_save()
    {
        var vm = Vm();
        vm.AddChecklistCommand.Execute(null);
        var id = vm.Sections.OfType<ChecklistTemplateSection>().Single().Id;
        vm.Sections.OfType<ChecklistTemplateSection>().Single().Title = "Nachbereitung";

        vm.SaveCommand.Execute(null);

        Assert.Equal(id, vm.Sections.OfType<ChecklistTemplateSection>().Single().Id);
    }

    // --- Navigation ---
    [Fact]
    public void The_navigation_list_covers_every_module_and_every_checklist()
    {
        var vm = Vm();
        vm.AddChecklistCommand.Execute(null);

        var rows = Nav(vm).Rows;

        Assert.Equal(NavModules.All.Count + 1, rows.Count);
        Assert.Equal(1, rows.Count(r => r.ChecklistId is not null));
    }

    [Fact]
    public void The_etb_row_cannot_be_hidden()
    {
        var vm = Vm();

        var etb = Assert.Single(Nav(vm).Rows, r => r.ModuleKey == NavModules.Etb);

        Assert.False(etb.CanHide);
        Assert.True(etb.IsVisible);
    }

    [Fact]
    public void Every_other_module_can_be_hidden()
    {
        var vm = Vm();

        Assert.All(
            Nav(vm).Rows.Where(r => r.ModuleKey != NavModules.Etb),
            r => Assert.True(r.CanHide));
    }

    [Fact]
    public void Switching_a_module_off_marks_dirty_and_survives_a_save()
    {
        var vm = Vm();
        var scba = Nav(vm).Rows.Single(r => r.ModuleKey == NavModules.Scba);

        scba.IsVisible = false;

        Assert.True(vm.IsDirty);
        Assert.False(Assert.Single(SavedSet(vm).Navigation, e => e.ModuleKey == NavModules.Scba).IsVisible);
    }

    [Fact]
    public void Reordering_moves_the_row_and_marks_dirty()
    {
        var vm = Vm();
        var nav = Nav(vm);
        var second = nav.Rows[1];

        nav.MoveUpCommand.Execute(second);

        Assert.Same(second, nav.Rows[0]);
        Assert.True(vm.IsDirty);
    }

    [Fact]
    public void Moving_the_first_row_up_does_nothing()
    {
        var vm = Vm();
        var nav = Nav(vm);
        var first = nav.Rows[0];

        nav.MoveUpCommand.Execute(first);

        Assert.Same(first, nav.Rows[0]);
        Assert.False(vm.IsDirty);
    }

    [Fact]
    public void A_stored_layout_is_reproduced_in_order()
    {
        var set = MasterDataSet.Empty with
        {
            Navigation = new[]
            {
                new NavEntry(NavModules.Files, null, true),
                new NavEntry(NavModules.Etb, null, true),
            },
        };

        var rows = Nav(Vm(set)).Rows;

        Assert.Equal(NavModules.Files, rows[0].ModuleKey);
        Assert.Equal(NavModules.Etb, rows[1].ModuleKey);
        Assert.Equal(NavModules.All.Count, rows.Count);
    }

    [Fact]
    public void An_entry_naming_a_checklist_that_no_longer_exists_is_dropped()
    {
        var set = MasterDataSet.Empty with
        {
            Navigation = new[]
            {
                new NavEntry(NavModules.Etb, null, true),
                new NavEntry(NavModules.Checklist, new Guid("33333333-3333-3333-3333-333333333333"), true),
            },
        };

        Assert.DoesNotContain(Nav(Vm(set)).Rows, r => r.ChecklistId is not null);
    }
}
