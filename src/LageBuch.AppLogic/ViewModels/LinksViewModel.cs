using System.Diagnostics.CodeAnalysis;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LageBuch.AppLogic.Services;
using LageBuch.Persistence.MasterData;

namespace LageBuch.AppLogic.ViewModels;

/// <summary>
/// Quick-access list of the global Links Stammdaten, shown as a read-only tab in the incident
/// workspace so a link can be opened while working an Einsatz. Unlike <see cref="FilesViewModel"/>
/// this holds no <see cref="LageBuch.Sync.IIncidentSession"/> and mutates nothing — Links are
/// global master data, not incident state, so opening one is not an incident action.
/// </summary>
public sealed partial class LinksViewModel : ObservableObject
{
    private readonly IFileDialogService _dialogs;

    public LinksViewModel(IReadOnlyList<Link> links, IFileDialogService dialogs)
    {
        _dialogs = dialogs;
        Links = links;
    }

    public IReadOnlyList<Link> Links { get; }

    [ObservableProperty]
    private string? _errorMessage;

    /// <summary>
    /// Refuses anything but http(s) before it reaches the OS: on desktop, OpenUrlAsync ultimately
    /// runs Process.Start with UseShellExecute=true, which resolves arbitrary URI handlers and even
    /// local executable paths, and a Link's URL can come from an imported Stammdaten JSON file, not
    /// just what the user themselves typed here.
    /// </summary>
    [RelayCommand]
    [SuppressMessage(
        "Design",
        "CA1031",
        Justification = "Deliberately broad: any launcher failure surfaces in the view instead of crashing it.")]
    private async Task OpenAsync(Link link)
    {
        ErrorMessage = null;
        var candidate = link.Url.Contains("://", StringComparison.Ordinal) ? link.Url : $"https://{link.Url}";
        if (!Uri.TryCreate(candidate, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            ErrorMessage = $"„{link.Name}“ hat keine gültige http(s)-Adresse.";
            return;
        }

        try
        {
            await _dialogs.OpenUrlAsync(uri.AbsoluteUri);
        }
        catch (Exception ex)
        {
            // No default browser/URL handler registered (a minimal OS install, or no app on
            // Android able to resolve Intent.ActionView) throws out of the platform launcher.
            ErrorMessage = $"„{link.Name}“ konnte nicht geöffnet werden: {ex.Message}";
        }
    }
}
