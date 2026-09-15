using System.Diagnostics.CodeAnalysis;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LageBuch.AppLogic.Services;

namespace LageBuch.AppLogic.ViewModels;

/// <summary>
/// Content of the "Über" overlay: what the app is, who publishes it, and where its source lives.
/// Pure display data plus two commands — the host clears the overlay via <see cref="Closed"/>,
/// and repository links go through <see cref="IFileDialogService.OpenUrlAsync"/> like every other
/// link in the app (works on desktop and Android alike).
/// </summary>
public sealed partial class AboutViewModel : ObservableObject
{
    private const string RepoUrl = "https://github.com/CodeForFire/lagebuch";

    private readonly IFileDialogService _dialogs;

    public AboutViewModel(IFileDialogService dialogs, string version)
    {
        _dialogs = dialogs;
        Version = version;
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
