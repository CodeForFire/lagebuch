using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media;
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
        var sessionOperator = new SessionOperator("Müller", "FFB 12/1");
        var session = TestSession.StartNew(
            new FakeStore(),
            new FixedClock(),
            sessionOperator,
            "/x.lagebuch",
            Array.Empty<(string, bool)>(),
            Array.Empty<(string, bool)>());
        using var workspace = new IncidentWorkspaceViewModel(
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
            var dropZone = view.GetControl<Border>("FilesDropZone");
            var emptyPanel = view.GetControl<StackPanel>("EmptyStatePanel");
            var hint = view.GetControl<TextBlock>("EmptyText");
            var instruction = view.GetControl<TextBlock>("EmptyInstructionText");
            var list = view.GetControl<Border>("FilesListFrame");

            // Empty state: hint and instruction visible, list frame hidden
            Assert.Equal("Keine Dateien angehängt.", hint.Text);
            Assert.Equal("Dateien hierher ziehen oder über \"Datei hinzufügen\" anhängen.", instruction.Text);
            Assert.True(emptyPanel.IsVisible);
            Assert.True(hint.IsVisible);
            Assert.True(instruction.IsVisible);
            Assert.False(list.IsVisible);
            Capture(window, "files-empty.png");

            // Bounds stability across drag-over in empty state
            var emptyBoundsAtRest = emptyPanel.Bounds;
            var transfer = new DataTransfer();
            transfer.Add(DataTransferItem.CreateText("probe.pdf"));

            dropZone.RaiseEvent(new DragEventArgs(DragDrop.DragEnterEvent, transfer, dropZone, new Point(10, 10), KeyModifiers.None));
            Dispatcher.UIThread.RunJobs();
            Assert.Contains("drag-over", dropZone.Classes);
            AssertBoundsNear(emptyBoundsAtRest, emptyPanel.Bounds);
            Capture(window, "files-empty-dragover.png");

            dropZone.RaiseEvent(new DragEventArgs(DragDrop.DragLeaveEvent, transfer, dropZone, new Point(10, 10), KeyModifiers.None));
            Dispatcher.UIThread.RunJobs();
            Assert.DoesNotContain("drag-over", dropZone.Classes);
            AssertBoundsNear(emptyBoundsAtRest, emptyPanel.Bounds);

            // Populated state: add file, list frame visible, empty hint hidden
            var file = session.Incident.AddFile(new FixedClock(), sessionOperator, "probe.pdf", "application/pdf", 12);
            vm.Sync();
            Dispatcher.UIThread.RunJobs();
            Assert.False(emptyPanel.IsVisible);
            Assert.False(hint.IsEffectivelyVisible);
            Assert.False(instruction.IsEffectivelyVisible);
            Assert.True(list.IsVisible);
            Capture(window, "files-populated.png");

            // Single visible ring and bounds stability during drag-over in populated state
            var listBoundsAtRest = list.Bounds;
            Assert.Equal(window.FindResource("HairlineBrush"), list.BorderBrush);

            dropZone.RaiseEvent(new DragEventArgs(DragDrop.DragEnterEvent, transfer, dropZone, new Point(10, 10), KeyModifiers.None));
            Dispatcher.UIThread.RunJobs();
            Assert.Contains("drag-over", dropZone.Classes);
            Assert.Equal(window.FindResource("SignalBrush"), dropZone.BorderBrush);
            Assert.Equal(Brushes.Transparent, list.BorderBrush);
            AssertBoundsNear(listBoundsAtRest, list.Bounds);
            Capture(window, "files-populated-dragover.png");

            dropZone.RaiseEvent(new DragEventArgs(DragDrop.DragLeaveEvent, transfer, dropZone, new Point(10, 10), KeyModifiers.None));
            Dispatcher.UIThread.RunJobs();
            Assert.DoesNotContain("drag-over", dropZone.Classes);
            Assert.Equal(window.FindResource("HairlineBrush"), list.BorderBrush);
            AssertBoundsNear(listBoundsAtRest, list.Bounds);

            // Populated -> empty transition: remove file, empty state restored
            session.Incident.RemoveFile(new FixedClock(), sessionOperator, file.Id);
            vm.Sync();
            Dispatcher.UIThread.RunJobs();
            Assert.True(emptyPanel.IsVisible);
            Assert.True(hint.IsEffectivelyVisible);
            Assert.True(instruction.IsEffectivelyVisible);
            Assert.False(list.IsVisible);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void Read_only_incident_hides_drop_instruction_and_disallows_drop()
    {
        var sessionOperator = new SessionOperator("Müller", "FFB 12/1");
        var session = TestSession.StartNew(
            new FakeStore(),
            new FixedClock(),
            sessionOperator,
            "/x.lagebuch",
            Array.Empty<(string, bool)>(),
            Array.Empty<(string, bool)>());
        session.Close();
        using var workspace = new IncidentWorkspaceViewModel(
            session,
            new FixedClock(),
            new NoopTicker(),
            WorkspaceRenderHelper.MasterData(),
            new FakeDialogs(),
            new NoopAlarmService(),
            new NoopIncidentHostController());
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
            var hint = view.GetControl<TextBlock>("EmptyText");
            var instruction = view.GetControl<TextBlock>("EmptyInstructionText");
            var dropZone = view.GetControl<Border>("FilesDropZone");

            Assert.Equal("Keine Dateien angehängt.", hint.Text);
            Assert.True(hint.IsVisible);
            Assert.False(instruction.IsVisible);
            Assert.False(DragDrop.GetAllowDrop(dropZone));
            Capture(window, "files-readonly-empty.png");
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void Narrow_empty_state_renders_readable_wrapped_instruction()
    {
        var sessionOperator = new SessionOperator("Müller", "FFB 12/1");
        var session = TestSession.StartNew(
            new FakeStore(),
            new FixedClock(),
            sessionOperator,
            "/x.lagebuch",
            Array.Empty<(string, bool)>(),
            Array.Empty<(string, bool)>());
        using var workspace = new IncidentWorkspaceViewModel(
            session,
            new FixedClock(),
            new NoopTicker(),
            WorkspaceRenderHelper.MasterData(),
            new FakeDialogs(),
            new NoopAlarmService(),
            new NoopIncidentHostController());
        var window = new Window
        {
            Content = new IncidentWorkspaceView { DataContext = workspace },
            Width = 420,
            Height = 800,
        };
        window.Show();
        try
        {
            Dispatcher.UIThread.RunJobs();
            WorkspaceRenderHelper.SelectTab(window, "DATEIEN");
            var view = window.GetVisualDescendants().OfType<FilesView>().Single();
            var instruction = view.GetControl<TextBlock>("EmptyInstructionText");
            Assert.True(instruction.IsVisible);
            Assert.Equal(TextWrapping.Wrap, instruction.TextWrapping);
            Capture(window, "files-narrow-empty.png");
        }
        finally
        {
            window.Close();
        }
    }

    private static void AssertBoundsNear(Rect expected, Rect actual, double tolerance = 0.5)
    {
        Assert.True(
            Math.Abs(expected.Left - actual.Left) <= tolerance
            && Math.Abs(expected.Top - actual.Top) <= tolerance
            && Math.Abs(expected.Width - actual.Width) <= tolerance
            && Math.Abs(expected.Height - actual.Height) <= tolerance,
            $"Expected bounds {expected} but got {actual} within tolerance {tolerance}");
    }

    private static void Capture(Window window, string name)
    {
        var output = Environment.GetEnvironmentVariable("RENDER_OUT");
        if (string.IsNullOrWhiteSpace(output))
        {
            return;
        }

        Directory.CreateDirectory(output);
        using var frame = window.CaptureRenderedFrame();
        Assert.NotNull(frame);
        frame.SavePng(Path.Join(output, name));
    }
}
