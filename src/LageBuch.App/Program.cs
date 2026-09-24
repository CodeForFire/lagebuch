using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using LageBuch.App.Services;
using LageBuch.AppLogic.Services;
using LageBuch.AppLogic.ViewModels;
using LageBuch.Domain.Time;
using LageBuch.Speech;
using LageBuch.Speech.Sherpa;
using LageBuch.Sync;

namespace LageBuch.App;

internal static class Program
{
    /// <summary>
    /// Loads the shipped voice, or returns null if anything about it is wrong.
    /// </summary>
    /// <remarks>
    /// Called on the audio worker at the first cue, never on the UI thread. A missing or broken
    /// model degrades to the bundled clips rather than taking the app down -- the same contract the
    /// clips have always had.
    /// </remarks>
    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "Design",
        "CA1031",
        Justification = "A voice that will not load must leave the bundled cue clips working, not crash the app mid-Einsatz.")]
    private static SherpaSpeechSynthesizer? LoadShippedVoice()
    {
        try
        {
            return SpeechModelLocator.TryLocate(out var root, out _)
                ? SherpaSpeechSynthesizer.Load(root, VoiceCatalog.Shipped)
                : null;
        }
        catch
        {
            return null;
        }
    }

    [STAThread]
    public static void Main(string[] args)
    {
        LageBuch.App.Shared.App.CreateMainViewModel = CreateMainViewModel;
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    private static MainWindowViewModel CreateMainViewModel()
    {
        // Resolved lazily at call time, once desktop.MainWindow has been assigned — avoids the
        // chicken-and-egg problem of needing a TopLevel before the Window that provides one exists.
        var dialogs = new StorageProviderFileDialogService(() =>
            (Avalonia.Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow);
        var clock = new SystemClock();
        var version = typeof(Program).Assembly.GetName().Version?.ToString() ?? "0.0.0";
        var uiDispatcher = new LageBuch.App.Shared.Services.AvaloniaUiDispatcher();
        var store = new IncidentStore();

        // CA2000: an app-lifetime singleton, disposed via the desktopLifetime.Exit hook below rather
        // than a using block — same shape as SerialAudioQueue's own CA1001 suppression.
#pragma warning disable CA2000
        // The reason is reported, not discarded. Speech failing open -- falling back to the bundled
        // clips with no signal at all -- is how this shipped silent once already: the models were
        // never where the app looked, every test passed, and the only symptom was a task cue that
        // still beeped.
        var located = false;
        string? speechReason;

        if (SpeechModelLocator.TryLocate(out var modelsRoot, out var missingModels))
        {
            located = Directory.Exists(SpeechModelLocator.VoiceDirectory(modelsRoot, VoiceCatalog.Shipped));
            speechReason = located
                ? null
                : $"Stimme '{VoiceCatalog.Shipped.Id}' fehlt in {modelsRoot} "
                    + "(packaging/speech/fetch-voices.sh).";
        }
        else
        {
            speechReason = missingModels;
        }

        if (!located)
        {
            Console.Error.WriteLine($"Sprachausgabe nicht verfügbar: {speechReason}");
        }

        // The factory is not called here: SystemAlarmService loads the voice on its audio worker,
        // the first time a cue actually fires. Loading a Piper model takes about two seconds, and
        // paying that at startup would delay the window for something that may never be needed.
        var alarms = new SystemAlarmService(located ? LoadShippedVoice : null)
        {
            CanSpeak = located,
            SpeechUnavailableReason = located ? null : speechReason,
        };
#pragma warning restore CA2000

        // Best-effort: drain IncidentStore's background writer (issue #167 P0 #1) before the process
        // actually exits, so the last queued save isn't lost. Blocking briefly here is fine — Exit
        // fires once shutdown is already underway, and FlushAsync's own writer thread completes the
        // wait, so there's no deadlock risk. Also cleans up the alarm temp WAVs (#167 P2).
        if (Avalonia.Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktopLifetime)
        {
            desktopLifetime.Exit += (_, _) => store.FlushAsync().GetAwaiter().GetResult();
            desktopLifetime.Exit += (_, _) => alarms.Dispose();
        }

        return LageBuch.App.Shared.CompositionRoot.CreateMainWindowViewModel(
            store,
            new MasterDataProvider(AppPaths.MasterDataDbPath),
            new JsonRecentFilesStore(AppPaths.RecentFilesJsonPath),
            dialogs,
            clock,
            new LageBuch.App.Shared.Services.DispatcherTimerTicker(),
            alarms,
            new MasterDataFileService(),
            new IncidentHostController(clock, version, uiDispatcher),
            uiDispatcher,
            version,
            new JsonLastSaveFolderStore(AppPaths.LastSaveFolderJsonPath),
            AppPaths.AttachmentCacheDir,
            trustStore: new JsonTrustStore(AppPaths.TrustJsonPath),
            pdfExporter: new QuestPdfIncidentExporter(),
            lastPdfExport: new JsonLastPdfExportStore(AppPaths.LastPdfExportJsonPath),
            lastJoinHost: new JsonLastJoinHostStore(AppPaths.LastJoinHostJsonPath));
    }

    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<LageBuch.App.Shared.App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}
