using Feuerwehr.AppLogic.Services;
using Feuerwehr.AppLogic.ViewModels;
using Feuerwehr.Persistence.MasterData;

namespace Feuerwehr.AppLogic.Tests;

public class MasterDataEditorViewModelTests
{
    private sealed class InMemoryProvider : IMasterDataProvider
    {
        private MasterDataSet _set = MasterDataSet.Empty with
        {
            Roles = new[] { "EL", "ZF" },
            Streets = new[] { new Street("Bahnhofstr.", "FFB") },
            Personnel = new[] { new Person("Mustermann", "Max", "ZF", "Land 1", "01 71 / 1 23 45 67") },
        };
        public int SaveCount { get; private set; }
        public MasterDataSet Get() => _set;
        public void Save(MasterDataSet set) { _set = set; SaveCount++; }
    }

    private static EditableListSection Roles(MasterDataEditorViewModel vm) =>
        vm.Sections.OfType<EditableListSection>().First(s => s.Title == "Rollen");

    [Fact]
    public void Loads_a_section_per_category_and_starts_clean()
    {
        var vm = new MasterDataEditorViewModel(new InMemoryProvider());
        Assert.Equal(10, vm.Sections.Count);
        Assert.False(vm.IsDirty);
        Assert.False(vm.SaveCommand.CanExecute(null));
        Assert.NotNull(vm.SelectedSection);
    }

    [Fact]
    public void Editing_marks_dirty_and_enables_save()
    {
        var vm = new MasterDataEditorViewModel(new InMemoryProvider());
        Roles(vm).AddCommand.Execute(null);
        Assert.True(vm.IsDirty);
        Assert.True(vm.SaveCommand.CanExecute(null));
    }

    [Fact]
    public void Save_persists_edits_carries_streets_through_and_clears_dirty()
    {
        var provider = new InMemoryProvider();
        var vm = new MasterDataEditorViewModel(provider);
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
        var vm = new MasterDataEditorViewModel(new InMemoryProvider());
        Roles(vm).AddCommand.Execute(null);

        vm.DiscardCommand.Execute(null);

        Assert.False(vm.IsDirty);
        Assert.Equal(new[] { "EL", "ZF" }, Roles(vm).ToValues());
    }

    [Fact]
    public void ConfirmDiscardThen_runs_immediately_when_clean()
    {
        var vm = new MasterDataEditorViewModel(new InMemoryProvider());
        var proceeded = false;
        vm.ConfirmDiscardThen(() => proceeded = true);
        Assert.True(proceeded);
        Assert.Null(vm.PendingConfirm);
    }

    [Fact]
    public void ConfirmDiscardThen_prompts_when_dirty_and_proceeds_only_on_confirm()
    {
        var vm = new MasterDataEditorViewModel(new InMemoryProvider());
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
        var vm = new MasterDataEditorViewModel(new InMemoryProvider());
        Roles(vm).AddCommand.Execute(null);
        var proceeded = false;

        vm.ConfirmDiscardThen(() => proceeded = true);
        vm.PendingConfirm!.CancelCommand.Execute(null);

        Assert.False(proceeded);
        Assert.Null(vm.PendingConfirm);
        Assert.True(vm.IsDirty);
    }
}
