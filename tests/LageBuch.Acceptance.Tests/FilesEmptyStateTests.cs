using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using LageBuch.App.Shared.Views;
using LageBuch.AppLogic.Services;
using LageBuch.AppLogic.ViewModels;
using LageBuch.Domain;

namespace LageBuch.Acceptance.Tests;

public class FilesEmptyStateTests
{
    [AvaloniaFact]
    public void Empty_hint_and_list_switch_when_files_are_added_and_removed()
    {
        var session = TestSession.StartNew(
            new FakeStore(),
            new FixedClock(),
            new SessionOperator("Müller", "FFB 12/1"),
            "/x.lagebuch",
            Array.Empty<(string, bool)>(),
            Array.Empty<(string, bool)>());
        var workspace = new IncidentWorkspaceViewModel(
            session,
            new FixedClock(),
            new NoopTicker(),
            WorkspaceRenderHelper.MasterData(),
            new FakeDialogs(),
            new NoopAlarmService(),
            new NoopIncidentHostController());
        var vm = workspace.Files;
        var window = new Window
        {
            Content = new IncidentWorkspaceView { DataContext = workspace },
            Width = 1280,
            Height = 800,
        };
        window.Show();
        try
        {
            Dispatcher.UIThread.RunJobs();
            WorkspaceRenderHelper.SelectTab(window, "DATEIEN");
            var view = window.GetVisualDescendants().OfType<FilesView>().Single();
            Capture(window, "files-empty.png");
            var hint = view.GetControl<TextBlock>("EmptyText");
            var list = view.GetControl<Border>("FilesListFrame");
            Assert.Equal("Keine Dateien angehängt.", hint.Text);
            Assert.True(hint.IsVisible);
            Assert.False(list.IsVisible);

            var file = session.Incident.AddFile(new FixedClock(), session.Operator!, "probe.pdf", "application/pdf", 12);
            vm.Sync();
            Dispatcher.UIThread.RunJobs();
            Assert.False(hint.IsVisible);
            Assert.True(list.IsVisible);
            Capture(window, "files-populated.png");

            session.Incident.RemoveFile(new FixedClock(), session.Operator!, file.Id);
            vm.Sync();
            Dispatcher.UIThread.RunJobs();
            Assert.True(hint.IsVisible);
            Assert.False(list.IsVisible);
        }
        finally
        {
            window.Close();
        }
    }

    private static void Capture(Window window, string name)
    {
        var output = Environment.GetEnvironmentVariable("RENDER_OUT");
        if (string.IsNullOrWhiteSpace(output))
        {
            return;
        }

        Directory.CreateDirectory(output);
        using var frame = window.CaptureRenderedFrame()!;
        frame.SavePng(Path.Join(output, name));
    }
}
