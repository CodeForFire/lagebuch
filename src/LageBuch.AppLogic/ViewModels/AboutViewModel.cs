using System.Diagnostics.CodeAnalysis;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LageBuch.AppLogic.Services;

namespace LageBuch.AppLogic.ViewModels;

/// <summary>
/// Content of the "Über" overlay: what the app is, who publishes it, and where its source lives.
/// Display data plus a few commands — the host clears the overlay via <see cref="Closed"/>,
/// and repository links go through <see cref="IFileDialogService.OpenUrlAsync"/> like every other
/// link in the app (works on desktop and Android alike). It also hosts the sound check (#603).
/// </summary>
public sealed partial class AboutViewModel : ObservableObject
{
    private const string RepoUrl = "https://github.com/CodeForFire/lagebuch";

    private readonly IFileDialogService _dialogs;
    private readonly IAlarmService? _alarm;

    public AboutViewModel(IFileDialogService dialogs, string version, IAlarmService? alarm = null)
    {
        _dialogs = dialogs;
        Version = version;
        _alarm = alarm;
    }

    /// <summary>Whether this device has an alarm service to test; the button is hidden otherwise.</summary>
    public bool CanTestSound => _alarm is not null;

    /// <summary>The outcome of the last sound check, in German, or null before the first.</summary>
    [ObservableProperty]
    private string? _soundTestResult;

    /// <summary>True when the last sound check could not play anything at all.</summary>
    [ObservableProperty]
    private bool _soundTestFailed;

    /// <summary>
    /// Plays the Rückzugsalarm once (#603). Nothing in software can tell a muted laptop from a
    /// working one, so a success hands the judgement to the listener; only a failure the device can
    /// see — no player, no clip, no audio on this platform — is stated as fact.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanTestSound))]
    private async Task TestSoundAsync()
    {
        if (_alarm is null)
        {
            return;
        }

        SoundTestFailed = false;
        SoundTestResult = "Rückzugsalarm wird abgespielt …";
        var played = await _alarm.PlayTestAsync();
        SoundTestFailed = !played;
        SoundTestResult = played
            ? "Rückzugsalarm abgespielt. Nichts gehört? Lautstärke und Lautsprecher prüfen."
            : "Auf diesem Gerät konnte kein Ton abgespielt werden. Alarme erscheinen nur auf dem Bildschirm.";
    }

    // No AppName/Descriptor here: the About card shows the project badge, which carries the
    // wordmark and the slogan as artwork (see AboutView.axaml).
    public string Version { get; }

    [SuppressMessage("Design", "CA1056", Justification = "RepositoryUrl is a display/launch string handed to IFileDialogService.OpenUrlAsync; System.Uri would add parse/validation behavior with no benefit here.")]
    [SuppressMessage("Performance", "CA1822", Justification = "XAML {Binding} target in AboutView; binding requires an instance property.")]
    public string RepositoryUrl => RepoUrl;

    // Kept in sync with the LICENSE file in the repo root.
    [SuppressMessage("Performance", "CA1822", Justification = "XAML {Binding} target in AboutView; binding requires an instance property.")]
    public string LicenseLine => "Veröffentlicht unter der MIT-Lizenz.";

    [SuppressMessage("Performance", "CA1822", Justification = "XAML {Binding} target in AboutView; binding requires an instance property.")]
    public string CopyrightLine => "Copyright © 2026 Thomas Müller";

    [ObservableProperty]
    private string? _errorMessage;

    /// <summary>Raised after Close so the host removes the overlay.</summary>
    public event EventHandler? Closed;

    [RelayCommand]
    private void Close() => Closed?.Invoke(this, EventArgs.Empty);

    // The URL is on screen in the About card, so it is what the error names.
    [RelayCommand]
    private async Task OpenRepositoryAsync() =>
        ErrorMessage = await UrlLauncher.TryOpenAsync(_dialogs, RepositoryUrl, RepositoryUrl);
}
