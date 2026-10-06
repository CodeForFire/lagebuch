using Avalonia.Controls;
using Avalonia.Threading;
using LageBuch.App.Shared.Views;
using LageBuch.AppLogic.Services;
using LageBuch.AppLogic.ViewModels;
using LageBuch.Domain;
using LageBuch.Persistence.MasterData;

namespace LageBuch.Acceptance.Tests;

// The real shell, MainView, which hosts the global shortcuts, on a clock and ticker the test
// drives. Shared by #545's walkthrough and keystroke budget, which reach every module and warning
// the way a Lagebuchführer does.
internal sealed record ShellHarness(Window Window, HomeViewModel Home, MainWindowViewModel Main, FixedClock Clock, ManualTicker Ticker)
{
    public IncidentWorkspaceViewModel Workspace => Assert.IsType<IncidentWorkspaceViewModel>(Main.CurrentView);

    // Starts on Home. The PDF save picker answers with pdfPath, so an export really writes.
    public static ShellHarness ShowHome(string? pdfPath = null)
    {
        var clock = new FixedClock();
        var ticker = new ManualTicker();
        var dialogs = new PdfDialogs(pdfPath);
        var masterData = new StaticMasterData(WorkspaceRenderHelper.MasterData());
        var home = new HomeViewModel(
            new FakeStore(),
            masterData,
            new EmptyRecent(),
            dialogs,
            clock,
            ticker,
            new NoopAlarmService(),
            new NoopIncidentHostController(),
            "0.1.0",
            pdfExporter: new ExportablePdf());
        var main = new MainWindowViewModel(home, new MasterDataEditorViewModel(masterData, dialogs, new NoFiles()), dialogs, "0.1.0");

        var view = new MainView();
        view.AttachViewModel(main);
        var window = new Window { Content = view, Width = 1920, Height = 1032 };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        return new(window, home, main, clock, ticker);
    }

    // Starts in an open, editable incident; the keys it took to get there are the walkthrough's.
    public static ShellHarness ShowIncident()
    {
        var shell = ShowHome();
        shell.Home.NewIncidentCommand.Execute(new NewIncidentRequest(new SessionOperator(AnonymizedExampleData.OperatorSurname, "Florian Testort 12/1")));
        Dispatcher.UIThread.RunJobs();
        _ = shell.Workspace;
        return shell;
    }

    // FakeDialogs, but the PDF save picker answers with a real path the export can write to.
    private sealed class PdfDialogs(string? pdfPath) : IFileDialogService
    {
        public Task<string?> PickSaveAsync(string s, string? initialFolder = null) => Task.FromResult<string?>("/x.fwincident");

        public Task<string?> PickOpenAsync() => Task.FromResult<string?>(null);

        public Task<string?> PickExportPdfAsync(string s) => Task.FromResult(pdfPath);

        public Task<string?> PickImportJsonAsync() => Task.FromResult<string?>(null);

        public Task<string?> PickExportJsonAsync(string s) => Task.FromResult<string?>(null);

        public Task<string?> PickAttachmentAsync() => Task.FromResult<string?>(null);

        public Task OpenFileAsync(string path) => Task.CompletedTask;

        public Task OpenUrlAsync(string url) => Task.CompletedTask;

        public Task OpenMailAsync(string address) => Task.CompletedTask;

        public Task OpenPhoneAsync(string number) => Task.CompletedTask;

        public Task ShareFileAsync(string path, string mimeType) => Task.CompletedTask;
    }

    private sealed class StaticMasterData(MasterDataSet set) : IMasterDataProvider
    {
        public MasterDataSet Get() => set;

        public void Save(MasterDataSet s)
        {
        }
    }

    private sealed class NoFiles : IMasterDataFileService
    {
        public MasterDataImportResult Read(string path) => new(MasterDataSet.Empty, Array.Empty<string>());

        public void Write(string path, MasterDataSet set)
        {
        }
    }

    private sealed class EmptyRecent : IRecentFilesStore
    {
        public IReadOnlyList<string> GetRecent() => Array.Empty<string>();

        public void Add(string path)
        {
        }

        public void Remove(string path)
        {
        }
    }
}
