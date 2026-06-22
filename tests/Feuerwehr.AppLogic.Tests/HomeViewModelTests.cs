using Feuerwehr.AppLogic.Services;
using Feuerwehr.AppLogic.ViewModels;
using Feuerwehr.Domain;
using Feuerwehr.Persistence.MasterData;

namespace Feuerwehr.AppLogic.Tests;

internal sealed class FakeMasterData : IMasterDataProvider
{
    public MasterDataSet Get() => new(
        Roles: new[] { "EL" }, Status: Array.Empty<string>(), Equipment: Array.Empty<string>(),
        Districts: Array.Empty<string>(), RadioCallSigns: Array.Empty<string>(),
        Streets: Array.Empty<Street>(), ChecklistTemplate: new[] { "A?" });
}

internal sealed class FakeRecent : IRecentFilesStore
{
    private readonly List<string> _list = new();
    public IReadOnlyList<string> GetRecent() => _list;
    public void Add(string path) { _list.Remove(path); _list.Insert(0, path); }
}

public class HomeViewModelTests
{
    private static readonly DateTimeOffset T0 = new(2026, 6, 22, 9, 0, 0, TimeSpan.FromHours(2));

    [Fact]
    public void NewIncident_opens_workspace_and_adds_to_recent()
    {
        var store = new FakeStore();
        var recent = new FakeRecent();
        var dialogs = new FakeDialogs(); // PickSaveAsync returns "/x.fwincident"
        var vm = new HomeViewModel(store, new FakeMasterData(), recent, dialogs, new FixedClock(T0));

        IncidentWorkspaceViewModel? opened = null;
        vm.WorkspaceOpened = ws => opened = ws;

        vm.NewIncidentCommand.Execute(new SessionOperator("Müller"));

        Assert.NotNull(opened);
        Assert.False(opened!.IsReadOnly);
        Assert.Contains("/x.fwincident", recent.GetRecent());
        Assert.Equal("A?", opened.Checklist.Items[0].Text); // checklist template seeded
    }

    [Fact]
    public void OpenRecent_of_closed_incident_opens_readonly()
    {
        var store = new FakeStore();
        var clock = new FixedClock(T0);
        var seed = IncidentSession.StartNew(store, clock, new SessionOperator("Müller"), "/x.fwincident", Array.Empty<string>());
        seed.Close(clock);

        var recent = new FakeRecent();
        var vm = new HomeViewModel(store, new FakeMasterData(), recent, new FakeDialogs(), clock);
        IncidentWorkspaceViewModel? opened = null;
        vm.WorkspaceOpened = ws => opened = ws;

        vm.OpenRecentCommand.Execute("/x.fwincident");

        Assert.NotNull(opened);
        Assert.True(opened!.IsReadOnly);
    }
}
