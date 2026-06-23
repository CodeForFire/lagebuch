using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using Feuerwehr.App.Views;
using Feuerwehr.AppLogic;
using Feuerwehr.AppLogic.Services;
using Feuerwehr.AppLogic.ViewModels;
using Feuerwehr.Domain;
using Feuerwehr.Domain.Time;
using Feuerwehr.Persistence.MasterData;

namespace Feuerwehr.Acceptance.Tests;

internal sealed class FakeStore : IIncidentStore
{
    private readonly Dictionary<string, Incident> _d = new();
    public void Save(string path, Incident incident) => _d[path] = incident;
    public Incident Load(string path) => _d[path];
}
internal sealed class FakeDialogs : IFileDialogService
{
    public Task<string?> PickSaveAsync(string s) => Task.FromResult<string?>("/x.fwincident");
    public Task<string?> PickOpenAsync() => Task.FromResult<string?>(null);
    public Task<string?> PickExportPdfAsync(string s) => Task.FromResult<string?>(null);
}
internal sealed class FixedClock : IClock
{
    public DateTimeOffset Now { get; set; } = new(2026, 6, 22, 9, 0, 0, TimeSpan.FromHours(2));
}

public class WorkspaceAcceptanceTests
{
    private static MasterDataSet Md() => new(
        new[] { "EL" }, Array.Empty<string>(), Array.Empty<string>(), Array.Empty<string>(),
        Array.Empty<string>(), Array.Empty<Street>(), new[] { "Blaulicht aus?" });

    private static IncidentWorkspaceViewModel BuildWorkspace(out IncidentSession session)
    {
        session = IncidentSession.StartNew(new FakeStore(), new FixedClock(),
            new SessionOperator("Müller", "FFB 12/1"), "/x.fwincident", new[] { "Blaulicht aus?" });
        return new IncidentWorkspaceViewModel(session, new FixedClock(), Md(), new FakeDialogs());
    }

    [AvaloniaFact]
    public void Workspace_renders_with_four_tabs()
    {
        var vm = BuildWorkspace(out _);
        var window = new Window { Content = new IncidentWorkspaceView { DataContext = vm }, Width = 1000, Height = 700 };
        window.Show();

        var tabs = window.GetVisualDescendants().OfType<TabControl>().Single();
        Assert.Equal(4, tabs.Items.Count);
    }

    [AvaloniaFact]
    public void Adding_etb_entry_via_ui_updates_the_grid()
    {
        var vm = BuildWorkspace(out var session);
        var view = new EtbView { DataContext = vm.Etb };
        var window = new Window { Content = view, Width = 800, Height = 600 };
        window.Show();

        var textBox = view.GetControl<TextBox>("EtbTextBox");
        var addButton = view.GetControl<Button>("EtbAddButton");

        textBox.Focus();
        window.KeyTextInput("Lagemeldung erhalten");
        // Ensure the binding pushed the value:
        Assert.Equal("Lagemeldung erhalten", vm.Etb.NewText);

        addButton.Command!.Execute(null);

        Assert.Single(session.Incident.Journal);
        Assert.Single(vm.Etb.Entries);
    }

    [AvaloniaFact]
    public void Closing_incident_shows_readonly_banner_and_disables_add()
    {
        var vm = BuildWorkspace(out _);
        var window = new Window { Content = new IncidentWorkspaceView { DataContext = vm }, Width = 1000, Height = 700 };
        window.Show();

        vm.CloseIncidentCommand.Execute(null);

        var banner = window.GetVisualDescendants().OfType<Border>()
            .Single(b => b.Name == "ReadOnlyBanner");
        Assert.True(banner.IsVisible);
        Assert.True(vm.Etb.IsReadOnly);
        Assert.False(vm.Etb.AddEntryCommand.CanExecute(null));
    }
}
