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

    public string AppName => "Lagebuch";
    public string Descriptor => "Einsatzdokumentation";
    public string Version { get; }
    public string RepositoryUrl => RepoUrl;

    // Kept in sync with the LICENSE file in the repo root.
    public string LicenseLine => "Veröffentlicht unter der MIT-Lizenz.";
    public string CopyrightLine => "Copyright © 2026 Thomas Müller";

    [ObservableProperty]
    private string? _errorMessage;

    /// <summary>Raised after Close so the host removes the overlay.</summary>
    public event EventHandler? Closed;

    [RelayCommand]
    private void Close() => Closed?.Invoke(this, EventArgs.Empty);

    [RelayCommand]
    private async Task OpenRepositoryAsync()
    {
        ErrorMessage = null;
        try
        {
            await _dialogs.OpenUrlAsync(RepositoryUrl);
        }
        catch (Exception ex)
        {
            // No browser/URL handler registered on this machine (a minimal offline install) throws
            // out of the platform launcher; report it in place instead of crashing the dialog.
            ErrorMessage = $"„{RepositoryUrl}“ konnte nicht geöffnet werden: {ex.Message}";
        }
    }
}
