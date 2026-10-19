using System.Net.Sockets;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Threading;
using LageBuch.App.Shared.Views;
using LageBuch.AppLogic;
using LageBuch.AppLogic.Services;
using LageBuch.AppLogic.ViewModels;
using LageBuch.Persistence.MasterData;

namespace LageBuch.Acceptance.Tests;

// #655: a failure to share or export reached the screen as raw English exception text, trimmed
// to an ellipsis with the rest only in a hover tooltip -- which a tablet does not have. Both
// lines now carry a fixed German reason and wrap instead of trimming.
public class FailureStatusRenderTests
{
    private const string PortTaken = "Failed to bind to address https://[::]:5859: address already in use.";

    [AvaloniaFact]
    public async Task Share_and_export_failures_are_german_and_wrap_rather_than_trim()
    {
        var vm = WorkspaceRenderHelper.BuildEditableWorkspaceWithAllBars(new FailingHost());
        var view = new IncidentWorkspaceView { DataContext = vm };
        var window = new Window { Content = view, Width = 1280, Height = 800 };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        await vm.ToggleSharingCommand.ExecuteAsync(null);
        vm.ExportStatus = FailureReasons.ForExport(new IOException("The process cannot access the file", unchecked((int)0x80070020)));
        Dispatcher.UIThread.RunJobs();
        Capture(window, "share-export-failure.png");

        var share = view.GetControl<TextBlock>("ShareError");
        var export = view.GetControl<TextBlock>("ExportStatusText");
        Assert.Equal(FailureReasons.SharingPortInUse, share.Text);
        Assert.Equal(FailureReasons.ExportFileInUse, export.Text);
        foreach (var line in new[] { share, export })
        {
            Assert.True(line.IsEffectivelyVisible);
            Assert.Equal(TextWrapping.Wrap, line.TextWrapping);
            Assert.Equal(TextTrimming.None, line.TextTrimming);
        }
    }

    // Opt-in PNG capture for the PR's before/after pair; a plain test run writes nothing.
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

    private sealed class FailingHost : IIncidentHostController
    {
        public bool CanHost => true;

        public bool IsHosting => false;

        public string? ShareHint => null;

        public string? SharePin => null;

        public string? ShareKennung => null;

        public bool JoinsClosed => false;

        public event EventHandler? JoinsClosedChanged
        {
            add { }
            remove { }
        }

        public void RenewPin()
        {
        }

        public Task StartAsync(LocalIncidentSession session, MasterDataSet masterData, CancellationToken cancellationToken = default) =>
            throw new IOException(PortTaken, new SocketException((int)SocketError.AddressAlreadyInUse));

        public Task StopAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}
