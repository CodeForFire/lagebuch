using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using LageBuch.App.Shared.Views;
using LageBuch.AppLogic;
using LageBuch.AppLogic.Services;
using LageBuch.AppLogic.ViewModels;
using LageBuch.Domain;
using LageBuch.Domain.Etb;
using LageBuch.Persistence.MasterData;
using Microsoft.Data.Sqlite;

namespace LageBuch.Acceptance.Tests;

// #598: a closed incident names its seal in the read-only banner, and a file that no longer
// matches it shows a warning above the (still viewable) incident. Set RENDER_OUT to keep the PNGs.
public sealed class IncidentSealRenderTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("lagebuch-seal-render-").FullName;

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        Directory.Delete(_dir, recursive: true);
    }

    [AvaloniaFact]
    public async Task A_sealed_closed_incident_shows_its_seal_and_no_warning()
    {
        var path = await SealedFile();

        var (window, vm) = Show(path);

        var banner = window.GetVisualDescendants().OfType<Border>().Single(b => b.Name == "ReadOnlyBanner");
        Assert.True(banner.IsVisible);
        Assert.Contains("Siegel: ", vm.ReadOnlyReason, StringComparison.Ordinal);
        Assert.False(window.GetVisualDescendants().OfType<Border>().Single(b => b.Name == "SealWarningBanner").IsVisible);
        Capture(window, "seal-intact.png");
    }

    [AvaloniaFact]
    public async Task An_edited_file_shows_the_seal_warning()
    {
        var path = await SealedFile();
        await using (var cn = new SqliteConnection($"Data Source={path}"))
        {
            await cn.OpenAsync();
            await using var cmd = cn.CreateCommand();
            cmd.CommandText = "UPDATE etb_entries SET text = 'Lage unklar' WHERE text = 'Lage unter Kontrolle';";
            Assert.Equal(1, await cmd.ExecuteNonQueryAsync());
        }

        SqliteConnection.ClearAllPools();

        // A file without attachments has nothing to hash, so its check completes before Show returns.
        var (window, vm) = Show(path);

        var warning = window.GetVisualDescendants().OfType<Border>().Single(b => b.Name == "SealWarningBanner");
        Assert.True(warning.IsVisible);
        Assert.Equal(
            "Siegel verletzt: Inhalt weicht vom Siegel ab. Die Datei stimmt nicht mehr mit dem versiegelten Stand überein.",
            vm.SealWarning);
        Capture(window, "seal-broken.png");
    }

    private async Task<string> SealedFile()
    {
        var path = Path.Join(_dir, "uebung.fwincident");
        var store = new IncidentStore();
        var session = TestSession.StartNew(
            store,
            new FixedClock(),
            new SessionOperator(AnonymizedExampleData.OperatorSurname, "FFB 12/1"),
            path,
            new[] { ("Blaulicht aus?", false) },
            Array.Empty<(string, bool)>(),
            keyword: "B3P");
        session.AddJournalEntry(EtbDirection.Incoming, "Lage unter Kontrolle", from: "ILS");
        session.Close();
        await store.FlushAsync();
        SqliteConnection.ClearAllPools();
        return path;
    }

    private static (Window Window, IncidentWorkspaceViewModel Vm) Show(string path)
    {
        var clock = new FixedClock();
        var session = LocalIncidentSession.OpenReadOnly(new IncidentStore(), clock, path);
        var vm = new IncidentWorkspaceViewModel(
            session,
            clock,
            new ManualTicker(),
            WorkspaceRenderHelper.MasterData(),
            new FakeDialogs(),
            new NoopAlarmService(),
            new NoopIncidentHostController());
        var window = new Window { Content = new IncidentWorkspaceView { DataContext = vm }, Width = 1280, Height = 720 };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        return (window, vm);
    }

    private static void Capture(Window window, string name)
    {
        var dir = Environment.GetEnvironmentVariable("RENDER_OUT");
        if (string.IsNullOrWhiteSpace(dir))
        {
            return;
        }

        Directory.CreateDirectory(dir);
        using var frame = window.CaptureRenderedFrame()!;
        frame.SavePng(Path.Join(dir, name));
    }
}
