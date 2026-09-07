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
using LageBuch.Persistence.MasterData;

namespace LageBuch.Acceptance.Tests;

// #262: the workspace footer's PDF export outcome line -- previously there was no feedback
// beyond the OS save dialog closing. Before/after pair for the PR (the "before" state has no
// status line at all; "after" shows it once an export completes).
public class PdfExportStatusRenderTests
{
    // A minimal stand-in for TestPdfExporter (LageBuch.AppLogic.Tests) -- this project has no
    // reference to LageBuch.Documents/QuestPDF, so this returns fixed "%PDF" bytes rather than a
    // real render. Good enough to exercise CanExport/the write-to-disk path for a screenshot.
    private sealed class FakePdfExporter : IIncidentPdfExporter
    {
        public bool CanExport => true;

        public byte[] Generate(Incident incident, IReadOnlyDictionary<Guid, byte[]> fileBytes, IReadOnlyDictionary<Guid, string> pdfAttachmentPaths, IncidentPdfSections sections = IncidentPdfSections.All) =>
            [0x25, 0x50, 0x44, 0x46, 0x2D]; // "%PDF-"
    }

    // The shared FakeDialogs in this project always returns null from PickExportPdfAsync -- this
    // variant returns a caller-supplied path, mirroring AttachmentDialogs' relationship to it.
    private sealed class ExportDialogs : IFileDialogService
    {
        private readonly string _exportPath;

        public ExportDialogs(string exportPath) => _exportPath = exportPath;

        public Task<string?> PickSaveAsync(string suggestedFileName, string? initialFolder = null) => Task.FromResult<string?>("/x.fwincident");

        public Task<string?> PickOpenAsync() => Task.FromResult<string?>(null);

        public Task<string?> PickExportPdfAsync(string suggestedFileName) => Task.FromResult<string?>(_exportPath);

        public Task<string?> PickImportJsonAsync() => Task.FromResult<string?>(null);

        public Task<string?> PickExportJsonAsync(string suggestedFileName) => Task.FromResult<string?>(null);

        public Task<string?> PickAttachmentAsync() => Task.FromResult<string?>(null);

        public Task OpenFileAsync(string path) => Task.CompletedTask;

        public Task OpenUrlAsync(string url) => Task.CompletedTask;

        public Task ShareFileAsync(string path, string mimeType) => Task.CompletedTask;
    }

    private static (Window Window, IncidentWorkspaceViewModel Vm, string ExportPath) ShowWorkspace()
    {
        var exportPath = Path.Combine(Path.GetTempPath(), $"lagebuch-render-export-{Guid.NewGuid():N}.pdf");
        var session = LocalIncidentSession.StartNew(
            new FakeStore(),
            new FixedClock(),
            new SessionOperator(AnonymizedExampleData.OperatorSurname, "FFB 12/1"),
            "/x.fwincident",
            Array.Empty<(string, bool)>(),
            Array.Empty<(string, bool)>());
        var vm = new IncidentWorkspaceViewModel(
            session,
            new FixedClock(),
            new ManualTicker(),
            WorkspaceRenderHelper.MasterData(),
            new ExportDialogs(exportPath),
            new NoopAlarmService(),
            new NoopIncidentHostController(),
            new FakePdfExporter());

        var window = new Window { Content = new IncidentWorkspaceView { DataContext = vm }, Width = 1920, Height = 1032 };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        return (window, vm, exportPath);
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
        frame.SavePng(Path.Combine(dir, name));
    }

    [AvaloniaFact]
    public void Before_export_no_status_line_is_shown()
    {
        var (window, vm, _) = ShowWorkspace();

        Assert.Null(vm.ExportStatus);
        Assert.DoesNotContain(
            window.GetVisualDescendants().OfType<TextBlock>(),
            t => t.Name == "ExportStatusText" && t.IsVisible);
        Capture(window, "pdf-export-status-before.png");
    }

    [AvaloniaFact]
    public async Task After_a_successful_export_the_status_line_shows_the_path()
    {
        var (window, vm, exportPath) = ShowWorkspace();

        vm.ExportPdfCommand.Execute(null);
        await vm.PendingPdfExportOptions!.ExportCommand.ExecuteAsync(null);
        Dispatcher.UIThread.RunJobs();

        try
        {
            Assert.Contains(exportPath, vm.ExportStatus, StringComparison.Ordinal);
            var statusText = window.GetVisualDescendants().OfType<TextBlock>().First(t => t.Name == "ExportStatusText");
            Assert.True(statusText.IsVisible);
            Assert.Contains(exportPath, statusText.Text, StringComparison.Ordinal);
            Capture(window, "pdf-export-status-after.png");
        }
        finally
        {
            File.Delete(exportPath);
        }
    }
}
